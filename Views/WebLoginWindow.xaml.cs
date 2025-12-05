using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FFXIVSimpleLauncher.Views;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class LoginBridge
{
    private static readonly HttpClient _httpClient = new();
    private readonly WebLoginWindow _window;
    private readonly string _gamePath;

    static LoginBridge()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; Orbit/1.0)");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US, en");
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
    }

    public LoginBridge(WebLoginWindow window, string gamePath)
    {
        _window = window;
        _gamePath = gamePath;
    }

    public async Task<string> Login(string email, string password, string otp, string recaptchaToken, bool rememberMe)
    {
        try
        {
            // Step 1: launcherLogin - get token
            var payload = new
            {
                email = ToHex(email),
                password = ToHex(password),
                code = otp ?? "",
                token = recaptchaToken
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://user.ffxiv.com.tw/api/login/launcherLogin", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var jsonDoc = JsonDocument.Parse(responseBody);
            if (jsonDoc.RootElement.TryGetProperty("token", out var tokenElement))
            {
                var loginToken = tokenElement.GetString();
                if (!string.IsNullOrEmpty(loginToken))
                {
                    // Step 2: patch-gamever - check game version (REQUIRED before session!)
                    await CheckGameVersionAsync();

                    // Step 3: launcherSession - get sessionId
                    var sessionPayload = JsonSerializer.Serialize(new { token = loginToken });
                    var sessionContent = new StringContent(sessionPayload, Encoding.UTF8, "application/json");
                    var sessionResponse = await _httpClient.PostAsync("https://user.ffxiv.com.tw/api/login/launcherSession", sessionContent);
                    var sessionBody = await sessionResponse.Content.ReadAsStringAsync();

                    var sessionJson = JsonDocument.Parse(sessionBody);
                    if (sessionJson.RootElement.TryGetProperty("sessionId", out var sessionElement))
                    {
                        var sessionId = sessionElement.GetString();
                        _window.Dispatcher.Invoke(() =>
                        {
                            _window.LoginToken = loginToken;
                            _window.SessionId = sessionId;
                            // Save credentials if remember me is checked
                            if (rememberMe)
                            {
                                _window.LastEmail = email;
                                _window.LastPassword = password;
                            }
                            else
                            {
                                _window.LastEmail = null;
                                _window.LastPassword = null;
                            }
                            _window.DialogResult = true;
                            _window.Close();
                        });
                        return JsonSerializer.Serialize(new { success = true, sessionId });
                    }
                }
            }

            return responseBody; // Return error from server
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private async Task CheckGameVersionAsync()
    {
        var gameVer = GetGameVersion();
        var exVersions = GetExpansionVersions();

        // Build request body with expansion versions
        var body = new StringBuilder();
        foreach (var ex in exVersions)
        {
            body.AppendLine($"ex{ex.Key}\t{ex.Value}");
        }

        var url = $"http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{gameVer}/";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("X-Hash-Check", "enabled");

        await _httpClient.SendAsync(request);
    }

    private string GetGameVersion()
    {
        var verFile = System.IO.Path.Combine(_gamePath, "game", "ffxivgame.ver");
        if (System.IO.File.Exists(verFile))
        {
            return System.IO.File.ReadAllText(verFile).Trim();
        }
        return "2025.10.27.0000.0000";
    }

    private Dictionary<int, string> GetExpansionVersions()
    {
        var versions = new Dictionary<int, string>();
        var sqpackPath = System.IO.Path.Combine(_gamePath, "game", "sqpack");

        for (int i = 1; i <= 5; i++)
        {
            var exVerFile = System.IO.Path.Combine(sqpackPath, $"ex{i}", $"ex{i}.ver");
            if (System.IO.File.Exists(exVerFile))
            {
                versions[i] = System.IO.File.ReadAllText(exVerFile).Trim();
            }
        }
        return versions;
    }

    private static string ToHex(string str)
    {
        return string.Concat(str.Select(c => ((int)c).ToString("x2")));
    }
}

public partial class WebLoginWindow : Window
{
    public string? SessionId { get; set; }
    public string? LoginToken { get; set; }
    public string? LastEmail { get; set; }
    public string? LastPassword { get; set; }
    private LoginBridge? _bridge;
    private readonly string _gamePath;
    private readonly string? _savedEmail;
    private readonly string? _savedPassword;

    public WebLoginWindow(string gamePath, string? savedEmail = null, string? savedPassword = null)
    {
        _gamePath = gamePath;
        _savedEmail = savedEmail;
        _savedPassword = savedPassword;
        InitializeComponent();
        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            // Create WebView2 environment with custom user data folder
            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FINAL FANTASY XIV TC", "etc", "EBWebView");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            // Add JavaScript bridge for login
            _bridge = new LoginBridge(this, _gamePath);
            WebView.CoreWebView2.AddHostObjectToScript("loginBridge", _bridge);

            // Intercept requests to launcher.ffxiv.com.tw
            WebView.CoreWebView2.AddWebResourceRequestedFilter("*://launcher.ffxiv.com.tw/*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            // Navigate to the login page
            WebView.CoreWebView2.Navigate("https://launcher.ffxiv.com.tw/index.html");

            // Update status when navigation completes
            WebView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    StatusText.Text = "就緒 - 請登入";

                    // Auto-fill saved credentials
                    if (!string.IsNullOrEmpty(_savedEmail))
                    {
                        var escapedEmail = _savedEmail.Replace("'", "\\'");
                        var escapedPassword = _savedPassword?.Replace("'", "\\'") ?? "";
                        var script = $@"
                            document.getElementById('email').value = '{escapedEmail}';
                            document.getElementById('password').value = '{escapedPassword}';
                            document.getElementById('rememberMe').checked = true;
                        ";
                        await WebView.CoreWebView2.ExecuteScriptAsync(script);
                        StatusText.Text = "已載入帳號資料 - 請登入";
                    }
                }
                else
                {
                    StatusText.Text = $"導覽失敗: {e.WebErrorStatus}";
                }
            };

            StatusText.Text = "就緒 - 請登入";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"WebView2 錯誤: {ex.Message}";
            MessageBox.Show($"初始化 WebView2 失敗: {ex.Message}\n\n請確認已安裝 WebView2 Runtime。",
                "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);

        if (uri.Host == "launcher.ffxiv.com.tw")
        {
            if (uri.AbsolutePath == "/" || uri.AbsolutePath == "/index.html")
            {
                // Return custom login page HTML with reCAPTCHA
                var html = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>FFXIV 登入</title>
    <script src='https://www.google.com/recaptcha/enterprise.js?render=6Ld6VmorAAAAANQdQeqkaOeScR42qHC7Hyalq00r'></script>
    <style>
        * { box-sizing: border-box; }
        body {
            font-family: 'Microsoft JhengHei', 'Segoe UI', Arial, sans-serif;
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
            margin: 0; padding: 20px;
            min-height: 100vh;
            display: flex; justify-content: center; align-items: center;
        }
        .login-box {
            background: rgba(255,255,255,0.1);
            backdrop-filter: blur(10px);
            border-radius: 16px;
            padding: 40px;
            width: 100%; max-width: 400px;
            box-shadow: 0 8px 32px rgba(0,0,0,0.3);
        }
        h1 {
            color: #fff;
            text-align: center;
            margin: 0 0 30px 0;
            font-size: 24px;
        }
        .form-group { margin-bottom: 20px; }
        label {
            display: block;
            color: #ccc;
            margin-bottom: 8px;
            font-size: 14px;
        }
        input[type='email'], input[type='password'], input[type='text'] {
            width: 100%;
            padding: 12px 16px;
            border: 1px solid rgba(255,255,255,0.2);
            border-radius: 8px;
            background: rgba(255,255,255,0.1);
            color: #fff;
            font-size: 16px;
            transition: border-color 0.3s;
        }
        input:focus {
            outline: none;
            border-color: #4a9eff;
        }
        input::placeholder { color: rgba(255,255,255,0.5); }
        button {
            width: 100%;
            padding: 14px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            border: none;
            border-radius: 8px;
            color: #fff;
            font-size: 16px;
            font-weight: bold;
            cursor: pointer;
            transition: transform 0.2s, box-shadow 0.2s;
        }
        button:hover {
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(102, 126, 234, 0.4);
        }
        button:disabled {
            background: #666;
            cursor: not-allowed;
            transform: none;
        }
        .status {
            text-align: center;
            margin-top: 20px;
            padding: 10px;
            border-radius: 8px;
            font-size: 14px;
        }
        .status.error { background: rgba(255,0,0,0.2); color: #ff6b6b; }
        .status.success { background: rgba(0,255,0,0.2); color: #6bff6b; }
        .status.info { background: rgba(0,100,255,0.2); color: #6b9fff; }
    </style>
</head>
<body>
    <div class='login-box'>
        <h1>FINAL FANTASY XIV</h1>
        <form id='loginForm'>
            <div class='form-group'>
                <label>電子郵件</label>
                <input type='email' id='email' placeholder='your@email.com' required>
            </div>
            <div class='form-group'>
                <label>密碼</label>
                <input type='password' id='password' placeholder='請輸入密碼' required>
            </div>
            <div class='form-group'>
                <label>OTP 驗證碼 (如有啟用)</label>
                <input type='text' id='otp' placeholder='6 位數驗證碼 (選填)' maxlength='6'>
            </div>
            <div class='form-group' style='display:flex;align-items:center;'>
                <input type='checkbox' id='rememberMe' style='width:auto;margin-right:8px;'>
                <label for='rememberMe' style='margin:0;cursor:pointer;'>記住帳號</label>
            </div>
            <button type='submit' id='submitBtn'>登入</button>
        </form>
        <div id='status' class='status' style='display:none;'></div>
    </div>
    <script>
        const RECAPTCHA_SITE_KEY = '6Ld6VmorAAAAANQdQeqkaOeScR42qHC7Hyalq00r';

        function showStatus(msg, type) {
            const status = document.getElementById('status');
            status.textContent = msg;
            status.className = 'status ' + type;
            status.style.display = 'block';
        }

        document.getElementById('loginForm').addEventListener('submit', async function(e) {
            e.preventDefault();
            const btn = document.getElementById('submitBtn');
            btn.disabled = true;
            btn.textContent = '登入中...';
            showStatus('正在取得 reCAPTCHA 驗證...', 'info');

            try {
                // Use reCAPTCHA Enterprise API
                const token = await grecaptcha.enterprise.execute(RECAPTCHA_SITE_KEY, {action: 'LOGIN'});
                showStatus('正在驗證帳號...', 'info');

                const email = document.getElementById('email').value;
                const password = document.getElementById('password').value;
                const otp = document.getElementById('otp').value || '';
                const rememberMe = document.getElementById('rememberMe').checked;

                // Call C# backend via WebView2 bridge to bypass CORS
                const bridge = window.chrome.webview.hostObjects.loginBridge;
                const result = await bridge.Login(email, password, otp, token, rememberMe);

                const data = JSON.parse(result);
                if (data.success) {
                    showStatus('登入成功！Session: ' + data.sessionId, 'success');
                } else if (data.error) {
                    showStatus('錯誤: ' + data.error, 'error');
                } else {
                    showStatus('登入失敗: ' + result, 'error');
                }
            } catch (err) {
                showStatus('錯誤: ' + err.message, 'error');
            } finally {
                btn.disabled = false;
                btn.textContent = '登入';
            }
        });
    </script>
</body>
</html>";

                var stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
                var response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
                e.Response = response;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        WebView.CoreWebView2?.Navigate("https://launcher.ffxiv.com.tw/index.html");
    }

    protected override void OnClosed(EventArgs e)
    {
        WebView?.Dispose();
        base.OnClosed(e);
    }
}
