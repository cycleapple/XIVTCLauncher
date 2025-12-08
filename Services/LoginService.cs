using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using FFXIVSimpleLauncher.Services.Platform;

namespace FFXIVSimpleLauncher.Services;

public class LoginService
{
    private const string LauncherLoginUrl = "https://user.ffxiv.com.tw/api/login/launcherLogin";
    private const string LauncherSessionUrl = "https://user.ffxiv.com.tw/api/login/launcherSession";
    private const string PatchGameVerUrl = "http://patch-gamever.ffxiv.com.tw/http/win32/ffxivtc_release_tc_game/{0}/";

    private static readonly HttpClient HttpClient = new();

    static LoginService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; Orbit/1.0)");
        HttpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US, en");
        HttpClient.DefaultRequestHeaders.Add("Accept", "*/*");
    }

    /// <summary>
    /// Get the installed game version from ffxivgame.ver file
    /// </summary>
    public string GetGameVersion(string gamePath)
    {
        var verFile = Path.Combine(gamePath, "game", "ffxivgame.ver");
        if (File.Exists(verFile))
        {
            return File.ReadAllText(verFile).Trim();
        }
        return "2025.10.27.0000.0000"; // Fallback version
    }

    /// <summary>
    /// Get expansion versions from their respective .ver files
    /// </summary>
    private Dictionary<int, string> GetExpansionVersions(string gamePath)
    {
        var versions = new Dictionary<int, string>();
        var sqpackPath = Path.Combine(gamePath, "game", "sqpack");

        for (int i = 1; i <= 5; i++)
        {
            var exVerFile = Path.Combine(sqpackPath, $"ex{i}", $"ex{i}.ver");
            if (File.Exists(exVerFile))
            {
                versions[i] = File.ReadAllText(exVerFile).Trim();
            }
        }
        return versions;
    }

    /// <summary>
    /// Check game version with patch server (required before getting session)
    /// </summary>
    public async Task<string?> CheckGameVersionAsync(string gamePath)
    {
        var gameVer = GetGameVersion(gamePath);
        var exVersions = GetExpansionVersions(gamePath);

        // Build request body with expansion versions
        var body = new StringBuilder();
        foreach (var ex in exVersions)
        {
            body.AppendLine($"ex{ex.Key}\t{ex.Value}");
        }

        var url = string.Format(PatchGameVerUrl, gameVer);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("X-Hash-Check", "enabled");

        var response = await HttpClient.SendAsync(request);

        // Return X-Patch-Unique-Id from response headers (empty string if no patches needed)
        if (response.Headers.TryGetValues("X-Patch-Unique-Id", out var uniqueIdValues))
        {
            return uniqueIdValues.FirstOrDefault() ?? "";
        }

        return "";
    }

    private static string ToHex(string input)
    {
        return Convert.ToHexString(Encoding.UTF8.GetBytes(input)).ToLowerInvariant();
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string? otp = null, string? captchaToken = null)
    {
        try
        {
            // Step 1: launcherLogin
            var loginPayload = new LauncherLoginRequest
            {
                Email = ToHex(email),
                Password = ToHex(password),
                Code = otp ?? "",
                Token = captchaToken ?? ""
            };

            var loginResponse = await HttpClient.PostAsJsonAsync(LauncherLoginUrl, loginPayload);
            var loginResponseText = await loginResponse.Content.ReadAsStringAsync();

            if (!loginResponse.IsSuccessStatusCode)
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = $"Login failed: {loginResponse.StatusCode} - {loginResponseText}"
                };
            }

            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LauncherLoginResponse>();

            if (loginResult?.Token == null)
            {
                return new LoginResult { Success = false, ErrorMessage = $"Login failed: {loginResponseText}" };
            }

            // Step 2: launcherSession
            var sessionPayload = new LauncherSessionRequest { Token = loginResult.Token };
            var sessionResponse = await HttpClient.PostAsJsonAsync(LauncherSessionUrl, sessionPayload);
            var sessionResponseText = await sessionResponse.Content.ReadAsStringAsync();

            if (!sessionResponse.IsSuccessStatusCode)
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = $"Session failed: {sessionResponse.StatusCode} - {sessionResponseText}"
                };
            }

            var sessionResult = await sessionResponse.Content.ReadFromJsonAsync<LauncherSessionResponse>();

            if (sessionResult?.SessionId == null)
            {
                return new LoginResult { Success = false, ErrorMessage = $"Failed to get session: {sessionResponseText}" };
            }

            return new LoginResult { Success = true, SessionId = sessionResult.SessionId };
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public void LaunchGame(string gamePath, string sessionId)
    {
        var gameLauncher = PlatformServiceFactory.GetGameLauncher();
        gameLauncher.LaunchGame(gamePath, sessionId);
    }
}

public class LauncherLoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";
}

public class LauncherSessionRequest
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";
}

public class LauncherLoginResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("remain")]
    public int Remain { get; set; }
}

public class LauncherSessionResponse
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorMessage { get; set; }
}
