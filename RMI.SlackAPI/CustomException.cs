using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace RMI.Slack {
    //[Serializable]
    public class CustomException : Exception {
        private readonly string _className;
        private readonly string _message;
        private readonly string _stackTrace;
        private List<CustomException> _exceptions;

        private CustomException(string message) : base(message) {
            this._message = message;
        }

        public CustomException(JObject jObject) : this(jObject.GetValue("Message").Value<string>()) {
            this.Source      = jObject.GetValue("Source").Value<string>();
            this._className  = jObject.GetValue("ClassName")?.Value<string>() ?? "System.Exception";
            this._stackTrace = jObject.GetValue("StackTraceString")?.Value<string>() ??
                               jObject.GetValue("StackTrace")?.Value<string>();
            this.FillExceptions(jObject);
            var data = jObject.GetValue("Data").Value<JObject>();
            if(data != null) {
                foreach(var item in data) {
                    if(item.Key.HasValue()) {
                        this.Data.SafeAdd(item.Key, item.Value.Value<object>());
                    } else {
                        this.Data.AddSpacer();
                    }
                }
            }
        }

        public string ClassName => this._className;
        public override string Message => this._message;
        public override string StackTrace => this._stackTrace;
        public int Count => this._exceptions.Count + 1;

        [JsonProperty("InnerExceptions")]
        public IEnumerable<CustomException> Exceptions => this._exceptions;

        public CustomException AddException(Exception ex) {
            if(ex == null) { return null; }
            this.AddException(ex.ToJObject());
            return this;
        }

        public void AddException(JObject jObject) {
            if(jObject == null) { return; }
            this._exceptions.Add(new CustomException(jObject));
        }

        private CustomException FillExceptions(JObject jObject) {
            var exList = new List<CustomException>();
            var list = jObject.GetValue("InnerExceptions")?.Value<JArray>();
            if(list != null) {
                foreach(JObject item in list) {
                    exList.Add(new CustomException(item));
                }
            } else {
                var jObj = jObject.GetValue("InnerException")?.Value<JObject>();
                if(jObj != null) {
                    exList.Add(new CustomException(jObj));
                }
            }
            this._exceptions = exList;
            return this;
        }
    }
}