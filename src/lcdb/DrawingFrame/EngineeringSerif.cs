using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiaSharp;

namespace lcdb.DrawingFrame;

/// <summary>Printable lettering for new sheets, measured in paper millimetres from the same glyph runs as the renderers.</summary>
public static class EngineeringSerif
{
    public const string ProfileName = "OtoCAD.EngineeringSerif";
    public const string LatinFamily = "Anthropic Serif Web";
    public const string ChineseFamily = "Source Han Serif SC";
    private const string LatinFile = "AnthropicSerifWebText-Regular.ttf";

    public static string CjkPath(string weight) => Path.Combine(AppContext.BaseDirectory, "Fonts", $"SourceHanSerifSC-{weight}.otf");

    public static string? FindLatinFont()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var directory in new[] { Path.Combine(local, "OpticalDrawingLite", "Fonts"),
            Path.Combine(local, "Microsoft", "Windows", "Fonts"), Environment.GetFolderPath(Environment.SpecialFolder.Fonts) })
        {
            if (string.IsNullOrEmpty(directory)) continue;
            var path = Path.Combine(directory, LatinFile);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // Windows and Skia expose either name ID 1 or the typographic family (name ID 16) of this face.
    public static bool IsLatinFamily(string family) =>
        string.Equals(family, LatinFamily, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(family, "Anthropic Serif Web Text", StringComparison.OrdinalIgnoreCase);
    private static bool IsExpected(string name, string family) => family == LatinFamily
        ? IsLatinFamily(name) : string.Equals(name, family, StringComparison.OrdinalIgnoreCase);

    private static SKTypeface? ExactFace(string? path, string family)
    {
        var face = path != null && File.Exists(path) ? SKTypeface.FromFile(path) : null;
        if (face != null && IsExpected(face.FamilyName, family)) return face;
        face?.Dispose();
        face = SKFontManager.Default.MatchFamily(family);
        if (face != null && IsExpected(face.FamilyName, family)) return face;
        face?.Dispose();
        if (family == LatinFamily)
        {
            face = SKFontManager.Default.MatchFamily("Anthropic Serif Web Text");
            if (face != null && IsLatinFamily(face.FamilyName)) return face;
            face?.Dispose();
        }
        return null;
    }

    private static readonly Lazy<SKTypeface> Chinese = new(() =>
        ExactFace(CjkPath("Regular"), ChineseFamily) ?? PaperTextLayout.ResolveFace("Arial", "文"));
    private static readonly Lazy<SKTypeface?> Latin = new(() => ExactFace(FindLatinFont(), LatinFamily));

    public readonly record struct FontRun(string Text, SKTypeface Face, double Width);

    public static List<FontRun> Runs(string text, double height)
    {
        var runs = new List<FontRun>();
        if (string.IsNullOrEmpty(text)) return runs;
        var chinese = Chinese.Value;
        var latin = AppContext.TryGetSwitch("OtoCAD.Drawing.UseBundledSerifOnly", out var bundledOnly) && bundledOnly
            ? chinese : Latin.Value ?? chinese;
        using var measure = new SKPaint { TextSize = 256 };
        void AddRun(int from, int to, SKTypeface face)
        {
            var part = text[from..to];
            measure.Typeface = face;
            runs.Add(new FontRun(part, face, measure.MeasureText(part) * height / 256.0));
        }
        if (ReferenceEquals(chinese, latin))
        {
            AddRun(0, text.Length, chinese);
            return runs;
        }

        int start = 0;
        SKTypeface? current = null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            int index = elements.ElementIndex;
            int code = char.IsSurrogatePair(text, index) ? char.ConvertToUtf32(text, index) : text[index];
            bool eastAsian = code is (>= 0x2e80 and <= 0xa4cf) or (>= 0xac00 and <= 0xd7af)
                or (>= 0xf900 and <= 0xfaff) or (>= 0xfe10 and <= 0xfe4f)
                or (>= 0xff00 and <= 0xffef) or (>= 0x20000 and <= 0x323af);
            var face = eastAsian || !latin.ContainsGlyph(code) ? chinese : latin;
            if (current != null && !ReferenceEquals(current, face))
            {
                AddRun(start, index, current);
                start = index;
            }
            current = face;
        }
        AddRun(start, text.Length, current!);
        return runs;
    }
}
