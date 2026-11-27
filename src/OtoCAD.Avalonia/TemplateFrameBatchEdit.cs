using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using lcdb;
using lcdb.DrawingFrame;
using OtoCAD.Avalonia.Templating;

namespace OtoCAD.Avalonia.Slim;

public static partial class TemplatePipeline
{
    private static List<IsoSpecColumn>? EditColumns(DrawingFrame frame) =>
        frame switch { TemplateTable t => t.Columns, OpticalDrawingFrame o => o.Columns, _ => Document1Sheet.Columns(frame) };

    private static TemplateTextBinding? RowBinding(DrawingFrame frame, int column, int row) =>
        frame.TemplateTextBindings?.GetValueOrDefault($"c:{column}:r:{row}");

    private static string[] RowTokens(string expression) =>
        Regex.Matches(expression, "【([^】]+)】").Select(m => m.Groups[1].Value).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    private static string CanonicalRowToken(string token) => token switch
    {
        "材料1" or "材料2" => "材料", "折射率1" or "折射率2" => "折射率", "阿贝数1" or "阿贝数2" => "阿贝数",
        _ => token
    };

    private static string ColumnRole(DrawingFrame frame, IsoSpecColumn col, int index)
    {
        var tokens = col.Rows.SelectMany((row, r) => RowTokens(RowBinding(frame, index, r)?.Expression ?? row)).ToHashSet();
        if (tokens.Contains("胶合面片1半径")) return "contact:first";
        if (tokens.Contains("胶合面片2半径")) return "contact:second";
        if (tokens.Contains("前表面半径") || tokens.Contains("前表面通光口径")) return "surface:front";
        if (tokens.Contains("后表面半径") || tokens.Contains("后表面通光口径")) return "surface:back";
        if (tokens.Overlaps(new[] { "材料2", "折射率2", "阿贝数2" })) return "material:second";
        if (tokens.Overlaps(new[] { "材料", "折射率", "阿贝数", "材料1", "折射率1", "阿贝数1" })) return "material:first";
        return col.Title.Trim() switch
        {
            "左表面" or "前表面" or "表面1" => "surface:front",
            "右表面" or "后表面" or "表面4" => "surface:back",
            "表面2" => "contact:first", "表面3" => "contact:second",
            var title => "column:" + title
        };
    }

    private static string RowLabel(string row)
    {
        var match = Regex.Match(row, @"^\s*(\d+\s*/|[\p{L}_][\p{L}_\d -]*?(?=\s*[:：])|倒角|曲率半径|[ΦØφ]e)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    // Capture the template before a three-column singlet is expanded into a six-column doublet.
    // Physical values and later manual edits therefore cannot change what counts as a sibling frame.
    private static string FrameLayoutKey(DrawingFrame frame)
    {
        if (frame is DataDrivenFrame custom) custom.EnsureColumns();
        var fields = TextSlots(frame).Select(s => (Key: s.Key, Value: s.Get()))
            .OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new { s.Key, s.Value });
        var layout = new
        {
            frame.className, frame.PaperWidth, frame.PaperHeight,
            Columns = EditColumns(frame)?.Select(c => new { c.Title, c.RowKeys }),
            Definition = (frame as DataDrivenFrame)?.Definition,
            Fields = fields
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(layout)));
    }

    private static bool SameFrameFamily(DrawingFrame source, DrawingFrame candidate)
    {
        if (source.GetType() != candidate.GetType() || source.PaperWidth != candidate.PaperWidth ||
            source.PaperHeight != candidate.PaperHeight) return false;
        if (source.SourceFrameLayoutKey is { } original && candidate.SourceFrameLayoutKey is { } other)
            return original == other;
        if (source is DataDrivenFrame a && candidate is DataDrivenFrame b &&
            (a.Definition.Name != b.Definition.Name ||
             !a.Definition.NamedFields().Select(f => (f.Key, f.Label)).SequenceEqual(b.Definition.NamedFields().Select(f => (f.Key, f.Label))))) return false;
        string[] Roles(DrawingFrame frame) => EditColumns(frame)?.Select((c, i) => ColumnRole(frame, c, i))
            .Where(role => role != "contact:first" && role != "contact:second" && role != "material:second")
            .OrderBy(role => role, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        return Roles(source).SequenceEqual(Roles(candidate));
    }

    private static bool IsStaticDesignRow(DrawingFrame frame, IsoSpecColumn col, int c, int r)
    {
        if (RowTokens(RowBinding(frame, c, r)?.Expression ?? col.Rows[r]).Length > 0) return false;
        string key = col.RowKeys.ElementAtOrDefault(r) ?? "";
        if (Regex.IsMatch(key, "^(radius|clear_aperture|mechanical_diameter|thickness|material_name|n_d|v_d)($|_)", RegexOptions.IgnoreCase))
            return true;
        return Regex.IsMatch(col.Rows[r], @"^\s*(?:曲率半径|[Rr]\s*[-+∞=\d]|[ØΦφ]e|通光口径|机械外径|中心厚度|材料\s*[:：　 ]|折射率|阿贝数|[nv]_?d\b)", RegexOptions.IgnoreCase);
    }

    private static bool SameRow(DrawingFrame source, PropertyCellHit sourceCell, IsoSpecColumn sourceColumn,
        DrawingFrame target, int columnIndex, int rowIndex, IsoSpecColumn targetColumn)
    {
        string sourceText = RowBinding(source, sourceCell.ColIndex, sourceCell.RowIndex)?.Expression ?? sourceColumn.Rows[sourceCell.RowIndex];
        string targetText = RowBinding(target, columnIndex, rowIndex)?.Expression ?? targetColumn.Rows[rowIndex];
        var sourceTokens = RowTokens(sourceText).Select(CanonicalRowToken).OrderBy(t => t).ToArray();
        var targetTokens = RowTokens(targetText).Select(CanonicalRowToken).OrderBy(t => t).ToArray();
        if (sourceTokens.Length > 0 || targetTokens.Length > 0) return sourceTokens.Length > 0 && sourceTokens.SequenceEqual(targetTokens);
        string? key = sourceColumn.RowKeys.ElementAtOrDefault(sourceCell.RowIndex);
        string? other = targetColumn.RowKeys.ElementAtOrDefault(rowIndex);
        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(other) && !key.StartsWith("custom_", StringComparison.Ordinal))
            return key == other && (RowLabel(sourceText) == "" || RowLabel(targetText) == "" || RowLabel(sourceText) == RowLabel(targetText));
        string label = RowLabel(sourceText);
        return label.Length > 0 && label == RowLabel(targetText);
    }

    private static PropertyCellHit? MatchingNamedCell(DrawingFrame source, PropertyCellHit from, DrawingFrame target)
    {
        if (IsTechnicalNotesField(source, from))
        {
            string? notes = target switch
            {
                IsoLensDrawingFrame or GbLensDrawingFrame => "Notes",
                DataDrivenFrame custom => custom.Definition.FirstBand(FrameDefinition.KindNotes)?.Fields.FirstOrDefault()?.Key,
                _ => null
            };
            return string.IsNullOrEmpty(notes) ? null : new PropertyCellHit("技术要求", notes, default);
        }
        // A generic "name" in an unrelated custom definition need not mean the same thing.
        if (source.GetType() == target.GetType() && IsNamedFrameField(target, from))
        {
            if (source is not DataDrivenFrame a || target is not DataDrivenFrame b || a.Definition.Name == b.Definition.Name)
                return from;
            var original = a.Definition.NamedFields().Where(f => f.Key == from.FieldKey).Select(f => f.Label).Distinct().ToArray();
            var matching = b.Definition.NamedFields().Where(f => f.Key == from.FieldKey).Select(f => f.Label).Distinct().ToArray();
            if (original.Length == 1 && original[0].Length > 0 && original.SequenceEqual(matching)) return from;
        }
        if (source is IsoLensDrawingFrame && target is DataDrivenFrame || source is DataDrivenFrame && target is IsoLensDrawingFrame)
        {
            if (from.FieldKey is "ProjectPart" or "DrawnBy" or "ApprovedBy" or "DrawDate" or "Rev" &&
                IsNamedFrameField(target, from)) return from;
        }
        return null;
    }

    private static bool PlanNamedField(DrawingFrame sourceFrame, PropertyCellHit sourceCell, IReadOnlyList<Entity> entities,
        Func<string, string> editText, out Action? apply, out string reason)
    {
        apply = null;
        reason = "";
        if (!IsNamedFrameField(sourceFrame, sourceCell) || IsGeneratedScaleField(sourceFrame, sourceCell))
            { reason = "该字段不允许批量修改"; return false; }
        var matches = entities.OfType<DrawingFrame>().Where(f => f is IPropertyZoneFrame)
            .Select(f => (Frame: f, Cell: MatchingNamedCell(sourceFrame, sourceCell, f))).Where(x => x.Cell.HasValue).ToList();
        if (matches.Any(m => ReferenceEquals(m.Frame, sourceFrame))) matches = matches.Where(m => ReferenceEquals(m.Frame, sourceFrame)).ToList();
        if (matches.Count > 1)
        {
            var sameFamily = matches.Where(m => SameFrameFamily(sourceFrame, m.Frame)).ToList();
            if (sameFamily.Count > 0) matches = sameFamily;
        }
        if (matches.Count > 1) matches = matches.Where(m => m.Frame.Origin == sourceFrame.Origin).ToList();
        if (matches.Count != 1) { reason = "没有唯一对应的图签字段"; return false; }
        var (target, hit) = matches[0];
        var cell = hit!.Value;
        if (IsGeneratedScaleField(target, cell)) { reason = "图纸比例由零件图区决定"; return false; }
        string current = ((IPropertyZoneFrame)target).GetCellText(cell);
        string edited = editText(current);
        if (current != edited || CanOverrideFrameField(target, cell)) apply = () => SetFrameFieldText(target, cell, edited);
        return true;
    }

    public static bool CanBatchFrameField(DrawingFrame frame, PropertyCellHit cell)
    {
        if (!string.IsNullOrEmpty(cell.FieldKey)) return IsNamedFrameField(frame, cell) && !IsGeneratedScaleField(frame, cell);
        var columns = EditColumns(frame);
        int c = cell.ColIndex, r = cell.RowIndex;
        if (columns is null || c < 0 || c >= columns.Count || r < 0 || r >= columns[c].Rows.Count) return false;
        var col = columns[c];
        if (IsStaticDesignRow(frame, col, c, r)) return false;
        string text = RowBinding(frame, c, r)?.Expression ?? col.Rows[r];
        string? key = col.RowKeys.ElementAtOrDefault(r);
        return RowTokens(text).Length > 0 || RowLabel(text).Length > 0 ||
            (!string.IsNullOrEmpty(key) && !key.StartsWith("custom_", StringComparison.Ordinal));
    }

    /// <summary>Plans one change without writing to either document. A null action on success means no change.</summary>
    public static bool TryPlanMatchingFrameEdit(DrawingFrame sourceFrame, PropertyCellHit sourceCell,
        IReadOnlyList<Entity> targetEntities, Func<string, string> editText, out Action? apply, out string reason)
    {
        apply = null;
        reason = "";
        if (!string.IsNullOrEmpty(sourceCell.FieldKey))
            return PlanNamedField(sourceFrame, sourceCell, targetEntities, editText, out apply, out reason);
        var frames = targetEntities.OfType<DrawingFrame>().Where(f => f is IPropertyZoneFrame && SameFrameFamily(sourceFrame, f)).ToList();
        if (frames.Contains(sourceFrame)) frames = new() { sourceFrame };
        else if (frames.Count > 1) frames = frames.Where(f => f.Origin == sourceFrame.Origin).ToList();
        if (frames.Count != 1) { reason = "没有唯一对应的模板图框"; return false; }
        var target = frames[0];
        var zone = (IPropertyZoneFrame)target;
        var originalColumns = EditColumns(sourceFrame);
        var targetColumns = EditColumns(target);
        int c = sourceCell.ColIndex, r = sourceCell.RowIndex;
        if (originalColumns is null || targetColumns is null || c < 0 || c >= originalColumns.Count ||
            r < 0 || r >= originalColumns[c].Rows.Count)
            { reason = "请选择有明确指标的参数表行"; return false; }
        var sourceColumn = originalColumns[c];
        string role = ColumnRole(sourceFrame, sourceColumn, c);
        var matches = new List<(int C, int R)>();
        if (ReferenceEquals(sourceFrame, target)) matches.Add((c, r));
        else for (int i = 0; i < targetColumns.Count; i++)
        {
            var col = targetColumns[i];
            if (ColumnRole(target, col, i) != role) continue;
            for (int j = 0; j < col.Rows.Count; j++)
                if (SameRow(sourceFrame, sourceCell, sourceColumn, target, i, j, col)) matches.Add((i, j));
        }
        if (matches.Count != 1) { reason = matches.Count == 0 ? "没有相同表面或材料的对应指标" : "存在多个同名指标"; return false; }
        var (tc, tr) = matches[0];
        if (!ReferenceEquals(sourceFrame, target) && (IsStaticDesignRow(sourceFrame, sourceColumn, c, r) ||
            IsStaticDesignRow(target, targetColumns[tc], tc, tr)))
        {
            reason = "此行只有固定设计值，无法区分各镜片的数值与说明";
            return false;
        }
        var dst = new PropertyCellHit(targetColumns[tc].Title, sourceCell.Label, tc, tr, default);
        var binding = RowBinding(target, tc, tr);
        string before = binding?.Expression ?? zone.GetCellText(dst);
        string after = editText(before);
        string[] required = RowTokens(before);
        if (!RowTokens(after).SequenceEqual(required))
        {
            // A singlet's material tokens become material-1 tokens in the first glass of a doublet.
            var possible = required.Distinct().ToLookup(CanonicalRowToken);
            after = Regex.Replace(after, "【([^】]+)】", match =>
            {
                var alternatives = possible[CanonicalRowToken(match.Groups[1].Value)].ToArray();
                return alternatives.Length == 1 ? Token(alternatives[0]) : match.Value;
            });
            if (!RowTokens(after).SequenceEqual(required)) { reason = "编辑会替换该图纸自身的设计参数"; return false; }
        }
        if (after != before)
        {
            string actual = after;
            apply = () =>
            {
                if (binding is null) { zone.SetCellText(dst, actual); return; }
                binding.Expression = actual;
                binding.LastRendered = S(actual, binding.LastValues);
                zone.SetCellText(dst, binding.LastRendered);
            };
        }
        return true;
    }
}
