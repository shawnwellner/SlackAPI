using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.Caching;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;

namespace RMI.Slack {
    public static class ExMethods {
        private static readonly ObjectCache cache = MemoryCache.Default;

        public static bool IsEmpty(this string value) {
            return string.IsNullOrEmpty(value);
        }

        public static string IsEmpty(this string value, string defaultValue) {
            if(value.HasValue()) { return value; }
            return defaultValue;
        }

        public static bool HasValue(this string value) {
            return !string.IsNullOrEmpty(value?.Trim());
        }

        public static bool HasValue(this object value) {
            if(value == null) {
                return false;
            } else if(value is string strValue) {
                return strValue.HasValue();
            }
            return true;
        }

        public static int? ToInt(this string value) {
            if(value.IsEmpty()) { return null; }
            if(int.TryParse(value, out int iValue)) {
                return iValue;
            }
            return null;
        }

        public static bool ToBool<T>(this T instance) {
            if(instance == null) { return false; }
            return instance.ToString().Matches("true|yes|1");
        }

        public static string GetCache(this string key) {
            return cache.Get(key.ToLower()) as string;
        }

        public static string Cache(this string value, string key, double expireMinutes) {
            CacheItemPolicy policy = new CacheItemPolicy() {
                AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(expireMinutes)
            };
            cache.Set(key.ToLower(), value, policy);
            return value;
        }


        public static bool IsLowerCase(this string value) {
            if(value.IsEmpty()) { return false; }
            return Regex.IsMatch(value, "^[a-z]+$");
        }

        public static bool IsTitleCase(this string value) {
            if(value.IsEmpty()) { return false; }
            return Regex.IsMatch(value, "^[A-Z]{1}[a-z]+$");
        }

        public static string ToTitleCase(this string value) {
            if(value.IsEmpty()) { return value; }
            string lowerCase = value.ToLower();
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lowerCase);
        }

        public static bool Matches(this string value, string pattern, RegexOptions options = RegexOptions.IgnoreCase) {
            if(value.IsEmpty()) { return false; }
            return Regex.IsMatch(value, pattern, options);
        }

        public static bool Matches(this string value, string pattern, out string[] groups, RegexOptions options = RegexOptions.IgnoreCase) {
            groups = null;
            if(value.IsEmpty()) { return false; }
            MatchCollection matches = Regex.Matches(value, pattern, options);
            List<string> lst = new List<string>();
            foreach(Match match in matches) {
                if(match.Success) {
                    foreach(Group grp in match.Groups) {
                        lst.Add(grp.Value);
                    }
                }
            }
            if(lst.Count > 0) {
                groups = lst.ToArray();
                return true;
            }
            return false;
        }

        public static string RegExEscape(this string value) {
            if(value.IsEmpty()) { return value; }
            return Regex.Escape(value);
        }

        public static bool In(this string value, params string[] values) {
            return values.Contains(value);
        }

        public static string ToJson<T>(this T instance) {
            return JsonConvert.SerializeObject(instance, Formatting.None);
        }

        public static JObject ToJObject<T>(this T instance, Formatting format = Formatting.None) {
            string json = JsonConvert.SerializeObject(instance, format);
            return JObject.Parse(json);
        }

        public static bool TryToJToken(this string json, out JToken jToken) {
            try {
                jToken = JToken.Parse(json);
                return jToken != null;
            } catch {
                jToken = null;
                return false;
            }
        }

        public static string CompressJson(this string json) {
            try {
                var obj = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(obj, Formatting.None);
            } catch { 
                return json;
            }
        }

        public static string GetValue(this IDictionary list, string key, string defaultValue, bool remove = false) {
            if(null == list || key.IsEmpty()) { return defaultValue; }
            if(list.Contains(key)) { 
                string value = list[key] as string;
                if(remove) { list.Remove(key); }
                return value ?? defaultValue; 
            }
            return defaultValue;
        }

        public static T AddSpacer<T>(this T instance) where T : IEnumerable {
            string space = " ";
            if(instance is IDictionary lst1) {
                while(lst1.Contains(space)) {
                    space += " ";
                }
                lst1.Add(space, null);
            } else if(instance is IDictionary<string, object> lst2) {
                while(lst2.ContainsKey(space)) {
                    space += " ";
                }
                lst2.Add(space, null);
            }

            return instance;
        }

        public static IDictionary SafeAdd(this IDictionary list, string key, object value) {
            if(null == list) { return null; }
            if(list.Contains(key)) { return list; }
            try {
                JObject jObj;
                if(value is JValue) {
                    value = ((JValue)value).Value;
                } else if(value is JObject) {
                    jObj = (JObject)value;
                    list.Add(key, string.Empty);
                    foreach(JProperty prop in jObj.Properties()) {
                        list.SafeAdd($"\t{prop.Name}", prop.Value);
                    }
                    return list;
                }

                list.Add(key, value);
            } catch {
                list.Add(key, value?.ToString());
            }
            return list;
        }

        public static void AppendData(this Exception targetException, IDictionary source) {
            if(source?.Count > 0) {
                if(targetException.Data.Count > 0) {
                    targetException.Data.AddSpacer();
                }
                foreach(string key in source.Keys) {
                    if(key.HasValue()) {
                        targetException.Data.SafeAdd(key, source[key]);
                    } else {
                        targetException.Data.AddSpacer();
                    }
                }
            }
        }

        public static bool IgnoreNotification(this Exception ex, HttpRequestMessage request = null) {
            const string task_exception = "System.Threading.Tasks.TaskCanceledException";
            const string ua_pattern = "SiteWarmup|AlwaysOn|ElasticScaleControllerExtension|HttpScaleManager";
            const string local_pattern = @"^(localhost|10\.0\.\d{1,3}.\d{1,3})$";

            string className = (ex as CustomException)?.ClassName;
            if(ex is TaskCanceledException || className?.Equals(task_exception) == true) {
                bool isLocal = request?.IsLocal() == true ||
                    ex.Data.Find("RemoteAddress", "Remote-Address", "Remote Address")
                           .Matches(local_pattern);
                if(isLocal) {
                    string userAgent = ex.Data.Find("UserAgent", "User-Agent", "User Agent")?.ToString();
                    if(userAgent.IsEmpty() && request != null) {
                        if(request.Headers.TryGetValues("User-Agent", out IEnumerable<string> values)) {
                            userAgent = values.FirstOrDefault();
                        }
                    }
                    return userAgent.Matches(ua_pattern);
                }
            }
            return false;
        }

        public static string Find(this IDictionary list, params string[] keys) {
            if(list == null) { return null; }
            foreach(string key in keys) {
                if(list.Contains(key)) {
                    return list[key] as string;
                }
            }
            return null;
        }

        public static string RegExReplace(this string value, string pattern, string replacement) {
            if(value.IsEmpty()) { return value; }
            return Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
        }

        public static StringContent ToStringContent(this string content, string contentType) {
            if(content.IsEmpty()) { return null; }
            return new StringContent(content, Encoding.UTF8, contentType);
        }

        public static StringContent ToJsonContent(this string content) {
            return content.ToStringContent("application/json");
        }

        public static bool IsJson(this string value) {
            const string json_pattern = @"^[{[]{1}.*[}\]]{1}$";
            return value?.Trim().Matches(json_pattern, RegexOptions.Singleline) == true;
        }

        public static void AppendEnvironment(this JObject jsonObj) {
            if(!Settings.IsProduction) {
                string name = jsonObj["username"]?.ToString() ?? "RMI Notification";
                jsonObj["username"] = $"{name} - {Settings.SlotName}";
            }
        }

        public static void AddDefaultValue(this JObject jsonObj, string key, object value) {
            if(jsonObj?.ContainsKey(key) == false) {
                JToken jToken = JToken.FromObject(value);
                jsonObj.Add(key, jToken);
            }
        }

        public static object Rename(this JObject jsonObj, string targetKey, params string[] aliases) {
            if(jsonObj?.ContainsKey(targetKey) == true) { return false; }
            foreach(string key in aliases) {
                if(jsonObj?.ContainsKey(key) == true) {
                    object value = jsonObj[key];
                    jsonObj.Remove(key);
                    jsonObj.AddDefaultValue(targetKey, value);
                    return true;
                }
            }
            return false;
        }

        public static HttpRequestBase GetBaseRequest(this HttpRequestMessage request) {
            return ((HttpContextWrapper)request.Properties["MS_HttpContext"]).Request;
        }

        public static string UserIPAddress(this HttpRequestMessage req) {
            IEnumerable<string> values;
            string ipAddress = req.IsLocal() ? "localhost" : null;
            if(ipAddress.IsEmpty()) {
                if(req.Headers.TryGetValues("X-Real-IP", out values)) {
                    ipAddress = values.FirstOrDefault();
                }

                if(ipAddress.IsEmpty() && req.Headers.TryGetValues("X-Forwarded-For", out values)) {
                    ipAddress = values.FirstOrDefault();
                }

                if(ipAddress.IsEmpty()) {
                    HttpRequestBase reqBase = req.GetBaseRequest();
                    ipAddress = reqBase.UserHostAddress;
                }
            }
            return ipAddress;
        }

        public static bool IsAdmin(this HttpRequestMessage req) {
            const string _IP_PATTERN = @"^172\.16\.10\.\d{1,2}";
            if(req.IsLocal()) { return true; }
            if(req.Headers.TryGetValues("X-Forwarded-For", out IEnumerable<string> values)) {
                return values.Any(v => v.Matches(_IP_PATTERN));
            }
            return false;
        }

        public static bool ValidateKeyToken(this HttpRequestMessage req) {
            string headerValue;
            if(req.Headers.TryGetValues("x-ms-auth-internal-token", out IEnumerable<string> values)) {
                headerValue = values.SingleOrDefault();
            } else {
                return false;
            }
            string key = Environment.GetEnvironmentVariable("WEBSITE_AUTH_ENCRYPTION_KEY");
            if(key.IsEmpty()) { return false; }

            SHA256 sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            string hash = Convert.ToBase64String(sha.ComputeHash(bytes));
            return hash == headerValue;
        }

        #region FormatException
        public static string Join(this IEnumerable<string> value, string separator) {
            if(value == null) { return null; }
            return string.Join(separator, value);
        }

        public static string Join(this IDictionary data, string prefix = null) {
            StringBuilder buf = new StringBuilder();
            foreach(string key in data.Keys) {
                if(key.HasValue()) {
                    buf.AppendLine($"{prefix}{key}: {data[key]}");
                } else {
                    buf.AppendLine();
                }
            }
            return buf.ToString();
        }

        public static IDictionary Clone(this IDictionary list) {
            if(list == null) { return null; }
            IDictionary clone = new Dictionary<string, object>();
            foreach(string key in list.Keys) {
                clone.Add(key, list[key]);
            }
            return clone;
        }
        #endregion
    }
}