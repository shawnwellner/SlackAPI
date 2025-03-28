using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RMI.Slack.Database;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RMI.Slack {
    internal static class SlackMessage {
        public static object PostToSlack(this Exception ex, string channel, HttpRequestMessage req) {
            string json = ex.ToJson();
            JObject jObj = JObject.Parse(json);
            jObj.AddFirst(new JProperty("BuildVersion", Settings.BuildVersion));
            jObj.AddFirst(new JProperty("Environment", Settings.SlotName));
            //json = jObj.ToString();

            return jObj.PostToSlack(channel, req);
        }

        public static object PostToSlack(this HttpRequestMessage req, string channel) {
            channel = channel.IsEmpty(Settings.DefaultSlackChannel);
            string body = req.GetRequestBody().Replace("\"SafeSerializationManager\":", "\"_SafeSerializationManager\":");
            req.Properties.Add("RMI_BODY", body);

            JToken jToken;
            string json;

            if(!body.TryToJToken(out jToken)) {
                throw new FormatException("Unable to parse JSON string");
            }
            if( req.Headers.UserAgent.ToString().Matches("Domo-Alert") ) {
                JToken token;
                json = jToken.Value<string>("message");
                if(json.TryToJToken(out token)) {
                    jToken = token;
                } else {                    
                    /*Clean Up JSON: https://regex101.com/r/hrMtiy/1 */
                    json = json.RegExReplace(@"\\r\\n(\s+|((\\t)*))", "");
                    json = $"[{json.RegExReplace(@"}(\s+)?{", @"},{")}]";

                    if(json.TryToJToken(out token)) {
                        JArray jArry = token as JArray;
                        foreach(JObject jObj in jArry) {
                            token = jObj.PostToSlack(channel, req);
                        }
                        return token;
                    }
                }
            }
            return jToken.PostToSlack(channel, req);
        }

        public async static Task<string> PostTest(this HttpRequestMessage req) {
            return await Task.Factory.StartNew(() => {
                string json = new {
                    title = "RMI.SlackAPI",
                    message = "This is a test."
                }.ToJson();

                JObject jObj = JToken.Parse(json)
                    .PostToSlack(Settings.DefaultSlackChannel, req);
                return jObj.ToString();
            });
        }

        private static JObject PostToSlack(string json) {
            try {
                const string url = "https://slack.com/api/chat.postMessage";
                string resp = WebRequest.Post(url, json, "application/json", Settings.SlackAuthToken, 0);

                JObject jObj = JObject.Parse(resp);
                if(jObj.GetValue(false, "ok") == false) {
                    Exception ex = new Exception("Error posting to Slack");
                    ex.Data.SafeAdd("Slack-Response", resp);
                    throw ex;
                }
                return jObj;
            } catch(Exception ex) {
                JObject jObj = JObject.Parse(json);
                ex.Data.SafeAdd("Slack-Post", jObj.ToString());
                Utilities.SendExceptionEmail(ex);
                return null;
            }
        }

        private static string ToSlackMessage(this List<object> list, string channel, string title, string text, string icon_url) {
            title = title.IsEmpty(Settings.AssemblyName);
            var message = new {
                channel,
                text,
                username = title,
                icon_url,
                mrkdwn = true,
                attachments = list.ToArray()
            };
            return ((object)message).ToJson();
        }

        public static JObject PostToSlack(this JToken token, string channel, HttpRequestMessage req) {
            JObject jObject;
            if(token.IsException()) {
                token.AddAnnotation(new JProperty("SlackChannel", channel));
                ExceptionLog.CreateLog(token, req.RequestUri.OriginalString);
                return JObject.FromObject(new { success = true });
            } else {
                jObject = token as JObject;
                jObject.AddDefaultValue("channel", channel);
                jObject.AddDefaultValue("mrkdwn", true);
                jObject.Rename("icon_url", "image", "icon");
                jObject.Rename("text", "message");
                jObject.Rename("username", "title", "from");
                jObject.AppendEnvironment();
            }

            string json = jObject.ToString();
            return PostToSlack(json);
        }

        private static bool IsException(this JToken jToken) {
            if(jToken is JArray) { return true; }
            return (jToken as JObject)?.ContainsKey("InnerException") == true;
        }

        public static JObject SendException(ExceptionLog log) {
            if(log?.LogOnly != false) { return null; }

            Debug.WriteLine($"Send Slack Message - {log.ExCount}");
            //return null;

            const string default_icon = "https://cdn.rmiatl.org/cdn/img/slack/warning.png";
            const string default_text = "Unexpected Exception Occured";

            List<object> list = new List<object>();

            log.FormatException(list);

            string source = $"{log.Source} - {log.Environment}";
            string title = log.JObject.GetValue(source, "title", "from", "username");
            string text = log.JObject.GetValue(default_text, "text", "message");
            string icon = log.JObject.GetValue(default_icon, "image", "icon");
            string channel = Settings.GetSlackChannel(title) ?? log.SlackChannel ?? Settings.DefaultSlackChannel;
            string json = list.ToSlackMessage(channel, title, text, icon);
            return PostToSlack(json);
        }

        private static ExceptionLog FormatException(this ExceptionLog log, List<object> list) {
            const string header_info_key = "Header-Info";
            const string headers_key = "Headers";

            StringBuilder buffer = new StringBuilder();

            CustomException ex = log.Exception;
            
            string source = $"{log.Source} - {log.Environment}";
            buffer.Append($"*Slack-Environment*: {Settings.SlotName}").AppendLine();
            buffer.Append($"*Source*: {source}").AppendLine();
            buffer.Append($"*BuildVersion*: {log.BuildVersion}").AppendLine();
            buffer.Append($"*LogId*: {log.Id}").AppendLine();
            buffer.Append($"*Total Attempts*: {log.ExCount}").AppendLine();

            list.AddAttachment(buffer, null, "danger", log.LinkUrl, $"Exception Details - {ex.Count} Exception(s)");

            if(ex.Data[header_info_key] is JObject header) {
                foreach(var item in header) {
                    buffer.AppendFormat("*{0}*: {1}", item.Key, item.Value).AppendLine();
                }
                ex.Data.Remove(header_info_key);
                list.AddAttachment(buffer, null, "danger", null);
            }

            buffer.Append($"*Exception-Type*: {ex.ClassName}").AppendLine();
            buffer.Append($"*Exception*: {ex.Message}").AppendLine();

            if(ex.Data?.Count > 0) {
                buffer.AppendLine();
                bool hideHeaders = false;
                foreach(string key in ex.Data.Keys) {
                    if(key == headers_key) {
                        hideHeaders = true;
                    } else if(key.HasValue()) {
                        string value = ex.Data[key] as string;
                        if(!hideHeaders && !value.IsJson()) {
                            buffer.AppendFormat("*{0}*: {1}", key, value).AppendLine();
                        }
                    } else {
                        hideHeaders = false;
                        buffer.AppendLine();
                    }
                }
            }
            list.AddAttachment(buffer, null, "danger", ex.HelpLink);
            return log;
        }

        public static T GetValue<T>(this JObject obj, T defaultValue, params string[] aliases) {
            try {
                foreach(string name in aliases) {
                    JToken value = obj.GetValue(name, StringComparison.CurrentCultureIgnoreCase);
                    if(null != value) { return value.Value<T>(); }
                }
            } catch {
                //Ignore Error
            }
            return defaultValue;
        }

        private static void AddAttachment(this List<object> list, StringBuilder text, string title, string color, string author_link = null, string linkText = "Details") {
            if(text.Length == 0) { return; }
            object item;
            color = color ?? "danger";
            if(author_link.HasValue()) {
                item = new { author_name = linkText, author_link, color, text = text.ToString() };
            } else if(title.HasValue()) {
                item = new { title, color, text = text.ToString() };
            } else {
                item = new { color, text = text.ToString() };
            }
            list.Add(item);
            text.Clear();
        }
    }
}