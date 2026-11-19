using System.IO;

namespace HotKeyCommandApp.Models
{
    public sealed class GitConsoleHistory
    {
        private const int MaxVisibleLines = 400;
        private readonly List<string> _entries = new();
        private readonly List<string> _lines = new();
        private string _header = string.Empty;
        private int _activeIndex = -1;
        private int _omittedLines;

        public string LatestBody
        {
            get
            {
                if (_lines.Count == 0) return string.Empty;
                string body = string.Join("\n", _lines);
                return _omittedLines == 0 ? body : $"... {_omittedLines} lines omitted ...\n{body}";
            }
        }

        public void SetEntry(string header, string content, bool replaceCurrent = false)
        {
            _header = header;
            _lines.Clear();
            _omittedLines = 0;
            using var reader = new StringReader(content.TrimEnd());
            while (reader.ReadLine() is string line) AppendLine(line);

            if (!replaceCurrent || _activeIndex < 0 || _activeIndex >= _entries.Count)
            {
                _entries.Add(BuildEntry());
                _activeIndex = _entries.Count - 1;
            }
            else
            {
                _entries[_activeIndex] = BuildEntry();
            }
        }

        public void AppendLines(IEnumerable<string> lines)
        {
            foreach (string line in lines) AppendLine(line);
        }

        public string Render(bool historyMode)
        {
            if (_activeIndex >= 0 && _activeIndex < _entries.Count)
                _entries[_activeIndex] = BuildEntry();

            return historyMode
                ? "[コンソール履歴 (Console History)]\n=================================\n" + string.Join("\n\n---------------------------------\n", _entries)
                : LatestBody;
        }

        private void AppendLine(string line)
        {
            _lines.Add(line);
            int overflow = _lines.Count - MaxVisibleLines;
            if (overflow <= 0) return;
            _lines.RemoveRange(0, overflow);
            _omittedLines += overflow;
        }

        private string BuildEntry()
        {
            string body = LatestBody.TrimEnd();
            if (string.IsNullOrEmpty(_header)) return body;
            return string.IsNullOrEmpty(body) ? $"[{_header}]" : $"[{_header}]\n{body}";
        }
    }
}
