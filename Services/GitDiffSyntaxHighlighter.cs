using System.IO;
using HotKeyCommandApp.Models;
using LibGit2Sharp;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace HotKeyCommandApp.Services
{
    public sealed record GitDiffSyntaxSpan(int Start, int Length, string Color);

    public static class GitDiffSyntaxHighlighter
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly RegistryOptions Options = new(ThemeName.DarkPlus);
        private static readonly Registry Registry = new(Options);

        public static IReadOnlyList<GitDiffSyntaxSpan>[] Highlight(
            IReadOnlyList<GitDiffLine> lines, string path, CancellationToken cancellationToken,
            string repositoryPath = null, GitDiffFile file = null)
        {
            var result = Enumerable.Range(0, lines.Count).Select(_ => (IReadOnlyList<GitDiffSyntaxSpan>)Array.Empty<GitDiffSyntaxSpan>()).ToArray();
            if (string.IsNullOrEmpty(path) || lines.Count == 0) return result;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".xaml" or ".csproj" or ".props" or ".targets" or ".resx") extension = ".xml";
            Gate.Wait(cancellationToken);
            try
            {
                string scope = Options.GetScopeByExtension(extension);
                if (string.IsNullOrEmpty(scope)) return result;
                var grammar = Registry.LoadGrammar(scope);
                if (grammar == null) return result;
                var theme = Registry.GetTheme();
                var (oldSource, newSource) = ReadSources(repositoryPath, file, cancellationToken);
                IStateStack oldState = null, newState = null;
                int oldLine = 0, newLine = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = lines[i];
                    if (line.Kind == GitDiffLineKind.Hunk)
                    {
                        if (oldSource == null) { oldState = null; oldLine = 0; }
                        if (newSource == null) { newState = null; newLine = 0; }
                        continue;
                    }
                    if (line.Kind == GitDiffLineKind.Notice) continue;
                    if (line.Text.Length > 4000) { oldState = null; newState = null; continue; }

                    ITokenizeLineResult tokens;
                    if (line.Kind == GitDiffLineKind.Deleted)
                    {
                        AdvanceToLine(grammar, oldSource, line.OldNumber, ref oldLine, ref oldState, cancellationToken);
                        tokens = grammar.TokenizeLine(line.Text, oldState, TimeSpan.FromMilliseconds(100));
                        oldState = tokens.RuleStack;
                        if (int.TryParse(line.OldNumber, out int number)) oldLine = number;
                    }
                    else
                    {
                        AdvanceToLine(grammar, newSource, line.NewNumber, ref newLine, ref newState, cancellationToken);
                        tokens = grammar.TokenizeLine(line.Text, newState, TimeSpan.FromMilliseconds(100));
                        newState = tokens.RuleStack;
                        if (int.TryParse(line.NewNumber, out int number)) newLine = number;
                        if (line.Kind == GitDiffLineKind.Context)
                        {
                            AdvanceToLine(grammar, oldSource, line.OldNumber, ref oldLine, ref oldState, cancellationToken);
                            oldState = grammar.TokenizeLine(line.Text, oldState, TimeSpan.FromMilliseconds(100)).RuleStack;
                            if (int.TryParse(line.OldNumber, out number)) oldLine = number;
                        }
                    }
                    var spans = new List<GitDiffSyntaxSpan>();
                    foreach (var token in tokens.Tokens)
                    {
                        int start = Math.Clamp(token.StartIndex, 0, line.Text.Length);
                        int end = Math.Clamp(token.EndIndex, start, line.Text.Length);
                        var rule = theme.Match(token.Scopes)?.FirstOrDefault(match => match.foreground > 0);
                        string color = rule == null ? null : theme.GetColor(rule.foreground);
                        if (end > start && !string.IsNullOrEmpty(color)) spans.Add(new GitDiffSyntaxSpan(start, end - start, color));
                    }
                    result[i] = spans;
                }
                return result;
            }
            finally { Gate.Release(); }
        }

        private static void AdvanceToLine(IGrammar grammar, string[] source, string lineNumber,
            ref int currentLine, ref IStateStack state, CancellationToken token)
        {
            if (source == null || !int.TryParse(lineNumber, out int target)) return;
            if (target <= currentLine || target - currentLine > 20000) { state = null; currentLine = target - 1; return; }
            while (currentLine < target - 1 && currentLine < source.Length)
            {
                token.ThrowIfCancellationRequested();
                string text = source[currentLine++];
                state = text.Length > 4000 ? null : grammar.TokenizeLine(text, state, TimeSpan.FromMilliseconds(100)).RuleStack;
            }
        }

        private static (string[] Old, string[] New) ReadSources(string repositoryPath, GitDiffFile file, CancellationToken token)
        {
            if (string.IsNullOrEmpty(repositoryPath) || file == null) return (null, null);
            try
            {
                using var repository = new LibGit2Sharp.Repository(repositoryPath);
                string oldPath = string.IsNullOrEmpty(file.OldPath) ? file.Path : file.OldPath;
                string[] oldSource = file.IsStaged
                    ? ReadBlob(repository.Head.Tip?.Tree[oldPath]?.Target as Blob, token)
                    : ReadIndexBlob(repository, oldPath, token);
                string[] newSource;
                if (file.IsStaged) newSource = ReadIndexBlob(repository, file.Path, token);
                else
                {
                    string fullPath = Path.Combine(repositoryPath, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    newSource = File.Exists(fullPath) && new FileInfo(fullPath).Length <= 8 * 1024 * 1024
                        ? ReadLines(File.OpenRead(fullPath), token) : null;
                }
                return (oldSource, newSource);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibGit2SharpException or ArgumentException)
            {
                return (null, null);
            }
        }

        private static string[] ReadBlob(Blob blob, CancellationToken token) =>
            blob == null || blob.Size > 8 * 1024 * 1024 ? null : ReadLines(blob.GetContentStream(), token);

        private static string[] ReadIndexBlob(LibGit2Sharp.Repository repository, string path, CancellationToken token)
        {
            var entry = repository.Index[path];
            return entry == null ? null : ReadBlob(repository.Lookup<Blob>(entry.Id), token);
        }

        private static string[] ReadLines(Stream stream, CancellationToken token)
        {
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                token.ThrowIfCancellationRequested();
                lines.Add(line);
            }
            return lines.ToArray();
        }
    }
}
