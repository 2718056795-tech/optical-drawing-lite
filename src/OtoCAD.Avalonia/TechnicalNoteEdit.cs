using System.Text.RegularExpressions;

namespace OtoCAD.Avalonia;

/// <summary>Apply a change to common clauses while keeping other drawings' local notes.</summary>
internal sealed class TechnicalNoteEdit
{
    private readonly string[] before;
    private readonly string[] after;
    private readonly int selectedLine;
    private readonly List<(int Old, int OldCount, int New, int NewCount)> changes = new();
    private static readonly Regex NumberPrefix = new(
        @"^\s*[(（]?(\d+)\s*[.．、:：)）]", RegexOptions.CultureInvariant);

    public TechnicalNoteEdit(string original, string edited, string? field)
    {
        before = Lines(original);
        after = Lines(edited);
        selectedLine = field?.StartsWith("NoteLine_", StringComparison.Ordinal) == true
            && int.TryParse(field.AsSpan(9), out var index) ? index : -1;
        // Unchanged rows anchor disjoint edits. Queues handle repeated rows without a quadratic diff table.
        var positions = after.Select((text, i) => (text, i)).GroupBy(x => x.text, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<int>(g.Select(x => x.i)), StringComparer.Ordinal);
        int oldStart = 0, newStart = 0;
        for (int old = 0; old < before.Length; old++)
        {
            if (!positions.TryGetValue(before[old], out var indices)) continue;
            while (indices.Count > 0 && indices.Peek() < newStart) indices.Dequeue();
            if (indices.Count == 0) continue;
            int next = indices.Dequeue();
            if (old > oldStart || next > newStart) changes.Add((oldStart, old-oldStart, newStart, next-newStart));
            oldStart = old + 1;
            newStart = next + 1;
        }
        if (oldStart < before.Length || newStart < after.Length)
            changes.Add((oldStart, before.Length-oldStart, newStart, after.Length-newStart));
    }

    private static string[] Lines(string text) => text.Length == 0 ? Array.Empty<string>()
        : text.ReplaceLineEndings("\n").Split('\n');

    private static string? Number(string line)
    {
        var match = NumberPrefix.Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    public string Merge(string current)
    {
        var target = Lines(current);
        if (target.SequenceEqual(after)) return current;
        if (target.SequenceEqual(before)) return string.Join("\n", after);
        if (changes.Count == 0 && (selectedLine < 0 || selectedLine >= before.Length))
            throw new InvalidOperationException("请先修改要同步的技术要求，或选中一个具体条款。");

        var targetNotes = new TargetNoteLines(before, target);
        if (changes.Count == 0) targetNotes.Set(selectedLine, after[selectedLine]);
        foreach (var change in changes)
        {
            int paired = Math.Min(change.OldCount, change.NewCount);
            for (int i = 0; i < paired; i++) targetNotes.Set(change.Old + i, after[change.New + i]);
            for (int i = paired; i < change.OldCount; i++) targetNotes.Set(change.Old + i, null);
            if (change.NewCount > paired)
                targetNotes.Insert(change.Old + change.OldCount, after.Skip(change.New + paired).Take(change.NewCount - paired));
        }

        return targetNotes.Render();
    }

    private sealed class TargetNoteLines(string[] before, string[] target)
    {
        // All matches refer to the original target array, so a deletion cannot shift a later edit.
        private readonly Dictionary<int, string?> edits = new Dictionary<int, string?>();
        private readonly Dictionary<int, int> matched = new Dictionary<int, int>();
        private readonly Dictionary<int, List<string>> inserts = new Dictionary<int, List<string>>();
        private int Find(int old, string? replacement = null)
        {
            if (matched.TryGetValue(old, out var known)) return known;
            string original = before[old];
            string? number = Number(original);
            var hits = Enumerable.Range(0, target.Length).Where(i => number is not null
                ? Number(target[i]) == number : target[i] == original || target[i] == replacement).Take(2).ToArray();
            if (hits.Length == 1) return hits[0];
            if (number is null && target.Length == before.Length && Number(target[old]) is null)
                return old;
            return -1;
        }
        public void Set(int old, string? value)
        {
            int at = Find(old, value);
            if (at < 0 && value is null && !target.Any(line =>
                    Number(before[old]) is string number ? Number(line) == number : line == before[old])) return;
            if (at < 0 || !edits.TryAdd(at, value))
                throw new InvalidOperationException($"技术要求第 {old + 1} 行缺失或无法唯一对应。");
            matched[old] = at;
        }
        public void Insert(int oldEnd, IEnumerable<string> text)
        {
            var addition = text.ToList();
            if (addition.Count == 0) return;
            int at = -1;
            if (oldEnd < before.Length) at = Find(oldEnd);
            if (at < 0 && oldEnd > 0)
            {
                int preceding = Find(oldEnd - 1);
                if (preceding >= 0) at = preceding + 1;
            }
            if (at < 0 && before.Length == 0) at = target.Length;
            if (at < 0) throw new InvalidOperationException("目标图纸缺少定位新增条款的相邻行。");
            bool AlreadyThere(int start) => start >= 0 && start + addition.Count <= target.Length
                && target.Skip(start).Take(addition.Count).SequenceEqual(addition);
            if (AlreadyThere(at) || AlreadyThere(at-addition.Count)) return;
            foreach (var line in addition)
            {
                string? number = Number(line);
                if (number is not null && target.Any(item => Number(item) == number))
                    throw new InvalidOperationException($"技术要求第 {number} 条已存在不同内容。");
            }
            if (!inserts.TryGetValue(at, out var items)) inserts[at] = items = new List<string>();
            items.AddRange(addition);
        }

        public string Render()
        {
            var result = new List<string>();
            for (int i = 0; i <= target.Length; i++)
            {
                if (inserts.TryGetValue(i, out var lines)) result.AddRange(lines);
                if (i == target.Length) break;
                if (!edits.TryGetValue(i, out var value)) result.Add(target[i]);
                else if (value is not null) result.Add(value);
            }
            return string.Join("\n", result);
        }
    }
}
