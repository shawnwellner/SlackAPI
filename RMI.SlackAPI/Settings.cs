using System.Configuration;

namespace RMI.Slack {
    public static class Settings {
        private static string _assemblyName = null;
        private static string _slotName = null;
        public static string AssemblyName {
            get {
                if(_assemblyName == null) {
                    _assemblyName = typeof(Settings).Assembly.GetName().Name;
                }
                return _assemblyName;
            }
        }

        public static bool IsProduction {
            get {
#if DEBUG
                return false;
#else
				return SlotName.Matches("prod");
#endif
            }

        }

        public static string SlotName {
            get {
                if(_slotName.IsEmpty()) {
                    const string testPattern = "dev|staging|test";
                    string slotName = System.Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME");
                    if(slotName.Matches(testPattern)) {
                        _slotName = slotName;
                    } else {
                        string siteName = System.Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME").RegExEscape();
                        if(siteName.HasValue()) {
                            string hostName = System.Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                            string pattern = $@"^{siteName}\.azurewebsites\.net$";
                            if(hostName.Matches(pattern)) {
                                _slotName = "production";
                            }
                            pattern = $@"^{siteName}-([^.]+)\.azurewebsites\.net$";
                            if(hostName.Matches(pattern, out string[] groups)) {
                                _slotName = groups[1];
                            }
                        }
                    }
                }
                return _slotName ?? "localhost";
            }
        }

        private static string _buildVersion = null;
        public static string BuildVersion {
            get {
                if(_buildVersion.IsEmpty()) {
                    System.Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    _buildVersion = version.ToString();
                }
                return _buildVersion;
            }
        }

        private static string[] _scmURLs = null;
        public static string[] ScmUrls {
            get {
                if(_scmURLs == null) {
                    string hostName = System.Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                    if(hostName.HasValue()) {
                        string scmUrl = hostName.Replace(".azurewebsites.net", ".scm.azurewebsites.net");
                        _scmURLs = new string[] {
                            $"https://{scmUrl}/Env.cshtml",
                            $"https://{scmUrl}/dev/wwwroot/"
                        };
                    }
                }
                return _scmURLs;
            }
        }

        public static string ConnectionString => ConfigurationManager.ConnectionStrings["ConnectionString"]?.ConnectionString ??
                                                 ConfigurationManager.ConnectionStrings["ConnectionStringInfrastructure"].ConnectionString;
        public static string SlackAuthToken => ConfigurationManager.AppSettings["SlackAuthToken"];
        public static string DefaultSlackChannel => ConfigurationManager.AppSettings["DefaultSlackChannel"];
        public static string EmailUrl => ConfigurationManager.AppSettings["EmailUrl"];

        public static int MaxErrorCount => ConfigurationManager.AppSettings["MaxErrorCount"].ToInt() ?? 5;
        public static int ResetErrorCount => ConfigurationManager.AppSettings["ResetErrorCount"].ToInt() ?? 20;

        public static string GetSlackChannel(string source) {
            if(Settings.IsProduction && source.HasValue()) {
                return ConfigurationManager.AppSettings[source];
            }
            return null;
        }
    }
}