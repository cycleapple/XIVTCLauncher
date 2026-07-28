using System.Diagnostics;
using System.IO;
using FFXIVSimpleLauncher.Models;

namespace FFXIVSimpleLauncher.Services;

/// <summary>
/// Manages account bindings and isolated Dalamud profile directories.
/// The legacy Dalamud\Config directory is treated as the built-in shared profile.
/// </summary>
public sealed class ProfileService
{
    public const string SharedProfileId = "";
    public const string SharedProfileName = "共用插件設定（目前設定）";

    private static readonly HashSet<string> CopyExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "logs",
            "log",
            "temp",
            "tmp",
            "crashes"
        };

    private readonly string _appRoot;
    private readonly string _dalamudRoot;
    private readonly string _profilesRoot;
    private readonly string _trashRoot;

    public ProfileService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FFXIVSimpleLauncher"))
    {
    }

    internal ProfileService(string appRoot)
    {
        _appRoot = Path.GetFullPath(appRoot);
        _dalamudRoot = Path.Combine(_appRoot, "Dalamud");
        _profilesRoot = Path.Combine(_appRoot, "Profiles");
        _trashRoot = Path.Combine(_profilesRoot, ".trash");
    }

    public IReadOnlyList<DalamudProfileOption> GetOptions(LauncherSettings settings)
    {
        var options = new List<DalamudProfileOption>
        {
            new(SharedProfileId, SharedProfileName)
        };

        options.AddRange(settings.DalamudProfiles
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(profile => new DalamudProfileOption(profile.Id, profile.Name)));

        return options;
    }

    public DalamudProfile? ResolveProfile(LauncherSettings settings, Account? account)
    {
        if (string.IsNullOrWhiteSpace(account?.DalamudProfileId))
            return null;

        return settings.DalamudProfiles.FirstOrDefault(
            profile => profile.Id == account.DalamudProfileId);
    }

    public DalamudProfilePaths ResolvePaths(LauncherSettings settings, Account? account)
    {
        var profile = ResolveProfile(settings, account);
        return ResolvePaths(profile?.Id);
    }

    public DalamudProfilePaths ResolvePaths(string? profileId)
    {
        var root = string.IsNullOrWhiteSpace(profileId)
            ? Path.Combine(_dalamudRoot, "Config")
            : GetProfileDalamudRoot(profileId);

        root = Path.GetFullPath(root);
        EnsureWithinAllowedRoot(root, string.IsNullOrWhiteSpace(profileId) ? _dalamudRoot : _profilesRoot);

        return new DalamudProfilePaths(
            root,
            Path.Combine(root, "dalamudConfig.json"),
            Path.Combine(root, "installedPlugins"),
            Path.Combine(root, "devPlugins"),
            Path.Combine(root, "logs"));
    }

    public DalamudProfile Create(
        LauncherSettings settings,
        string name,
        string? description = null,
        string? copyFromProfileId = null)
    {
        name = NormalizeName(name);
        EnsureUniqueName(settings, name);

        var profile = new DalamudProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description?.Trim() ?? string.Empty
        };

        var targetPaths = ResolvePaths(profile.Id);
        var targetProfileRoot = GetProfileRoot(profile.Id);
        try
        {
            Directory.CreateDirectory(targetPaths.Root);

            if (copyFromProfileId != null)
            {
                var sourcePaths = ResolveCopySource(settings, copyFromProfileId);
                if (Directory.Exists(sourcePaths.Root))
                {
                    CopyDirectoryRecursive(sourcePaths.Root, targetPaths.Root);
                }
            }

            EnsureProfileDirectories(targetPaths);
            settings.DalamudProfiles.Add(profile);
            return profile;
        }
        catch
        {
            if (Directory.Exists(targetProfileRoot))
            {
                Directory.Delete(targetProfileRoot, recursive: true);
            }

            throw;
        }
    }

    public void Update(LauncherSettings settings, string profileId, string name, string? description)
    {
        var profile = GetRequiredProfile(settings, profileId);
        name = NormalizeName(name);

        if (settings.DalamudProfiles.Any(
                candidate => candidate.Id != profileId &&
                             string.Equals(candidate.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException($"已存在名稱為「{name}」的插件設定檔。");
        }

        profile.Name = name;
        profile.Description = description?.Trim() ?? string.Empty;
    }

    public string Delete(LauncherSettings settings, string profileId)
    {
        var profile = GetRequiredProfile(settings, profileId);
        var profileRoot = GetProfileRoot(profile.Id);
        var trashedPath = string.Empty;

        if (Directory.Exists(profileRoot))
        {
            Directory.CreateDirectory(_trashRoot);
            trashedPath = Path.Combine(
                _trashRoot,
                $"{profile.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}");
            Directory.Move(profileRoot, trashedPath);
        }

        foreach (var account in settings.Accounts.Where(
                     account => account.DalamudProfileId == profile.Id))
        {
            account.DalamudProfileId = null;
        }

        settings.DalamudProfiles.Remove(profile);
        return trashedPath;
    }

    public int CountBoundAccounts(LauncherSettings settings, string profileId)
    {
        return settings.Accounts.Count(account => account.DalamudProfileId == profileId);
    }

    public void EnsureProfileDirectories(DalamudProfilePaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.InstalledPlugins);
        Directory.CreateDirectory(paths.DevPlugins);
        Directory.CreateDirectory(paths.Logs);
    }

    public void OpenProfileDirectory(string? profileId)
    {
        var paths = ResolvePaths(profileId);
        EnsureProfileDirectories(paths);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{paths.Root}\"",
            UseShellExecute = true
        });
    }

    public void NormalizeBindings(LauncherSettings settings)
    {
        settings.DalamudProfiles = settings.DalamudProfiles
            .Where(profile => IsValidProfileId(profile.Id))
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var validIds = settings.DalamudProfiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var account in settings.Accounts)
        {
            if (!string.IsNullOrWhiteSpace(account.DalamudProfileId) &&
                !validIds.Contains(account.DalamudProfileId))
            {
                account.DalamudProfileId = null;
            }
        }
    }

    private DalamudProfilePaths ResolveCopySource(
        LauncherSettings settings,
        string copyFromProfileId)
    {
        if (copyFromProfileId == SharedProfileId)
            return ResolvePaths(null);

        _ = GetRequiredProfile(settings, copyFromProfileId);
        return ResolvePaths(copyFromProfileId);
    }

    private string GetProfileRoot(string profileId)
    {
        EnsureValidProfileId(profileId);
        var path = Path.GetFullPath(Path.Combine(_profilesRoot, profileId));
        EnsureWithinAllowedRoot(path, _profilesRoot);
        return path;
    }

    private string GetProfileDalamudRoot(string profileId)
    {
        return Path.Combine(GetProfileRoot(profileId), "Dalamud");
    }

    private static DalamudProfile GetRequiredProfile(
        LauncherSettings settings,
        string profileId)
    {
        return settings.DalamudProfiles.FirstOrDefault(profile => profile.Id == profileId)
            ?? throw new InvalidOperationException("找不到指定的插件設定檔。");
    }

    private static string NormalizeName(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("插件設定檔名稱不能為空白。");
        if (name.Length > 60)
            throw new InvalidOperationException("插件設定檔名稱不能超過 60 個字元。");
        return name;
    }

    private static void EnsureUniqueName(LauncherSettings settings, string name)
    {
        if (settings.DalamudProfiles.Any(
                profile => string.Equals(profile.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException($"已存在名稱為「{name}」的插件設定檔。");
        }
    }

    private static void EnsureValidProfileId(string profileId)
    {
        if (!IsValidProfileId(profileId))
            throw new InvalidOperationException("插件設定檔 ID 格式無效。");
    }

    private static bool IsValidProfileId(string? profileId)
    {
        return profileId is { Length: 32 } &&
               profileId.All(Uri.IsHexDigit);
    }

    private static void EnsureWithinAllowedRoot(string path, string allowedRoot)
    {
        var normalizedRoot = Path.GetFullPath(allowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                normalizedPath.TrimEnd(Path.DirectorySeparatorChar),
                normalizedRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("插件設定檔路徑超出允許範圍。");
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(file, Path.Combine(destination, fileName), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (CopyExcludedDirectories.Contains(name))
                continue;

            CopyDirectoryRecursive(directory, Path.Combine(destination, name));
        }
    }
}
