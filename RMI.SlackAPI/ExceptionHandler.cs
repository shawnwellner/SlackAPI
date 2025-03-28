using RMI.Slack.Database;
using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http.Filters;

namespace RMI.Slack {
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class ExceptionHandlerAttribute : ExceptionFilterAttribute {
        public override void OnException(HttpActionExecutedContext context) {
            HttpRequestMessage req = context.Request;
            Exception ex = context.Exception;

            if(ex.IgnoreNotification()) {
                return;
            }

            string value;
            string remoteAddess = req.UserIPAddress();

            IDictionary data = ex.Data.Clone();
            ex.Data.Clear();
            ex.Data.SafeAdd("Url", req.RequestUri.OriginalString)
                   .SafeAdd("Method", req.Method.ToString())
                   .SafeAdd("Remote Address", remoteAddess)
                   .SafeAdd("User-Agent", req.Headers.UserAgent.ToString())
                   .AddSpacer()
                   .SafeAdd("Headers", string.Empty);

            foreach(var h in req.Headers) {
                try {
                    value = string.Join(",", h.Value);
                    ex.Data.SafeAdd($"\t - {h.Key}", value);
                } catch {
                    //Ignore Error Here
                }
            }

            ex.AppendData(data);

            if(req.Properties.TryGetValue("RMI_BODY", out object objBody)) {
                string body = objBody.ToString();
                ex.Data.AddSpacer().SafeAdd("Posted-Data", body);
            }

            /*if(ex.Response.HasValue()) {
				ex.Data.AddSpacer();
                ex.Data.SafeAdd("Slack-Response", ex.Response);
            }*/

            ex.PostToSlack(Settings.DefaultSlackChannel, req);
            //Utilities.SendExceptionEmail(ex);

            //object response = BuildResponse(ex);
            SendResponse(context);
            base.OnException(context);
        }

        private static void SendResponse(HttpActionExecutedContext context) {
            try {
                //string json = objectToReturn.ToJson();
                string json = new { success = false }.ToJson();
                //StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
                context.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                    Content = json.ToStringContent(WebRequest.JsonContentType)
                };
            } catch {
                //Ignore Errors
            }
        }

        private static object BuildResponse(Exception ex) {
            return new {
                Error = ex.Message,
                Details = ex.Data.Join(),
                Success = false
            };
        }
    }
}