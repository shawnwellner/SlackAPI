using RMI.Slack.Database;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace RMI.Slack.Controllers {
    //http://localhost:60942/slack
    [ExceptionHandler]
    public class SlackController : ApiController {
        [Route("{channel}"), HttpPost]
        public object PostMessage(string channel) {
            return this.Request.PostToSlack(channel);
        }

        [HttpPost, Route]
        public object PostMessage() {
            return this.PostMessage(null);
        }

        [Route("ex/{id:int?}"), HttpGet]
        public HttpResponseMessage ExceptionDetails(int? id = null) {
            ExceptionLog log = ExceptionLog.GetExeptionDetails(id);
            string content = log?.ToHtml() ?? "Not Found";
            return new HttpResponseMessage() {
                Content = new StringContent(content, Encoding.UTF8, "text/html")
            };
        }

        [Route("throw"), HttpGet]
        public void ThrowException() {
            throw new Exception("Testing Exception Handler");
        }

        [Route("test"), HttpGet]
        public async Task<HttpResponseMessage> TestSlack() {
            string json = await this.Request.PostTest();
            return new HttpResponseMessage() {
                StatusCode = HttpStatusCode.OK,
                Content = json.ToJsonContent()
            };
        }

        [HttpGet, Route]
        public HttpResponseMessage Default() {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        [Route("techinfo"), HttpGet]
        public async Task<HttpResponseMessage> TechInfo() {
            return await Task.Factory.StartNew(() => {
                StringBuilder buffer = new StringBuilder();
                string scmLink = string.Empty;
                if(Settings.ScmUrls.HasValue()) {
                    var urls = Settings.ScmUrls;
                    scmLink = $@"
            <div><a href='{urls[0]}' target='_blank'>Environment Details</a></div>
            <div><a href='{urls[1]}' target='_blank'>App Service Editor</a></div>";
                }

                buffer.AppendLine($@"
<!DOCTYPE html>
<html>
    <head>
        <meta charset=""utf-8"">
		<meta name=""viewport"" content=""width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=1"" />
        <title>Tech Info</title>
        <link href=""https://cdn.rmiatl.org/cdn/css/techinfo.css"" rel=""stylesheet"" />
        <style>
            a:link, a:visited {{ color: #007bff; text-decoration: none; }}
        </style>
    </head>
    <body>
        <section>
            <h3>Info</h3>
            <div class='left-margin'>
                <div>SLOT: {Settings.SlotName}</div>
                <div>BUILD_VERSION: v{Settings.BuildVersion}</div>
                {scmLink}
            </div>
        </section>
        <hr/>
        <section>
            <h3>Headers</h3>
            <div class='left-margin'>
                {Headers}
            </div>
        </section>
        <hr/>
        <section>
            <h3>Variables</h3>
            <div class='left-margin'>
                {Variables}
            </div>
        </section>
    </body>
</html>
");
                string content = buffer.ToString();
                return new HttpResponseMessage() {
                    Content = new StringContent(content, Encoding.UTF8, "text/html")
                };
            });
        }

        [Route("ping"), HttpGet]
        public async Task<HttpResponseMessage> Ping() {
            const string pong = "pong";
            const string error = "Connection Failed";

            bool success = await AzureDb.TestConnection();
            if(success) {
                return new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = pong.ToStringContent(WebRequest.PlainTextContentType)
                };
            } else {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {
                    Content = error.ToStringContent(WebRequest.PlainTextContentType)
                };
            }
        }

        private string Headers {
            get {
                StringBuilder buffer = new StringBuilder();
                char[] splitOn = new char[] { ';' };
                string value;
                foreach(var h in this.Request.Headers) {
                    value = h.Value.Join(",");
                    if(h.Key.Matches("Cookie")) {
                        buffer.AppendLine($"            <h4>{h.Key}s:</h4>");
                        foreach(string val in value.Split(splitOn, StringSplitOptions.RemoveEmptyEntries)) {
                            buffer.AppendFormat("           <div class='left-margin'>{0}</div>", val.Trim());
                        }
                    } else {
                        buffer.AppendLine($"            <div>{h.Key} = {value}</div>");
                    }
                }
                return buffer.ToString();
            }
        }

        private string Variables {
            get {
                StringBuilder buffer = new StringBuilder();
                var variables = this.Request.GetBaseRequest().ServerVariables;
                foreach(string key in variables.AllKeys.Where(k => !k.Matches("^(ALL|HTTP)_"))) {
                    buffer.AppendLine($"            <div>{key} = {variables[key]}</div>");
                }
                return buffer.ToString();
            }
        }
    }
}
