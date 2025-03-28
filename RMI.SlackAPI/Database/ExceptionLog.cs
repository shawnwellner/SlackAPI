using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Linq.Mapping;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RMI.Slack.Database {
    [Table(Name = "Infrastructure.AppExceptionLog")]
    internal class ExceptionLog {
        private const string DefaultSource = "RMI.SlackAPI";
        private static CancellationTokenSource _cancelSource = null;
        private static Queue<ExceptionLog> _queue;
        private static ExceptionLog _lastLog = null;
        private static DateTime _idleTime;
        //private List<Exception> _exceptionList = null;
        private static int _exceptionCount = 0;

        #region Conctructors
        static ExceptionLog() {
            _idleTime = DateTime.MinValue;
            _queue = new Queue<ExceptionLog>();
        }

        public ExceptionLog() {
            //_exceptionList = new List<Exception>();
        }

        public ExceptionLog(Exception ex) : this() {
            JObject jObject = ex.ToJObject();
            this.Source = ex.Source ?? DefaultSource;
            //this.ToException(jObject);
            AppendProperties(this, jObject);
        }

        internal static void CreateLog(JToken jToken, string baseUrl) {
            if(_exceptionCount >= Settings.MaxErrorCount || 
                _queue.Count >= Settings.MaxErrorCount || 
                _lastLog?.ExCount >= Settings.MaxErrorCount) {
                Debug.WriteLine($"Queue Count: {_queue.Count}");
                if(null != _lastLog) {
                    _lastLog.ExCount += _exceptionCount;
                    _lastLog.ExCount++;
                    _exceptionCount = 0;
                    Debug.WriteLine($"Max Errors: {_lastLog.ExCount}");
                    if(_lastLog.ExCount % Settings.ResetErrorCount == 0) {
                        Wait(ProcessRequest, 0).ConfigureAwait(false);
                    }
                } else {
                    _exceptionCount++;
                    Debug.WriteLine($"Max Errors: {_exceptionCount}");
                }
                return;
            }

            ExceptionLog log = new ExceptionLog();
            JObject jObject;

            JProperty prop = jToken.Annotation<JProperty>();
            log.SlackChannel = prop?.Value.ToString();

            log.Baseurl = baseUrl;
            if(jToken is JArray arry) {
                foreach(JToken item in arry) {
                    jObject = item as JObject;
                    if(log.Exception == null) {
                        AppendProperties(log, jObject);
                    } else {
                        log.ToException(jObject);
                    }
                }
            } else {
                jObject = jToken as JObject;
                AppendProperties(log, jObject);
            }

            //ResetTimer();
            Debug.WriteLine("");
            _queue.Enqueue(log);
            Wait(ProcessRequest, 1).ConfigureAwait(false);
        }

        public static bool OkayToSend {
            get {
                return (_queue.Count == 0 && DateTime.Now.Subtract(_idleTime).TotalSeconds >= 5) ||
                    _lastLog?.ExCount % Settings.ResetErrorCount == 0;
            }
        }

        public static void ResetTimer(bool clearLog = false) {
            _idleTime = DateTime.Now;
            if(clearLog) {
                _lastLog = null;
                _exceptionCount = 0;
                Debug.WriteLine($"Timer Reset - Cleared Log");
            } else {
                Debug.WriteLine($"Timer Reset");
            }
        }
        #endregion

        #region DB Columns
        [Column(IsPrimaryKey = true, IsDbGenerated = true)]
        public int? Id { get; private set; }

        [Column]
        public string Environment { get; set; }

        [Column]
        public string Source { get; set; }

        [Column(Name = "Exception")]
        public string ExceptionString { get; private set; }

        [Column]
        public int ExCount { get; private set; }

        [Column]
        public DateTime CreatedTime { get; private set; }

        [Column]
        public DateTime? UpdatedTime { get; private set; }
        #endregion

        public string SlackChannel { get; private set; }
        public string BuildVersion { get; private set; }
        private string Baseurl { get; set; }

        public bool LogOnly { get; private set; }

        public JObject JObject { get; private set; }

        //public Exception[] Exceptions => this._exceptionList?.ToArray();

        public CustomException Exception { get; private set; }

        //public Exception Exception { get; private set; }

        private static async Task Wait(Action<bool> action, double seconds) {
            _cancelSource?.Cancel();
            _cancelSource = new CancellationTokenSource();
            CancellationToken token = _cancelSource.Token;

            TimeSpan delay = TimeSpan.FromSeconds(seconds);
            await Task.Run(() => {
                try {
                    token.ThrowIfCancellationRequested();
                    Task.Delay(delay, token).Wait();
                    action(true);
                } catch(AggregateException) {
                    ResetTimer();
                } catch(Exception) {
                    //ExceptionLog log = new ExceptionLog(ex);
                    //_lastLog.Exception.AddException(ex);
                    UpdateRecord();
                    SlackMessage.SendException(_lastLog);
                }
            }, token);
        }

        private static void ProcessRequest(bool success) {
            ExceptionLog log = null;
            if(OkayToSend) {
                if(_lastLog.ExCount > 1) {
                    SlackMessage.SendException(_lastLog);
                }
                ResetTimer(_queue.Count == 0);
            } else {
                while(_queue.Count > 0) {
                    log = _queue.Dequeue();
                    UpdateRecord(log);
                }
                Wait(ProcessRequest, 5).ConfigureAwait(false);
            }
        }

        public static ExceptionLog GetExeptionDetails(int? id = null) {
            ExceptionLog log;
            using(AzureDb db = new AzureDb()) {
                if(id == null) {
                    log = db.ExceptionLog.OrderByDescending(l => l.Id).FirstOrDefault();
                } else {
                    log = db.ExceptionLog.SingleOrDefault(l => l.Id == id.Value);
                }
            }
            if(log != null) {
                JObject jObject = JObject.Parse(log.ExceptionString);
                AppendProperties(log, jObject);
            }
            return log;

        }

        private static void UpdateRecord(ExceptionLog appendLog = null) {
            if(null != _lastLog) {
                ExceptionLog log = _lastLog;
                _lastLog.Exception.AddException(appendLog?.JObject);
                using(AzureDb db = new AzureDb()) {
                    log = db.ExceptionLog.SingleOrDefault(l => l.Id == _lastLog.Id);
                    log.Baseurl = _lastLog.Baseurl;
                    log.SlackChannel = _lastLog.SlackChannel;
                    
                    JObject jObject = _lastLog.Exception.ToJObject();
                    AppendProperties(log, jObject);
                    log.UpdatedTime = DateTime.Now;
                    log.ExCount++;
                    db.SubmitChanges();
                }
                _lastLog = log;
                Debug.WriteLine($"Updated Record - {_lastLog.ExCount}");
            } else {
                InsertRecord(appendLog);
            }
        }

        private static void InsertRecord(ExceptionLog log) {
            try {
                using(AzureDb db = new AzureDb()) {
                    log.Baseurl = log.Baseurl ?? "unknown";
                    log.UpdatedTime = log.CreatedTime = DateTime.Now;
                    log.ExCount = ++_exceptionCount;
                    _exceptionCount = 0;
                    db.ExceptionLog.InsertOnSubmit(log);
                    db.SubmitChanges();
                }
                _lastLog = log;
                Debug.WriteLine($"New Record Created");
                SlackMessage.SendException(log);
                ResetTimer();
            } catch(Exception ex) {
                throw log.Exception.AddException(ex);
            }
        }

        private Exception ToException(JObject jObject) {
            const string envName = "Environment";
            const string buildName = "BuildVersion";
            Exception ex = this.Exception = new CustomException(jObject);

            this.Source = this.Source ?? ex.Source ?? DefaultSource;
            this.Environment = this.Environment ?? jObject.GetValue(envName)?.Value<string>();
            this.BuildVersion = this.BuildVersion ?? jObject.GetValue("BuildVersion")?.Value<string>();

            if(this.BuildVersion == null) {
                this.BuildVersion = this.Exception.Data.GetValue(envName, "unknown", true);
                jObject.AddFirst(new JProperty(buildName, this.BuildVersion));
            }

            if(this.Environment == null) {
                this.Environment = this.Exception.Data.GetValue(envName, "unknown", true);
                jObject.AddFirst(new JProperty(envName, this.Environment));
            }

            //Not sure why this is needed
            if(ex.Source.HasValue() && ex.Data[""] == null) {
                ex.Data[""] = ex.Source;
            }

            this.JObject = jObject; //this.Exception.ToJObject();
            this.ExceptionString = jObject.ToString();
            return this.Exception;
        }

        private static string FormatData(CustomException ex) {
            if(ex == null) { return null; }
            if(ex.Data.Count > 0) {
                StringBuilder additionalData = new StringBuilder();
                string value;
                additionalData.Append("<hr>");
                string preclass = null;
                foreach(string k in ex.Data.Keys) {
                    if(k.HasValue()) {
                        string key = k.RegExReplace(@" ", "&nbsp;")
                                      .RegExReplace(@"\t", "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;");

                        if(k.Equals("Headers")) {
                            preclass = " class='inline'";
                            additionalData.AppendLine("<hr>");
                        }

                        value = (ex.Data[k] as string) ?? "null";
                        if(value.IsJson()) {
                            var token = JToken.Parse(value);
                            value = token.ToString(Formatting.Indented);
                        }
                        if(value.Matches(@"[\r\n\t\\r\\n]")) {
                            value = value.RegExReplace(@"\r\n", "\r\n\t\t")
                                         .RegExReplace(@"\\r\\n", "\r\n")
                                         .RegExReplace(" ", "&nbsp;");
                            value = $"<pre{preclass}>\t{value}</pre>";
                        }
                        additionalData.AppendLine($"<div><b>{key}:</b> {value}</div>");
                    } else {
                        preclass = null;
                        additionalData.AppendLine("<br>");
                    }
                }
                return additionalData.ToString();
            }
            return null;
        }

        private static void AppendProperties(ExceptionLog log, JObject jObject) {
            const string baseUrlName = "BaseUrl";
            const string SlackChannelName = "SlackChannel";
            const string LogOnlyName = "LogOnly";

            try {
                jObject.AddFirst(new JProperty(baseUrlName, log.Baseurl));
                jObject.AddFirst(new JProperty(SlackChannelName, log.SlackChannel));
            } catch(ArgumentException) { //JProperties already added
                log.SlackChannel = jObject.GetValue(SlackChannelName)?.Value<string>();
                log.Baseurl = jObject.GetValue(baseUrlName)?.Value<string>();
            }

            log.ToException(jObject);
            log.LogOnly = log.Exception.Data[LogOnlyName].ToBool() ||
                log.Exception.IgnoreNotification();
        }

        public string LinkUrl {
            get {
                if(this.Baseurl.HasValue()) {
                    Uri uri = new Uri(this.Baseurl);
                    UriBuilder builder = new UriBuilder(uri);
                    builder.Path = $"/ex/{_lastLog?.Id}/";
                    return builder.ToString();
                }
                return null;
            }
        }

        #region HTML Methods
        private void FormatException(StringBuilder buffer, CustomException ex = null) {
            if(ex != null) {
                string stackTrace = null;
                if(ex.StackTrace.HasValue()) {
                    stackTrace = $@"
<hr>
<div>
	<b>StackTrace: </b>
	<pre>{ex.StackTrace}</pre>
</div>
";
                }

                buffer.Append($@"
<hr>
<div>
	<h4>Exception Details</h4>
    <div class='indent'>
        <div><b>Source: </b><span>{this.Source} - {this.Environment}</span></div>
        <div><b>Build: </b><span>{this.BuildVersion}</span></div>
	    <div><b>Exception-Type: </b><span>{ex.ClassName}</span></div>
	    <div><b>Exception: </b><pre>{ex.Message}</pre></div>
        <div>{ExceptionLog.FormatData(ex)}</div>
        {stackTrace}
    </div>
</div>
<br>
");
            }
        }

        public string ToHtml(bool email = false) {
            StringBuilder html = new StringBuilder();
            StringBuilder buffer = new StringBuilder();
            var exList = this.Exception.Exceptions;
            
            this.FormatException(buffer, this.Exception);

            foreach(CustomException ex in exList) {
                this.FormatException(buffer, ex);
            }

            string closingTags = null;
            if(email) {
                html.Append(@"
<style>
    body { background-color: #000; color: #fff; white-space: nowrap; }
    h4 { margin: 10px 0; }
    pre { margin: 7px 0; padding-left: 20px; }
    br { line-height: .5em; }

    .header { margin: 20px 0; }
    div { margin: 5px 0; }
    .indent { text-indent: 20px; }
    .content { font-size: .8em; }
    .content > div { margin: 20px 0; }
</style>");
            } else {
                closingTags = "</body></html>";
                html.Append(@"<!DOCTYPE html>
<html>
    <head>
        <meta charset=""utf-8"">
		<meta name=""viewport"" content=""width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=1"" />
        <title>Exception Details</title>
        <link href=""https://cdn.rmiatl.org/cdn/css/techinfo.css"" rel=""stylesheet"" />
    </head>
    <body>");
            }

            string IdValue = this.Id.HasValue ? this.Id.ToString() : "null";
            return $@"{html}
        <div class='slack-api'>
            <div class='container'>
                <div class='header'>
                    <div><b>Slack-Environment: </b><span>{Settings.SlotName}</span></div>
                    <div><b>LogId: </b>{IdValue}</div>
                    <div><b>Total Attempts: </b>{this.ExCount}</div>
                    <div><b>Total Exceptions: </b>{this.Exception.Count}</div>
                    <div><b>Created At: </b>{this.CreatedTime}</div>
                    <div><b>Updated At: </b>{this.UpdatedTime}</div>
                </div>
                <div class='content'>
                    {buffer}
                </div>
            </div>
        </div>
{closingTags}
";
        }
        #endregion
    }
}