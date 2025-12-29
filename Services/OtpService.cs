using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Service for generating TOTP (Time-based One-Time Password) codes.
/// Stores the OTP secret securely in Windows Credential Manager.
/// Supports per-account OTP secrets.
/// </summary>
public class OtpService
{
    private const string CredentialTargetBase = "FFXIVSimpleLauncher-OTP";
    private const string LegacyCredentialTarget = "FFXIVSimpleLauncher-OTP"; // For migration
    private const int TimeStep = 30; // 30 seconds
    private const int CodeDigits = 6;

    private string? _currentAccountId;
    private byte[]? _secretKey;
    private System.Timers.Timer? _refreshTimer;

    /// <summary>
    /// Event fired when a new OTP code is generated.
    /// </summary>
    public event Action<string>? OtpCodeChanged;

    /// <summary>
    /// Event fired when the remaining seconds until next code changes.
    /// </summary>
    public event Action<int>? SecondsRemainingChanged;

    /// <summary>
    /// Current OTP code.
    /// </summary>
    public string CurrentCode { get; private set; } = string.Empty;

    /// <summary>
    /// Seconds remaining until next code.
    /// </summary>
    public int SecondsRemaining { get; private set; }

    /// <summary>
    /// Whether an OTP secret is configured.
    /// </summary>
    public bool IsConfigured => _secretKey != null && _secretKey.Length > 0;

    /// <summary>
    /// Current account ID (null for legacy mode).
    /// </summary>
    public string? CurrentAccountId => _currentAccountId;

    /// <summary>
    /// Get the credential target for the current account.
    /// </summary>
    private string GetCredentialTarget()
    {
        return string.IsNullOrEmpty(_currentAccountId)
            ? LegacyCredentialTarget
            : $"{CredentialTargetBase}-{_currentAccountId}";
    }

    /// <summary>
    /// Get the credential target for a specific account.
    /// </summary>
    private static string GetCredentialTargetForAccount(string accountId)
    {
        return $"{CredentialTargetBase}-{accountId}";
    }

    /// <summary>
    /// Initialize the OTP service in legacy mode (single account).
    /// </summary>
    public void Initialize()
    {
        _currentAccountId = null;
        LoadSecret();
        if (IsConfigured)
        {
            StartAutoRefresh();
        }
    }

    /// <summary>
    /// Initialize the OTP service for a specific account.
    /// </summary>
    /// <param name="accountId">The account ID to load OTP secret for</param>
    public void InitializeForAccount(string accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            Initialize();
            return;
        }

        StopAutoRefresh();
        _currentAccountId = accountId;
        _secretKey = null;
        CurrentCode = string.Empty;

        LoadSecret();
        if (IsConfigured)
        {
            StartAutoRefresh();
        }
    }

    /// <summary>
    /// Set the OTP secret from a base32-encoded string.
    /// </summary>
    /// <param name="base32Secret">The base32-encoded secret (from QR code or manual entry)</param>
    /// <returns>True if the secret was valid and saved</returns>
    public bool SetSecret(string base32Secret)
    {
        try
        {
            // Remove spaces and convert to uppercase
            base32Secret = base32Secret.Replace(" ", "").ToUpperInvariant();

            // Validate and decode base32
            var secretBytes = Base32Decode(base32Secret);
            if (secretBytes == null || secretBytes.Length == 0)
            {
                return false;
            }

            _secretKey = secretBytes;
            SaveSecret(base32Secret);
            StartAutoRefresh();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clear the stored OTP secret.
    /// </summary>
    public void ClearSecret()
    {
        _secretKey = null;
        CurrentCode = string.Empty;
        StopAutoRefresh();
        DeleteSecret();
    }

    /// <summary>
    /// Generate the current OTP code.
    /// </summary>
    public string GenerateCode()
    {
        if (_secretKey == null || _secretKey.Length == 0)
        {
            return string.Empty;
        }

        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = unixTime / TimeStep;

        return GenerateTotp(_secretKey, counter);
    }

    /// <summary>
    /// Get seconds remaining until the next code.
    /// </summary>
    public int GetSecondsRemaining()
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return TimeStep - (int)(unixTime % TimeStep);
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();

        // Update immediately
        UpdateCode();

        // Start timer to update every second
        _refreshTimer = new System.Timers.Timer(1000);
        _refreshTimer.Elapsed += (s, e) => UpdateCode();
        _refreshTimer.AutoReset = true;
        _refreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private void UpdateCode()
    {
        var newCode = GenerateCode();
        var seconds = GetSecondsRemaining();

        if (newCode != CurrentCode)
        {
            CurrentCode = newCode;
            OtpCodeChanged?.Invoke(CurrentCode);
        }

        if (seconds != SecondsRemaining)
        {
            SecondsRemaining = seconds;
            SecondsRemainingChanged?.Invoke(SecondsRemaining);
        }
    }

    /// <summary>
    /// Generate TOTP code using HMAC-SHA1.
    /// </summary>
    private string GenerateTotp(byte[] secret, long counter)
    {
        // Convert counter to big-endian byte array
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        // Compute HMAC-SHA1
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        // Dynamic truncation (RFC 4226)
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        // Generate 6-digit code
        var otp = binaryCode % (int)Math.Pow(10, CodeDigits);
        return otp.ToString().PadLeft(CodeDigits, '0');
    }

    /// <summary>
    /// Decode a base32-encoded string to bytes.
    /// </summary>
    private static byte[]? Base32Decode(string base32)
    {
        if (string.IsNullOrEmpty(base32))
            return null;

        // Remove padding
        base32 = base32.TrimEnd('=');

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in base32)
        {
            var charIndex = alphabet.IndexOf(c);
            if (charIndex < 0)
            {
                return null; // Invalid character
            }

            buffer = (buffer << 5) | charIndex;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)(buffer >> bitsLeft));
            }
        }

        return output.ToArray();
    }

    #region Windows Credential Manager

    private void SaveSecret(string base32Secret)
    {
        SaveSecretToTarget(GetCredentialTarget(), base32Secret);
    }

    private void LoadSecret()
    {
        var target = GetCredentialTarget();
        _secretKey = LoadSecretFromTarget(target);
    }

    private void DeleteSecret()
    {
        CredDelete(GetCredentialTarget(), CRED_TYPE.GENERIC, 0);
    }

    /// <summary>
    /// Save OTP secret to a specific credential target.
    /// </summary>
    private static void SaveSecretToTarget(string target, string base32Secret)
    {
        var secretBytes = Encoding.Unicode.GetBytes(base32Secret);
        var secretPtr = Marshal.AllocHGlobal(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, secretPtr, secretBytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE.GENERIC,
                TargetName = target,
                UserName = "OTPSecret",
                CredentialBlob = secretPtr,
                CredentialBlobSize = (uint)secretBytes.Length,
                Persist = CRED_PERSIST.LOCAL_MACHINE
            };

            CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(secretPtr);
        }
    }

    /// <summary>
    /// Load OTP secret from a specific credential target.
    /// </summary>
    private static byte[]? LoadSecretFromTarget(string target)
    {
        if (CredRead(target, CRED_TYPE.GENERIC, 0, out var credentialPtr))
        {
            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                {
                    var secretBytes = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, secretBytes, 0, (int)credential.CredentialBlobSize);
                    var base32Secret = Encoding.Unicode.GetString(secretBytes);

                    // Decode and return
                    return Base32Decode(base32Secret.Replace(" ", "").ToUpperInvariant());
                }
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }
        return null;
    }

    /// <summary>
    /// Check if an OTP secret exists for a specific account.
    /// </summary>
    public static bool HasSecretForAccount(string accountId)
    {
        var target = GetCredentialTargetForAccount(accountId);
        if (CredRead(target, CRED_TYPE.GENERIC, 0, out var credentialPtr))
        {
            CredFree(credentialPtr);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Delete OTP secret for a specific account.
    /// </summary>
    public static void DeleteSecretForAccount(string accountId)
    {
        var target = GetCredentialTargetForAccount(accountId);
        CredDelete(target, CRED_TYPE.GENERIC, 0);
    }

    /// <summary>
    /// Check if legacy OTP secret exists (for migration).
    /// </summary>
    public static bool HasLegacySecret()
    {
        if (CredRead(LegacyCredentialTarget, CRED_TYPE.GENERIC, 0, out var credentialPtr))
        {
            CredFree(credentialPtr);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Migrate legacy OTP secret to a specific account.
    /// </summary>
    public static bool MigrateLegacySecretToAccount(string accountId)
    {
        var secretBytes = LoadSecretFromTarget(LegacyCredentialTarget);
        if (secretBytes != null && secretBytes.Length > 0)
        {
            // Read the raw base32 secret from legacy target
            if (CredRead(LegacyCredentialTarget, CRED_TYPE.GENERIC, 0, out var credentialPtr))
            {
                try
                {
                    var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                    if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                    {
                        var rawSecretBytes = new byte[credential.CredentialBlobSize];
                        Marshal.Copy(credential.CredentialBlob, rawSecretBytes, 0, (int)credential.CredentialBlobSize);
                        var base32Secret = Encoding.Unicode.GetString(rawSecretBytes);

                        // Save to new account-specific target
                        var newTarget = GetCredentialTargetForAccount(accountId);
                        SaveSecretToTarget(newTarget, base32Secret);

                        // Delete legacy secret
                        CredDelete(LegacyCredentialTarget, CRED_TYPE.GENERIC, 0);
                        return true;
                    }
                }
                finally
                {
                    CredFree(credentialPtr);
                }
            }
        }
        return false;
    }

    private enum CRED_TYPE : uint { GENERIC = 1 }
    private enum CRED_PERSIST : uint { LOCAL_MACHINE = 2 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    #endregion
}
