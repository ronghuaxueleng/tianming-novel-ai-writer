using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.User.Services;

namespace TM.Framework.User.Services
{
    public class OAuthService
    {

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[OAuthService] {key}: {ex.Message}");
        }

        #region OAuth配置

        public static int CallbackPort { get; set; } = 23456;

        public static string CallbackUrl => $"http://localhost:{CallbackPort}/oauth/callback";

        private static readonly Dictionary<string, OAuthConfig> PlatformConfigs = new()
        {
            ["github"] = new OAuthConfig
            {
                AuthUrl = "https://github.com/login/oauth/authorize",
                ClientId = "",
                Scope = "user:email"
            },
            ["google"] = new OAuthConfig
            {
                AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                ClientId = "",
                Scope = "openid email profile"
            },
            ["microsoft"] = new OAuthConfig
            {
                AuthUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                ClientId = "",
                Scope = "openid email profile"
            },
            ["wechat"] = new OAuthConfig
            {
                AuthUrl = "https://open.weixin.qq.com/connect/qrconnect",
                ClientId = "",
                Scope = "snsapi_login"
            },
            ["qq"] = new OAuthConfig
            {
                AuthUrl = "https://graph.qq.com/oauth2.0/authorize",
                ClientId = "",
                Scope = "get_user_info"
            },
            ["baidu"] = new OAuthConfig
            {
                AuthUrl = "https://openapi.baidu.com/oauth/2.0/authorize",
                ClientId = "",
                Scope = "basic"
            }
        };

        #endregion

        public OAuthService()
        {
            TM.App.Log("[OAuthService] 初始化完成");
        }

        public async Task<OAuthAuthorizationResult> StartAuthorizationAsync(string platform, int timeoutSeconds = 120)
        {
            var platformLower = platform.ToLower();

            if (!PlatformConfigs.TryGetValue(platformLower, out var config))
            {
                return new OAuthAuthorizationResult
                {
                    Success = false,
                    ErrorMessage = $"不支持的平台: {platform}"
                };
            }

            if (string.IsNullOrEmpty(config.ClientId))
            {
                return new OAuthAuthorizationResult
                {
                    Success = false,
                    ErrorMessage = $"{platform} 尚未配置ClientId，请在设置中配置"
                };
            }

            var state = ShortIdGenerator.NewGuid().ToString("N");

            try
            {
                _cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var callbackTask = StartCallbackListenerAsync(state, _cts.Token);

                var authUrl = BuildAuthorizationUrl(platformLower, config, state);
                TM.App.Log($"[OAuthService] 打开授权页面: {authUrl}");

                OpenBrowser(authUrl);

                var result = await callbackTask;
                result.Platform = platform;
                result.State = state;

                return result;
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[OAuthService] 授权超时");
                return new OAuthAuthorizationResult
                {
                    Success = false,
                    Platform = platform,
                    State = state,
                    ErrorMessage = "授权超时，请重试"
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OAuthService] 授权异常: {ex.Message}");
                return new OAuthAuthorizationResult
                {
                    Success = false,
                    Platform = platform,
                    State = state,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                StopListener();
            }
        }

        public void CancelAuthorization()
        {
            _cts?.Cancel();
            StopListener();
            TM.App.Log("[OAuthService] 授权已取消");
        }

        public static void ConfigurePlatform(string platform, string clientId)
        {
            var platformLower = platform.ToLower();
            if (PlatformConfigs.TryGetValue(platformLower, out var config))
            {
                config.ClientId = clientId;
                TM.App.Log($"[OAuthService] 已配置 {platform} ClientId");
            }
        }

        public static bool IsPlatformConfigured(string platform)
        {
            var platformLower = platform.ToLower();
            return PlatformConfigs.TryGetValue(platformLower, out var config) &&
                   !string.IsNullOrEmpty(config.ClientId);
        }

        #region 私有方法

        private string BuildAuthorizationUrl(string platform, OAuthConfig config, string state)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = config.ClientId,
                ["redirect_uri"] = CallbackUrl,
                ["scope"] = config.Scope,
                ["state"] = state
            };

            if (platform == "wechat")
            {
                queryParams["appid"] = config.ClientId;
                queryParams.Remove("client_id");
            }

            var queryString = string.Join("&",
                System.Linq.Enumerable.Select(queryParams, kvp =>
                    $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

            return $"{config.AuthUrl}?{queryString}";
        }

        private async Task<OAuthAuthorizationResult> StartCallbackListenerAsync(string expectedState, CancellationToken ct)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{CallbackPort}/");

            try
            {
                _listener.Start();
                TM.App.Log($"[OAuthService] 回调监听器已启动: http://localhost:{CallbackPort}/");

                while (!ct.IsCancellationRequested)
                {
                    var contextTask = _listener.GetContextAsync();
                    var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, ct));

                    if (completedTask != contextTask)
                    {
                        throw new OperationCanceledException();
                    }

                    var context = await contextTask;
                    var request = context.Request;
                    var response = context.Response;

                    if (request.Url?.AbsolutePath == "/oauth/callback")
                    {
                        var query = HttpUtility.ParseQueryString(request.Url.Query);
                        var code = query["code"];
                        var state = query["state"];
                        var error = query["error"];
                        var errorDescription = query["error_description"];

                        string responseHtml;
                        OAuthAuthorizationResult result;

                        if (!string.IsNullOrEmpty(error))
                        {
                            responseHtml = GenerateResponseHtml(false, errorDescription ?? error);
                            result = new OAuthAuthorizationResult
                            {
                                Success = false,
                                ErrorMessage = errorDescription ?? error
                            };
                        }
                        else if (state != expectedState)
                        {
                            responseHtml = GenerateResponseHtml(false, "安全验证失败，请重试");
                            result = new OAuthAuthorizationResult
                            {
                                Success = false,
                                ErrorMessage = "State不匹配，可能存在CSRF攻击"
                            };
                        }
                        else if (string.IsNullOrEmpty(code))
                        {
                            responseHtml = GenerateResponseHtml(false, "未获取到授权码");
                            result = new OAuthAuthorizationResult
                            {
                                Success = false,
                                ErrorMessage = "未获取到授权码"
                            };
                        }
                        else
                        {
                            responseHtml = GenerateResponseHtml(true, "授权成功！您可以关闭此窗口。");
                            result = new OAuthAuthorizationResult
                            {
                                Success = true,
                                Code = code,
                                State = state
                            };
                        }

                        var buffer = Encoding.UTF8.GetBytes(responseHtml);
                        response.ContentType = "text/html; charset=utf-8";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
                        response.Close();

                        return result;
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Close();
                    }
                }

                throw new OperationCanceledException();
            }
            finally
            {
                StopListener();
            }
        }

        private void StopListener()
        {
            try
            {
                _listener?.Stop();
                _listener?.Close();
                _listener = null;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(StopListener), ex);
            }
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OAuthService] 打开浏览器失败: {ex.Message}");
                throw new Exception($"无法打开浏览器: {ex.Message}", ex);
            }
        }

        private static string GenerateResponseHtml(bool success, string message)
        {
            var color = success ? "#4CAF50" : "#f44336";
            var icon = success ? "✓" : "✗";

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>OAuth授权 - 天命</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
        }}
        .container {{
            text-align: center;
            background: white;
            padding: 60px 80px;
            border-radius: 16px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
        }}
        .icon {{
            font-size: 72px;
            color: {color};
            margin-bottom: 20px;
        }}
        .message {{
            font-size: 24px;
            color: #333;
            margin-bottom: 20px;
        }}
        .hint {{
            font-size: 14px;
            color: #666;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""icon"">{icon}</div>
        <div class=""message"">{HttpUtility.HtmlEncode(message)}</div>
        <div class=""hint"">此窗口可以安全关闭</div>
    </div>
    <script>setTimeout(() => window.close(), 3000);</script>
</body>
</html>";
        }

        #endregion
    }

    #region 数据模型

    public class OAuthConfig
    {
        public string AuthUrl { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
    }

    public class OAuthAuthorizationResult
    {
        public bool Success { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    #endregion
}
