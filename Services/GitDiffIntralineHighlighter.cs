using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    using System.Text.RegularExpressions;
    public readonly record struct GitDiffChangeSpan(int Start, int Length);

    public static class GitDiffIntralineHighlighter
    {
        private static readonly Regex Word = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);
        public static IReadOnlyList<GitDiffChangeSpan>[] Highlight(IReadOnlyList<GitDiffLine> lines, CancellationToken token)
        {
            var result = Enumerable.Range(0, lines.Count).Select(_ => (IReadOnlyList<GitDiffChangeSpan>)Array.Empty<GitDiffChangeSpan>()).ToArray();
            int budget = 2000000;
            for (int i = 0; i < lines.Count;)
            {
                token.ThrowIfCancellationRequested();
                if (lines[i].Kind != GitDiffLineKind.Deleted) { i++; continue; }
                int deletedStart = i;
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Deleted) i++;
                int addedStart = i;
                while (i < lines.Count && lines[i].Kind == GitDiffLineKind.Added) i++;
                if (addedStart - deletedStart == 1 && i - addedStart == 1)
                    Compare(lines[deletedStart].Text, lines[addedStart].Text,
                        result, deletedStart, addedStart, ref budget, token);
            }
            return result;
        }

        private static void Compare(string oldText, string newText, IReadOnlyList<GitDiffChangeSpan>[] result,
            int oldIndex, int newIndex, ref int budget, CancellationToken token)
        {
            int prefix = 0;
            while (prefix < oldText.Length && prefix < newText.Length && oldText[prefix] == newText[prefix]) prefix++;
            int oldEnd = oldText.Length, newEnd = newText.Length;
            while (oldEnd > prefix && newEnd > prefix && oldText[oldEnd - 1] == newText[newEnd - 1]) { oldEnd--; newEnd--; }
            int oldLength = oldEnd - prefix, newLength = newEnd - prefix;
            long cost = (long)(oldLength + 1) * (newLength + 1);
            if (cost > 40000 || cost > budget)
                return;
            budget -= (int)cost;
            var lengths = new int[oldLength + 1, newLength + 1];
            for (int x = oldLength - 1; x >= 0; x--)
            {
                token.ThrowIfCancellationRequested();
                for (int y = newLength - 1; y >= 0; y--)
                    lengths[x, y] = oldText[prefix + x] == newText[prefix + y]
                        ? lengths[x + 1, y + 1] + 1 : Math.Max(lengths[x + 1, y], lengths[x, y + 1]);
            }
            var oldChanged = new bool[oldLength];
            var newChanged = new bool[newLength];
            int a = 0, b = 0;
            while (a < oldLength || b < newLength)
            {
                if (a < oldLength && b < newLength && oldText[prefix + a] == newText[prefix + b]) { a++; b++; }
                else if (b < newLength && (a == oldLength || lengths[a, b + 1] >= lengths[a + 1, b])) newChanged[b++] = true;
                else oldChanged[a++] = true;
            }
            var oldSpans = ExpandWords(oldText, Spans(oldChanged, prefix));
            var newSpans = ExpandWords(newText, Spans(newChanged, prefix));
            if (!MostlyUnchanged(oldText, oldSpans) || !MostlyUnchanged(newText, newSpans)) return;
            result[oldIndex] = oldSpans;
            result[newIndex] = newSpans;
        }

        private static bool MostlyUnchanged(string text, IReadOnlyList<GitDiffChangeSpan> spans)
        {
            int total = text.Count(character => !char.IsWhiteSpace(character));
            int changed = spans.Sum(span => text.Substring(span.Start, span.Length).Count(character => !char.IsWhiteSpace(character)));
            return total > 0 && changed * 2 < total;
        }

        private static IReadOnlyList<GitDiffChangeSpan> ExpandWords(string text, IReadOnlyList<GitDiffChangeSpan> spans)
        {
            var changed = new bool[text.Length];
            foreach (var span in spans) Array.Fill(changed, true, span.Start, span.Length);
            foreach (Match word in Word.Matches(text))
                if (Enumerable.Range(word.Index, word.Length).Any(index => changed[index]))
                    Array.Fill(changed, true, word.Index, word.Length);
            return Spans(changed, 0);
        }

        private static IReadOnlyList<GitDiffChangeSpan> Spans(bool[] changed, int offset)
        {
            var spans = new List<GitDiffChangeSpan>();
            for (int i = 0; i < changed.Length;)
            {
                if (!changed[i]) { i++; continue; }
                int start = i++;
                while (i < changed.Length && changed[i]) i++;
                spans.Add(new GitDiffChangeSpan(offset + start, i - start));
            }
            return spans;
        }
    }
}
