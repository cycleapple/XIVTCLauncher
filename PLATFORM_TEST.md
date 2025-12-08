# Platform Abstraction Layer - Testing Guide

## Quick Test Checklist

### 1. Compilation Test

```bash
cd XIVTCLauncher
dotnet build
```

Expected: Clean build with no errors.

Possible errors to fix:
- Missing `using` statements
- Namespace conflicts
- Property/method access issues

### 2. Platform Detection Test

Add this temporary test code to verify platform detection works:

**File: `ViewModels/MainViewModel.cs`**

Add to the constructor after existing initialization:

```csharp
public MainViewModel()
{
    _settingsService = new SettingsService();
    _loginService = new LoginService();
    _dalamudService = new DalamudService();
    _credentialService = PlatformServiceFactory.GetCredentialService();
    _settings = _settingsService.Load();

    // Subscribe to Dalamud status updates
    _dalamudService.StatusChanged += status => StatusMessage = status;

    // === ADD THIS TEST CODE ===
    TestPlatformServices();
    // === END TEST CODE ===
}

private void TestPlatformServices()
{
    try
    {
        // Test 1: Platform detection
        var platform = PlatformServiceFactory.CurrentPlatform;
        StatusMessage = $"✓ Platform detected: {platform}\n";

        // Test 2: Paths service
        var paths = PlatformServiceFactory.GetPlatformPaths();
        StatusMessage += $"✓ App Data Path: {paths.GetApplicationDataPath()}\n";
        StatusMessage += $"✓ Dalamud Path: {paths.GetDalamudBasePath()}\n";

        // Test 3: Credential service
        var credService = PlatformServiceFactory.GetCredentialService();
        StatusMessage += $"✓ Credential service: {credService.GetType().Name}\n";

        // Test 4: Game launcher
        var gameLauncher = PlatformServiceFactory.GetGameLauncher();
        StatusMessage += $"✓ Game launcher: {gameLauncher.GetType().Name}\n";

        // Test 5: Wine service (macOS only)
        if (platform == PlatformType.MacOS)
        {
            var wine = PlatformServiceFactory.GetWineService();
            bool wineDetected = wine.DetectWine();
            StatusMessage += $"✓ Wine detected: {wineDetected}\n";
            if (wineDetected)
            {
                StatusMessage += $"✓ Wine path: {wine.WinePath}\n";
                StatusMessage += $"✓ Wine version: {wine.GetWineVersion()}\n";
            }
        }

        StatusMessage += "\n✅ All platform services initialized successfully!";
    }
    catch (Exception ex)
    {
        StatusMessage = $"❌ Platform service test failed:\n{ex.Message}\n\n{ex.StackTrace}";
    }
}
```

### 3. Run Application

```bash
dotnet run
```

**On Windows**, you should see:
```
✓ Platform detected: Windows
✓ App Data Path: C:\Users\<username>\AppData\Roaming\FFXIVSimpleLauncher
✓ Dalamud Path: C:\Users\<username>\AppData\Roaming\FFXIVSimpleLauncher\Dalamud
✓ Credential service: WindowsCredentialService
✓ Game launcher: WindowsGameLauncher

✅ All platform services initialized successfully!
```

**On macOS**, you should see:
```
✓ Platform detected: MacOS
✓ App Data Path: /Users/<username>/Library/Application Support/FFXIVSimpleLauncher
✓ Dalamud Path: /Users/<username>/Library/Application Support/FFXIVSimpleLauncher/Dalamud
✓ Credential service: MacOSCredentialService
✓ Game launcher: MacOSGameLauncher
✓ Wine detected: true/false
✓ Wine path: /usr/local/bin/wine64
✓ Wine version: wine-9.x.x

✅ All platform services initialized successfully!
```

### 4. Test Credential Storage

Test saving and retrieving credentials:

```csharp
// In MainViewModel or create a test method
private void TestCredentials()
{
    var credService = PlatformServiceFactory.GetCredentialService();

    // Test save
    credService.SavePassword("test@example.com", "testpassword123");
    StatusMessage = "✓ Password saved\n";

    // Test retrieve
    var retrieved = credService.GetPassword("test@example.com");
    if (retrieved == "testpassword123")
    {
        StatusMessage += "✓ Password retrieved correctly\n";
    }
    else
    {
        StatusMessage += $"❌ Password mismatch: expected 'testpassword123', got '{retrieved}'\n";
    }

    // Test delete
    credService.DeletePassword("test@example.com");
    var afterDelete = credService.GetPassword("test@example.com");
    if (afterDelete == null)
    {
        StatusMessage += "✓ Password deleted successfully\n";
    }
    else
    {
        StatusMessage += $"❌ Password not deleted: still got '{afterDelete}'\n";
    }
}
```

### 5. Test Path Creation

Verify that application directories are created correctly:

**Windows:**
```powershell
dir $env:APPDATA\FFXIVSimpleLauncher
# Should show: Dalamud folder, settings.json, etc.
```

**macOS:**
```bash
ls ~/Library/Application\ Support/FFXIVSimpleLauncher
# Should show: Dalamud folder, settings.json, etc.
```

### 6. Test Game Launch (Windows)

With the platform abstraction, game launching should work exactly as before on Windows:

1. Configure game path in Settings
2. Click Login
3. Complete web login
4. Game should launch normally

### 7. Test Wine Detection (macOS)

If testing on macOS:

```bash
# Install Wine if not already installed
brew tap gcenx/wine
brew install --cask --no-quarantine wine-crossover

# Run launcher
dotnet run
```

The status message should show Wine detection results.

## Expected Behavior

### Windows
- ✅ Everything works as before
- ✅ Credentials stored in Windows Credential Manager
- ✅ Game launches directly
- ✅ Dalamud injection works normally

### macOS (after Avalonia migration)
- ✅ Credentials stored in macOS Keychain
- ✅ Game launches through Wine
- ✅ Dalamud injection through Wine
- ⚠️ Requires Wine installed

## Common Issues & Solutions

### Issue: Compilation Errors

**Error:** `The type or namespace name 'Platform' does not exist`

**Solution:** Make sure all new files are included in the project:
```bash
# Check .csproj includes Platform folder
dotnet clean
dotnet build
```

### Issue: Platform Service Not Found

**Error:** `PlatformServiceFactory could not be resolved`

**Solution:** Add using statement:
```csharp
using FFXIVSimpleLauncher.Services.Platform;
```

### Issue: Wine Not Detected on macOS

**Error:** Status shows "Wine detected: false"

**Solution:**
```bash
# Install Wine
brew tap gcenx/wine
brew install --cask --no-quarantine wine-crossover

# Or use standard Wine
brew install --formula wine
```

### Issue: Keychain Access Denied (macOS)

**Error:** Security command fails

**Solution:**
```bash
# Grant terminal access to Keychain
# System Preferences > Security & Privacy > Privacy > Automation
# Allow Terminal/IDE to control Keychain Access
```

### Issue: DalamudService Runtime Not Found (macOS)

**Solution:**
```bash
# Install .NET Runtime
brew install dotnet

# Or let DalamudService download it automatically
# (Currently implemented for Windows, may need adjustment for macOS)
```

## Regression Testing

After implementing platform abstraction, test these existing features on Windows:

- [ ] Login with saved credentials
- [ ] Login with web browser
- [ ] OTP auto-fill
- [ ] Dalamud download and injection
- [ ] Manual Dalamud path
- [ ] Settings save/load
- [ ] Game version detection
- [ ] All UI elements work
- [ ] No crashes or errors

## Performance Testing

Platform abstraction should have minimal performance impact:

- Factory methods use singleton pattern (one-time initialization)
- Platform detection happens once per service
- No additional I/O operations
- Same memory footprint

## Security Testing

### Credential Storage

**Windows:**
```powershell
# Verify credentials are in Credential Manager
cmdkey /list | findstr FFXIVSimpleLauncher
```

**macOS:**
```bash
# Verify credentials are in Keychain
security find-generic-password -s "FFXIVSimpleLauncher"
```

Both should show encrypted storage (not plain text in settings.json).

## Next Steps After Testing

Once platform abstraction is verified working:

1. **If keeping WPF (Windows only):**
   - Remove test code
   - Update README to mention platform abstraction
   - Tag as "Platform-Ready" version
   - Ship Windows version

2. **If migrating to Avalonia (Cross-platform):**
   - Follow `NEXT_STEPS.md` for Avalonia migration
   - Port UI to Avalonia
   - Test on both Windows and macOS
   - Ship cross-platform version

## Test Report Template

```
# Platform Abstraction Test Report

Date: ___________
Tester: ___________
Platform: Windows / macOS (circle one)

## Build Test
- [ ] Clean build: PASS / FAIL
- [ ] No warnings: PASS / FAIL
- [ ] All files included: PASS / FAIL

## Platform Detection
- [ ] Correct platform detected: ___________
- [ ] Paths service works: PASS / FAIL
- [ ] Credential service works: PASS / FAIL
- [ ] Game launcher works: PASS / FAIL

## Functional Tests (Windows)
- [ ] Login works: PASS / FAIL
- [ ] Game launches: PASS / FAIL
- [ ] Dalamud injection: PASS / FAIL
- [ ] Settings persist: PASS / FAIL

## macOS-Specific (if applicable)
- [ ] Wine detected: YES / NO
- [ ] Wine version: ___________
- [ ] Keychain access: PASS / FAIL
- [ ] Wine prefix created: PASS / FAIL

## Issues Found
1. ___________
2. ___________
3. ___________

## Overall Result
Platform abstraction: READY FOR PRODUCTION / NEEDS FIXES
```
