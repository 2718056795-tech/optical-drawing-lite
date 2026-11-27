using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;
using lcdb.DrawingFrame;

namespace OtoCAD.Avalonia;

/// <summary>Expose the same local font files to Avalonia that the drawing engine uses.</summary>
internal sealed class UiFontCollection : IFontCollection
{
    private static readonly Uri CollectionUri = new("fonts:DrawingSerif");
    private readonly Dictionary<string, List<IGlyphTypeface>> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FontFamily> _families = [];
    private readonly bool _bundledOnly = AppContext.TryGetSwitch("OtoCAD.Drawing.UseBundledSerifOnly", out var bundled) && bundled;

    public Uri Key => CollectionUri;
    public int Count => _families.Count;
    public FontFamily this[int index] => _families[index];

    public static FontFamily Register()
    {
        var manager = FontManager.Current;
        var collection = new UiFontCollection();
        manager.AddFontCollection(collection);

        var preferred = new List<string>();
        foreach (var family in new[] { EngineeringSerif.LatinFamily, EngineeringSerif.ChineseFamily })
        {
            if (collection._bundledOnly && family == EngineeringSerif.LatinFamily) continue;
            if (collection._faces.ContainsKey(family))
                preferred.Add(FamilySource(family));
            else if (manager.SystemFonts.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)))
                preferred.Add(family);
        }
        preferred.Add("Microsoft YaHei UI");
        preferred.Add("Segoe UI");
        return new FontFamily(string.Join(", ", preferred));
    }

    private static string FamilySource(string name) => $"{CollectionUri}#{name}";

    public void Initialize(IFontManagerImpl fontManager)
    {
        var latin = _bundledOnly ? null : EngineeringSerif.FindLatinFont();
        Load(fontManager, EngineeringSerif.LatinFamily, latin);
        Load(fontManager, EngineeringSerif.LatinFamily, latin, FontSimulations.Bold);
        foreach (var weight in new[] { "Regular", "Medium", "Bold" })
            Load(fontManager, EngineeringSerif.ChineseFamily, EngineeringSerif.CjkPath(weight));
    }

    private void Load(IFontManagerImpl fontManager, string family, string? path, FontSimulations simulations = FontSimulations.None)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            if (!fontManager.TryCreateGlyphTypeface(stream, simulations, out var glyphs)) return;
            if (!_faces.TryGetValue(family, out var variants))
            {
                _faces.Add(family, variants = []);
                _families.Add(new FontFamily(FamilySource(family)));
            }
            variants.Add(glyphs);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
        [NotNullWhen(true)] out IGlyphTypeface? glyphTypeface)
    {
        glyphTypeface = null;
        if (!_faces.TryGetValue(familyName, out var variants)) return false;
        // Prefer the stronger face for a semibold heading when medium and bold are equidistant.
        glyphTypeface = variants.OrderBy(face => Math.Abs((int)face.Weight - (int)weight))
                                .ThenByDescending(face => face.Weight).First();
        return true;
    }

    public bool TryMatchCharacter(int codepoint, FontStyle style, FontWeight weight, FontStretch stretch,
        string? familyName, CultureInfo? culture, out Typeface typeface)
    {
        foreach (var name in _faces.Keys)
        {
            if (familyName != null && !string.Equals(name, familyName, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryGetGlyphTypeface(name, style, weight, stretch, out var face) &&
                face.TryGetGlyph((uint)codepoint, out var glyph) && glyph != 0)
            {
                typeface = new Typeface(new FontFamily(FamilySource(name)), style, weight, stretch);
                return true;
            }
        }
        typeface = default;
        return false;
    }

    public IEnumerator<FontFamily> GetEnumerator() => _families.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        foreach (var variants in _faces.Values)
            foreach (var face in variants)
                face.Dispose();
        _faces.Clear();
        _families.Clear();
    }
}
