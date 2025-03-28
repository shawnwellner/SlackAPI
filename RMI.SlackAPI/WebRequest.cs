using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace RMI.Slack {
    internal static class WebRequest {
        private const int MAX_RETRY_COUNT = 1;
        public const string HtmlContentType = "text/html";
        public const string PlainTextContentType = "text/plain";
        public const string JsonContentType = "application/json";

        public static string Post(string url, string data, string contentType) {
            return Post(url, data, contentType, null, 0);
        }

        public static string Post(string url, string data, string contentType, string authorization, int errorCount) {
            try {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                using(HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url)) {
                    req.Content = new ByteArrayContent(bytes);
                    req.Content.Headers.Add("Content-Type", $"{contentType}; charset=utf-8");
                    if(authorization.HasValue()) {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization);
                    }
                    return req.Send();
                }
            } catch(Exception ex) {
                if(errorCount < MAX_RETRY_COUNT) {
                    Thread.Sleep(++errorCount * 1000); //Add 1 to errorcount and delay 1 Second for each error.
                    return Post(url, data, contentType, authorization, errorCount);
                }
                ex.Data.SafeAdd("Requested-URL", url)
                       .SafeAdd("Original-Data", data);
                throw;
            }
        }

        private static string Send(this HttpRequestMessage req) {
            using(HttpClient client = new HttpClient()) {
                using(HttpResponseMessage resp = client.SendAsync(req).Result) {
                    resp.EnsureSuccessStatusCode();
                    return resp.GetResponseString();
                }
            }
        }

        public static string GetResponseString(this HttpResponseMessage resp) {
            if(resp.Content == null) { return null; }
            string result = resp.Content.ReadAsStringAsync().Result;
            return result ?? string.Empty;
        }

        public static string GetRequestBody(this HttpRequestMessage req) {
            string response = string.Empty;
            try {
                response = req.Content.ReadAsStringAsync().Result;
                response = response.CompressJson();
                //return data.RegExReplace(@"(\s{2,}|[\r\n\t]+)", "");
            } catch {
                //Ignore Errors
            }
            return response;
        }
    }
}