using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OtoCAD.Avalonia.Services;

/// <summary>Upgrade only byte-identifiable factory copies. A customer's same-name template is user data.</summary>
internal static class FactoryPrintTemplates
{
    // Original distributed ZIP files and V4 templates emitted on first launch by previous releases.
    // V4's generated document title and two timestamps vary by installation.
    private static readonly Dictionary<string, (string Zip, string? V4)> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["默认模板"] = ("7E0DD076E1D8EC8F97CE01515297944F9B3861B8552CA2D9D3FF4BECEBFCD837", null),
        ["ISO三列表"] = ("A38745FB1FC5E829ADDED203E5E8F7079CF7C5FDB25A4FB738BB49D7DD7471C8",
            "0F57C92EE8AF244353D482722678E3B55A1DB47CF37F8768C874A136977A1979"),
        ["中文内部图"] = ("2A0A9DD32B38D147BAA35D9362B711BF352997EF137BE479BC66EBBD70A9176F",
            "48827674EB579184D07770AA05233419693EE5D7107478C6E8E93220A544DE24"),
        ["GB-my-1"] = ("4B3BB7332DBB88FE5BF305468ECE0E965C42FFF7FD7391615CCEB6DAD1E3D4B6",
            "D8BDA000EFC65422A378388BE06CC860DCBC7FDE3FC41365EE069E05C5557833")
    };

    internal static void EnsureCurrent(string path, Action<string> create)
    {
        if (!File.Exists(path)) { AppDataPaths.CreateFileIfMissing(path, create); return; }

        // Replacing an untouched old factory file is optional for read-only libraries.
        // Keep its original bytes as a separate backup, leaving any regular drawing .bak alone.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".otocad";
        try
        {
            if (!IsPreviousFactory(path)) return;
            create(temporary);
            using var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (!Matches(current, Path.GetFileNameWithoutExtension(path))) return;
            string backup = path + ".pre-1.13.5";
            if (File.Exists(backup)) backup += "." + Guid.NewGuid().ToString("N");
            File.Replace(temporary, path, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool IsPreviousFactory(string path)
    {
        using var input = File.OpenRead(path);
        return Matches(input, Path.GetFileNameWithoutExtension(path));
    }

    private static bool Matches(Stream input, string name)
    {
        // All earlier factory packages are smaller than 32 KiB. Other documents remain untouched.
        if (input.Length > 32768 || !Legacy.TryGetValue(name, out var expected)) return false;
        if (Convert.ToHexString(SHA256.HashData(input)) == expected.Zip) return true;
        input.Position = 0;
        if (expected.V4 is null || input.ReadByte() != '{') return false;
        input.Position = 0;
        using var reader = new StreamReader(input, Encoding.UTF8, leaveOpen: true);
        string raw = reader.ReadToEnd().ReplaceLineEndings("\n");
        try
        {
            using var parsed = JsonDocument.Parse(raw);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("document", out var document) ||
                document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("Title", out var title) ||
                title.ValueKind != JsonValueKind.String) return false;
            string text = title.GetString() ?? "";
            string prefix = name + ".otocad.";
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(text[prefix.Length..], "N", out _))
                return false;
            foreach (var field in new[] { "Title", "Created", "Modified" })
            {
                if (!document.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) return false;
                string label = "    \"" + field + "\": ";
                raw = raw.Replace(label + value.GetRawText() + ",\n", label + "\"\",\n", StringComparison.Ordinal);
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))) == expected.V4;
        }
        catch (JsonException) { return false; }
    }
}
