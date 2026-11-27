using lcdb.DrawingFrame;
using OtoCAD.Avalonia.Slim;

namespace OtoCAD.Avalonia;

public sealed record BatchPropertyResult(int Applied, int Unchanged, int Skipped, bool SourceAccepted, string Message);

public partial class MainWindow
{
    internal BatchPropertyResult ApplyTextToAllDrawings(CadCanvas sourceCanvas, DrawingFrame sourceFrame,
        PropertyCellHit sourceCell, string originalText, string editedText)
    {
        if (!sourceCanvas.GetAllEntities().Contains(sourceFrame))
            return new(0, 0, 0, false, "选中的图框已改变，请重新选择属性。");

        bool notes = TemplatePipeline.IsTechnicalNotesField(sourceFrame, sourceCell);
        var lines = notes ? new TechnicalNoteEdit(originalText, editedText, sourceCell.FieldKey) : null;
        // Applying to the first drawing can change a row's label. Match other drawings
        // against the source field as it was before this edit (also after a prior single confirmation).
        var matchFrame = (DrawingFrame)sourceFrame.Clone();
        if (sourceCell.RowIndex >= 0 && string.IsNullOrEmpty(sourceCell.FieldKey) && matchFrame is IPropertyZoneFrame zone
            && matchFrame.TemplateTextBindings?.ContainsKey($"c:{sourceCell.ColIndex}:r:{sourceCell.RowIndex}") != true)
            zone.SetCellText(sourceCell, originalText);
        return ApplyToDrawingTabs(sourceCanvas, canvas =>
        {
            string NextText(string current) => lines is null || ReferenceEquals(canvas, sourceCanvas)
                ? editedText : lines.Merge(current);
            var reference = ReferenceEquals(canvas, sourceCanvas) ? sourceFrame : matchFrame;
            if (!TemplatePipeline.TryPlanMatchingFrameEdit(reference, sourceCell, canvas.GetAllEntities(),
                    NextText, out var apply, out var reason))
                throw new InvalidOperationException(reason);
            if (apply is null) return false;
            canvas.ApplyEdit(apply);
            return true;
        });
    }

    internal BatchPropertyResult ApplyParameterToAllDrawings(CadCanvas sourceCanvas, string key, double value)
    {
        return ApplyToDrawingTabs(sourceCanvas, canvas =>
        {
            var entities = canvas.GetAllEntities();
            var parameter = TemplatePipeline.CorrespondingParameter(entities, key)
                ?? throw new InvalidOperationException("没有对应的可编辑尺寸。");
            if (parameter.Value.Equals(value)) return false;
            string error = "";
            if (!canvas.TryApplyEdit(() => TemplatePipeline.TrySetDirectParameter(entities, parameter.Key, value, out error)))
                throw new InvalidOperationException(error);
            return true;
        });
    }

    private BatchPropertyResult ApplyToDrawingTabs(CadCanvas source, Func<CadCanvas, bool> apply)
    {
        var drawings = GeneratedDrawingTabs().Select(tab => (CadCanvas)tab.Content!).ToList();
        if (!ReferenceEquals(source, _activeCanvas) || !drawings.Remove(source))
            return new(0, 0, 0, false, "请在打开的加工图中选中要批量修改的属性。");

        drawings.Insert(0, source);
        int applied = 0, unchanged = 0;
        var issues = new List<string>();
        foreach (var canvas in drawings)
        {
            try
            {
                if (apply(canvas)) applied++;
                else unchanged++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or OverflowException)
            {
                if (ReferenceEquals(canvas, source))
                    return new(0, 0, drawings.Count, false, "当前图纸无法应用，已保留草稿。" + ex.Message);
                string name = _sheets.TryGetValue(canvas, out var title) ? title
                    : Path.GetFileNameWithoutExtension(canvas.CurrentFilePath) ?? "加工图";
                issues.Add(name + "：" + ex.Message);
            }
        }
        if (applied > 0) EvaluateChecklist();
        string summary = $"已修改 {applied} 张，已有相同值 {unchanged} 张，跳过 {issues.Count} 张。";
        _status.Text = summary;
        string details = issues.Count == 0 ? summary : summary + Environment.NewLine + string.Join(Environment.NewLine, issues);
        return new(applied, unchanged, issues.Count, true, details);
    }
}
