using Core.LogModule;
using Newtonsoft.Json;
using System.Net.Http;

namespace Desktop.NetWork
{
    public class Post
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static int _postErrorCount = 0;
        private static bool _firstError = true;

        static Post()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
        }

        /// <summary>
        /// 同步POST方法
        /// </summary>
        /// <param name="url">URL</param>
        /// <param name="_dic">POST要发送的键值对</param>
        /// <param name="TimeoutPeriod">超时时间</param>
        /// <returns>请求返回体</returns>
        public static async Task<T> PostBody<T>(string url, Dictionary<string, string> _dic = null, TimeSpan TimeoutPeriod = default(TimeSpan))
        {

            if (!string.IsNullOrEmpty(url) && url.Length > 5 && url.Substring(0, 4) != "http")
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
                        dic.Add(item.Key, item.Value.ToString());
                    }
                }
                string AuthenticationOriginalStr = string.Join(";", dic.Where(p => p.Key.ToLower() != "sig").OrderBy(p => p.Key).Select(p => $"{p.Key.ToLower()}={p.Value}"));
                string sig = Core.Tools.Encryption.SHA1_Encrypt(AuthenticationOriginalStr);
                dic.Add("sig", sig);
                dic.Remove("access_key_secret");

                var client = _httpClient;
                if (TimeoutPeriod != default(TimeSpan))
                {
                    // 当需要自定义超时时，创建短期客户端
                    client = new HttpClient { Timeout = TimeoutPeriod };
                }

                var content = new FormUrlEncodedContent(dic);
                var response = await client.PostAsync(url, content);
                var responseString = await response.Content.ReadAsStringAsync();
                OperationQueue.pack<T> A = JsonConvert.DeserializeObject<OperationQueue.pack<T>>(responseString);

                return A.data;
            }
            catch (Exception ex)
            {
                _postErrorCount++;
                if (_postErrorCount > 30)
                {
                    Log.Warn(nameof(PostBody), $"触发DesktopTips={_postErrorCount}");
                    _postErrorCount = 0;
                }
                if (_firstError)
                {
                    _firstError = false;
                    Log.Warn(nameof(PostBody), $"发起Post请求出错({(Core.Config.Core_RunConfig._DesktopRemoteServer?"远程模式":"本地模式")}),URL:[{url}]，错误堆栈：\r\n{ex.ToString()}", ex);
                }
                else
                {
                    Log.Warn(nameof(PostBody), $"发起Post请求出错({(Core.Config.Core_RunConfig._DesktopRemoteServer?"远程模式":"本地模式")}),URL:[{url}]，错误堆栈：\r\n{ex.ToString()}", ex, false);
                }

                if (ex is TaskCanceledException)
                {
                    Log.Warn(nameof(PostBody), $"发起Post请求超时({(Core.Config.Core_RunConfig._DesktopRemoteServer?"远程模式":"本地模式")}),URL:[{url}]", ex);
                }

                return default;
            }
        }
    }
}
