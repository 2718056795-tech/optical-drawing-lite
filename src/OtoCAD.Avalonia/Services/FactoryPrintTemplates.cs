using System.IO.Compression;
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

    // V1.13.5 wrote the same three original layouts as V4 on first launch and V5 on upgrade.
    // The generated filename and timestamps vary. Other metadata, styles and all entities must match.
    private static readonly Dictionary<string, (string Plain, string Archive)> Print135 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB-my-1"] = ("75411BB4B6A6DD546F5823B3A484155730ECD729707852CDAE3E1F0BB21B1266",
            "F8F40C78E0BFE6883B6AC8063C78DD3C590DD53BD9194188A5BBDB1560DCD146"),
        ["ISO三列表"] = ("F35D982295EA37042174E21C9F97C6F5D3B06E95F49E66DB62730A8CD30BD042",
            "06D5CBE8A6571FE9ED91D03653F0908EBF0C2CA26EC709A931B6827250D6D70A"),
        ["中文内部图"] = ("3DE8911BE745DB94E1DA5395C90BAA2D4BEEB0CFA77E9F636D3660B0D33C6892",
            "20CA9461FAA7F65B805E9BB6A8D3AA9F1A0204313288F0CE17BEB1E98F27EAA9"),
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
            string backup = path + ".pre-1.13.6";
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
        string wholeFile = Convert.ToHexString(SHA256.HashData(input));
        if (wholeFile == expected.Zip || (name == "默认模板" && wholeFile == "A815CCFD3DE4D8DD7A65A76EE107DCB753B7066AF91FCE2652C90869BD79E148")) return true;
        if (!Print135.TryGetValue(name, out var previous)) return false;
        input.Position = 0;
        if (input.ReadByte() == 'P') return MatchesGeneratedArchive(input, name, previous.Archive);
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
            string plain = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            return plain == expected.V4 || plain == previous.Plain;
        }
        catch (JsonException) { return false; }
    }

    private static bool MatchesGeneratedArchive(Stream input, string name, string expected)
    {
        input.Position = 0;
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            string[] names = ["META.json", "MODEL.json", "STYLES.json", "manifest.json"];
            if (zip.Entries.Count != names.Length) return false;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var part in names)
            {
                var entry = zip.GetEntry(part);
                if (entry is null || entry.Length > 65536) return false;
                using var source = new StreamReader(entry.Open(), Encoding.UTF8);
                var raw = source.ReadToEnd().ReplaceLineEndings("\n");
                if (part is "META.json" or "manifest.json")
                {
                    using var document = JsonDocument.Parse(raw);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return false;
                    string[] fields = part == "META.json" ? ["Title", "Created", "Modified"] : ["created"];
                    foreach (var field in fields)
                    {
                        if (!root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) return false;
                        if (field == "Title")
                        {
                            string title = value.GetString() ?? "";
                            string prefix = name + ".otocad.";
                            if (!title.StartsWith(prefix, StringComparison.Ordinal) ||
                                !Guid.TryParseExact(title[prefix.Length..], "N", out _)) return false;
                        }
                        else if (!value.TryGetDateTime(out _)) return false;
                        string label = "  \"" + field + "\": ";
                        raw = raw.Replace(label + value.GetRawText(), label + "\"\"", StringComparison.Ordinal);
                    }
                }
                hash.AppendData(Encoding.UTF8.GetBytes(raw));
                hash.AppendData([0]);
            }
            return Convert.ToHexString(hash.GetHashAndReset()) == expected;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException) { return false; }
    }
}
