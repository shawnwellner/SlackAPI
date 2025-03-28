using RMI.Slack.Database;
using System;
using System.Threading.Tasks;

namespace RMI.Slack {
    public class EmailMsg {
        public string To { get; set; }
        public string From { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public bool AsHTML { get; set; }
        public bool Priority { get; set; }
    }

    internal static class Utilities {
        public static void SendExceptionEmail(Exception ex) {
            ExceptionLog log = new ExceptionLog(ex);
            string body = log.ToHtml(true);

            Utilities.SendEmail(new EmailMsg() {
                Body = body,
                AsHTML = true,
                Priority = true
            });
        }

        public static void SendEmail(EmailMsg msg) {
            try {
                string subject = msg.Subject ?? "Exception occured Posting to Slack";
                string to = "dev@responsemine.com";

#if STAGING
				subject += " - Staging!";
#elif DEBUG
                subject += " - Development!";
                to = msg.To ?? "shawn.wellner@responsemine.com";
#endif
                Task.Run(() => {
                    msg.From = msg.From ?? "dev@responsemine.com";
                    msg.To = to;
                    msg.Subject = subject;
                    WebRequest.Post(Settings.EmailUrl, msg.ToJson(), "application/json");
                }).ConfigureAwait(false);
            } catch {
                //Ignore Errors
            }
        }
    }
}