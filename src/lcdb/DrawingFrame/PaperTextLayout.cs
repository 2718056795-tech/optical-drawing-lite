using System;
using System.Collections.Generic;
using System.Globalization;
using SkiaSharp;

namespace lcdb.DrawingFrame;

/// <summary>Shared CAD text face and line fitting in paper millimetres for the canvas and drawing exports.</summary>
public static class PaperTextLayout
{
    private static readonly Lazy<SKTypeface> Cjk = new(() =>
    {
        var manager = SKFontManager.Default;
        foreach (var family in new[]{"Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑", "SimHei", "黑体", "SimSun", "宋体",
            "Microsoft JhengHei", "PingFang SC", "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei"})
        {
            var face = manager.MatchFamily(family);
            if (face is not null && face.ContainsGlyph('文')) return face;
            face?.Dispose();
        }
        return manager.MatchCharacter('文') ?? SKTypeface.Default;
    });
    private static readonly Lazy<SKTypeface> Arial = new(() => SKFontManager.Default.MatchFamily("Arial"));
    public static SKTypeface ResolveFace(string font, string text)
    {
        var face = string.Equals(font,"Arial",StringComparison.OrdinalIgnoreCase) ? Arial.Value :
            string.IsNullOrEmpty(font) ? null : SKFontManager.Default.MatchFamily(font);
        if (face is null) return Cjk.Value;
        foreach (var ch in text)
            if (ch > 127 && !face.ContainsGlyph('文')) return Cjk.Value;
        return face;
    }

    public static double Width(string text, double height) => Width(text, height, false);

    public static double Width(string text, double height, bool useEngineeringSerif) =>
        Width(text, height, useEngineeringSerif ? EngineeringSerif.ProfileName : "Arial");

    internal static double Width(string text, double height, string font)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (font == EngineeringSerif.ProfileName)
        {
            double width = 0;
            foreach (var run in EngineeringSerif.Runs(text, height)) width += run.Width;
            return width;
        }
        using var paint = new SKPaint { Typeface = ResolveFace(font,text), TextSize = 256 };
        return paint.MeasureText(text) * height / 256;
    }

    /// <summary>Keep a one-line title inside its printable cell, reducing the requested height only if needed.</summary>
    public static double FitHeight(string text, double availableWidth, double requestedHeight) => FitHeight(text, availableWidth, requestedHeight, false);

    public static double FitHeight(string text, double availableWidth, double requestedHeight, bool useEngineeringSerif)
    {
        double width = Width(text, requestedHeight, useEngineeringSerif);
        return width > availableWidth ? requestedHeight * System.Math.Max(0.1, availableWidth) / width : requestedHeight;
    }

    public static List<(int Paragraph, string Text)> Wrap(string input, double width, double height) => Wrap(input, width, height, false);

    public static List<(int Paragraph, string Text)> Wrap(string input, double width, double height, bool useEngineeringSerif)
    {
        var lines = new List<(int,string)>();
        if (string.IsNullOrEmpty(input)) return lines;
        var paragraphs = input.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for (int paragraph = 0; paragraph < paragraphs.Length; paragraph++)
        {
            string text = paragraphs[paragraph];
            if (text.Length == 0) { lines.Add((paragraph,"")); continue; }
            int[] offsets = StringInfo.ParseCombiningCharacters(text);
            int End(int boundary) => boundary == offsets.Length ? text.Length : offsets[boundary];
            int start = 0;
            while (start < offsets.Length)
            {
                int lo=start+1, hi=System.Math.Min(offsets.Length,start+32);
                while (hi<offsets.Length && Width(text[offsets[start]..End(hi)],height, useEngineeringSerif)<=width)
                {
                    lo=hi;
                    hi+=System.Math.Min(hi-start,offsets.Length-hi);
                }
                while (lo<hi)
                {
                    int mid=lo+(hi-lo+1)/2;
                    if (Width(text[offsets[start]..End(mid)],height, useEngineeringSerif) <= width) lo=mid;
                    else hi=mid-1;
                }
                int next=lo;
                // Keep a Latin word or numeric expression together when there is space on the next line.
                static bool TokenCharacter(char ch) => char.IsAsciiLetterOrDigit(ch) || ".%°±′″–−+-×".Contains(ch);
                if (next<offsets.Length && TokenCharacter(text[offsets[next]]) &&
                    TokenCharacter(text[offsets[next-1]]))
                {
                    int boundary=next;
                    while (boundary>start && TokenCharacter(text[offsets[boundary-1]])) boundary--;
                    if (boundary>start) next=boundary;
                }
                if (next<offsets.Length && next>start+1 && "。，、；：！？）】》".Contains(text[offsets[next]]))
                    next--;
                lines.Add((paragraph,text[offsets[start]..End(next)]));
                start=next;
            }
        }
        return lines;
    }
}
