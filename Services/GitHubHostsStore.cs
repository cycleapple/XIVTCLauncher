using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace FFXIVSimpleLauncher.Services;

/// <summary>Edits only our marked block, preserving all other hosts bytes.</summary>
internal sealed class GitHubHostsStore(string path)
{
    internal const string Begin = "# >>> XIVTCLauncher GitHub routes >>>";
    internal const string End = "# <<< XIVTCLauncher GitHub routes <<<";
    internal static readonly string[] Domains = ["raw.githubusercontent.com", "release-assets.githubusercontent.com"];
    internal static string SystemPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
    internal static GitHubHostsStore SystemStore => new(SystemPath);

    internal string Read() => Encoding.Latin1.GetString(File.ReadAllBytes(path));

    internal static (int Start, int Length) FindBlock(string text)
    {
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        if (start < 0 && end < 0) return (-1, 0);
        if (start < 0 || end < start || text.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0 ||
            text.IndexOf(End, end + End.Length, StringComparison.Ordinal) >= 0 ||
            (start > 0 && text[start - 1] != '\n') ||
            (text[start + Begin.Length] != '\r' && text[start + Begin.Length] != '\n') ||
            (end > 0 && text[end - 1] != '\n'))
            throw new InvalidOperationException("GitHub hosts 標記不完整或重複，請先檢查 hosts 檔案。");
        var finish = end + End.Length;
        if (finish < text.Length && text[finish] == '\r') finish++;
        if (finish < text.Length && text[finish] == '\n') finish++;
        else if (finish < text.Length) throw new InvalidOperationException("GitHub hosts 結尾標記不完整。");
        return (start, finish - start);
    }

    internal static string WithoutBlock(string text)
    {
        var block = FindBlock(text);
        if (block.Start < 0) return text;
        var body = text.Substring(block.Start, block.Length);
        var separator = body.Contains("# Added separator: CRLF\n") || body.Contains("# Added separator: CRLF\r\n") ? "\r\n" :
            body.Contains("# Added separator: LF\n") ? "\n" : "";
        if (block.Start + block.Length == text.Length && separator.Length > 0 && block.Start >= separator.Length &&
            text.Substring(block.Start - separator.Length, separator.Length) == separator)
            return text.Remove(block.Start - separator.Length, block.Length + separator.Length);
        return text.Remove(block.Start, block.Length);
    }

    internal static string[] Conflicts(string text) => WithoutBlock(text).Split('\n')
        .Select(line => line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length > 1)
        .SelectMany(parts => parts.Skip(1))
        .Where(host => Domains.Contains(host.TrimEnd('.'), StringComparer.OrdinalIgnoreCase)).Distinct().ToArray();

    internal static IReadOnlyDictionary<string, IPAddress> Routes(string text)
    {
        var block = FindBlock(text);
        var routes = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        if (block.Start < 0) return routes;
        foreach (var line in text.Substring(block.Start, block.Length).Split('\n'))
        {
            var parts = line.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && Domains.Contains(parts[1], StringComparer.OrdinalIgnoreCase) && IPAddress.TryParse(parts[0], out var ip))
            {
                if (!routes.TryAdd(parts[1], ip)) throw new InvalidOperationException("GitHub hosts 有重複網域。");
            }
        }
        return routes;
    }

    internal static string ApplyText(string text, IPAddress address, DateTimeOffset testedAt)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("只支援已測試的 IPv4 節點。");
        if (Conflicts(text).Length > 0) throw new InvalidOperationException("其他 hosts 設定已指定 GitHub 網域，請先使用原工具還原。");
        var clean = WithoutBlock(text);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var separator = clean.Length > 0 && !clean.EndsWith('\n');
        if (separator) clean += newline;
        return clean + Begin + newline + (separator ? "# Added separator: " + (newline == "\r\n" ? "CRLF" : "LF") + newline : "") +
            "# Tested UTC: " + testedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + newline +
            string.Join(newline, Domains.Select(host => $"{address} {host}")) + newline + End + newline;
    }

    internal void Write(Func<string, string> transform)
    {
        // FileShare.None prevents a competing editor from being overwritten while this transaction runs.
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (bytes.Contains((byte)0)) throw new InvalidDataException("hosts 使用不支援的編碼，未修改檔案。");
        var original = Encoding.Latin1.GetString(bytes);
        var updated = Encoding.Latin1.GetBytes(transform(original));
        if (bytes.SequenceEqual(updated)) return;
        var backup = path + ".xivtc-github.bak";
        if (!File.Exists(backup))
        {
            using var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            copy.Write(bytes);
            copy.Flush(true);
        }
        try
        {
            file.Position = 0;
            file.Write(updated);
            file.SetLength(updated.Length);
            file.Flush(true);
        }
        catch
        {
            file.Position = 0;
            file.Write(bytes);
            file.SetLength(bytes.Length);
            file.Flush(true);
            throw;
        }
    }

    internal static IPAddress? ManagedAddress(string host)
    {
        if (!Domains.Contains(host, StringComparer.OrdinalIgnoreCase)) return null;
        return ManagedAddress(SystemStore.Read(), host);
    }

    internal static IPAddress? ManagedAddress(string text, string host)
    {
        if (!Domains.Contains(host, StringComparer.OrdinalIgnoreCase)) return null;
        var routes = Routes(text);
        if (!routes.TryGetValue(host, out var ip)) return null;
        if (Conflicts(text).Length > 0) throw new InvalidOperationException("GitHub hosts 設定衝突，請先還原其他工具的設定。");
        return ip;
    }
}
