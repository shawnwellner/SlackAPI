using Azure.Core;
using Azure.Identity;
using System;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace RMI.Slack.Database {
    [Database]
    internal class AzureDb : DataContext {
        private static bool? _azureSqlServer = null;

        public AzureDb() : base(Settings.ConnectionString) {
            ((SqlConnection)base.Connection).AccessToken = AzureDb.AccessToken;
        }

        Table<ExceptionLog> _exLog = null;
        public Table<ExceptionLog> ExceptionLog {
            get {
                if(this._exLog == null) {
                    this._exLog = base.GetTable<ExceptionLog>();
                }
                return this._exLog;
            }
        }

        private static bool AzureSqlServer {
            get {
                if(_azureSqlServer == null) {
                    _azureSqlServer = Settings.ConnectionString.Matches(@"Server=[^.]+\.database\.windows\.net;");
                }
                return _azureSqlServer == true;
            }
        }

        private static string AccessToken {
            get {
                if(!AzureDb.AzureSqlServer) { return null; }
                const string AccessTokenKey = "AzureAccessToken";
                string token = AccessTokenKey.GetCache();
                if(token.IsEmpty()) {
                    var tokenRequestContext = new TokenRequestContext(new[] { "https://database.windows.net//.default" });
                    var tokenRequestResult = new DefaultAzureCredential().GetTokenAsync(tokenRequestContext).Result;
                    TimeSpan diff = tokenRequestResult.ExpiresOn.Subtract(DateTime.UtcNow);
                    token = tokenRequestResult.Token.Cache(AccessTokenKey, diff.TotalMinutes);
                }
                return token;
            }
        }

        public static async Task<bool> TestConnection() {
            try {
                using(AzureDb db = new AzureDb()) {
                    await db.Connection.OpenAsync();
                }
                return true;
            } catch(Exception) {
                return false;
            }
        }
    }
}