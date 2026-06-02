using Core.LogModule;
using Newtonsoft.Json;
using System.Net.Http;

namespace Desktop.NetWork
{
    public class Get
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static int _getErrorCount = 0;
        private static bool _firstError = true;

        static Get()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
        }

        /// <summary>
        /// 异步GET方法
        /// </summary>
        public static async Task<T> GetBodyAsync<T>(string url, Dictionary<string, string> _dic = null)
        {
            if (!string.IsNullOrEmpty(url) && url.Length > 4 && url.Substring(0, 4) != "http")
            {
                url = "http://" + url;
            }
            try
            {
                Dictionary<string, string> dic = new Dictionary<string, string>
                {
                    { "access_key_id", Core.Config.Core_RunConfig._DesktopAccessKeyId },
                    { "access_key_secret", Core.Config.Core_RunConfig._DesktopAccessKeySecret },
                    { "time", DateTimeOffset.Now.ToUnixTimeSeconds().ToString()}
                };
                if (_dic != null)
                {
                    foreach (var item in _dic)
                    {
                        dic.Add(item.Key, item.Value);
                    }
                }
                string AuthenticationOriginalStr = string.Join(";", dic.Where(p => p.Key.ToLower() != "sig").OrderBy(p => p.Key).Select(p => $"{p.Key.ToLower()}={p.Value}"));
                string sig = Core.Tools.Encryption.SHA1_Encrypt(AuthenticationOriginalStr);
                dic.Add("sig", sig);
                dic.Remove("access_key_secret");
                string Parameter = string.Empty;
                foreach (var item in dic)
                {
                    Parameter += $"{item.Key}={item.Value}&";
                }

                HttpResponseMessage response = await _httpClient.GetAsync($"{url}?{Parameter}");
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                OperationQueue.pack<T> A = JsonConvert.DeserializeObject<OperationQueue.pack<T>>(responseBody);
                return A.data;
            }
            catch (Exception ex)
            {
                _getErrorCount++;
                if (_getErrorCount > 30)
                {
                    Log.Warn(nameof(GetBodyAsync), $"触发DesktopTips={_getErrorCount}");
                    _getErrorCount = 0;
                }
                if (_firstError)
                {
                    _firstError = false;
                    Log.Warn(nameof(GetBodyAsync), $"发起Get请求出错({(Core.Config.Core_RunConfig._DesktopRemoteServer?"远程模式":"本地模式")}),URL:[{url}]，错误堆栈：\r\n{ex.ToString()}", ex);
                }
                else
                {
                    Log.Warn(nameof(GetBodyAsync), $"发起Get请求出错({(Core.Config.Core_RunConfig._DesktopRemoteServer?"远程模式":"本地模式")}),URL:[{url}]，错误堆栈：\r\n{ex.ToString()}", ex, false);
                }

                return default;
            }
        }
    }
}
