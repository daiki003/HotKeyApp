using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views.Controls
{
    public sealed class GitDiffTextView : Grid
    {
        private const double DefaultGutterWidth = 120;
        private readonly TextBox _textBox;
        private readonly DiffBackground _background;
        private bool _rowsDirty;
        private bool _updatePending;
        private CancellationTokenSource _highlightCts;
        public event EventHandler SelectionChanged;

        public GitDiffTextView()
        {
            Background = Brushes.Transparent;
            _textBox = new TextBox
            {
                Margin = new Thickness(DefaultGutterWidth, 0, 0, 0),
                Padding = new Thickness(0, 2, 16, 2),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brushes.Transparent,
                CaretBrush = Brushes.White,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _background = new DiffBackground(_textBox);
            Children.Add(_background);
            Children.Add(_textBox);
            _textBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RequestRowsUpdate()));
            _textBox.SizeChanged += (_, _) => RequestRowsUpdate();
            _textBox.Loaded += (_, _) => RequestRowsUpdate();
            _textBox.IsVisibleChanged += (_, _) => RequestRowsUpdate();
            _textBox.LayoutUpdated += (_, _) => { if (_rowsDirty) QueueRowsUpdate(); };
            _textBox.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetLines(IReadOnlyList<GitDiffLine> lines, GitDiffFile file = null, string repositoryPath = null)
        {
            CancelHighlighting();
            _background.Lines = lines;
            _background.Syntax = Array.Empty<IReadOnlyList<GitDiffSyntaxSpan>>();
            _background.ClearRows();
            UpdateGutterWidth();
            _textBox.Text = string.Join("\r\n", lines.Select(line => line.Text));
            _textBox.ScrollToHome();
            RequestRowsUpdate();
            if (lines.Count > 0 && file != null)
            {
                _highlightCts = new CancellationTokenSource();
                _ = HighlightAsync(lines, file, repositoryPath, _highlightCts.Token);
            }
        }

        public void CancelHighlighting()
        {
            _highlightCts?.Cancel();
            _highlightCts?.Dispose();
            _highlightCts = null;
        }

        private async Task HighlightAsync(IReadOnlyList<GitDiffLine> lines, GitDiffFile file, string repositoryPath, CancellationToken token)
        {
            try
            {
                var syntax = await Task.Run(() => GitDiffSyntaxHighlighter.Highlight(lines, file.Path, token, repositoryPath, file), token);
                if (token.IsCancellationRequested) return;
                _background.Syntax = syntax;
                _background.InvalidateVisual();
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        public void SetFontSize(double fontSize)
        {
            _textBox.SetCurrentValue(Control.FontSizeProperty, fontSize);
            UpdateGutterWidth();
            _background.ClearRows();
            RequestRowsUpdate();
        }

        private void UpdateGutterWidth()
        {
            int digits = Math.Max(3, _background.Lines.Select(line => Math.Max(line.OldNumber?.Length ?? 0, line.NewNumber?.Length ?? 0))
                .DefaultIfEmpty(0).Max());
            double numberWidth = Math.Max(48, Math.Ceiling(_textBox.FontSize * 0.65 * digits) + 9);
            double markerWidth = Math.Max(24, Math.Ceiling(_textBox.FontSize * 1.4));
            _background.NumberColumnWidth = numberWidth;
            _background.MarkerColumnWidth = markerWidth;
            _textBox.Margin = new Thickness(numberWidth * 2 + markerWidth, 0, 0, 0);
        }

        public IReadOnlyList<int> GetSelectedLineIndices()
        {
            if (_textBox.LineCount <= 0) return Array.Empty<int>();
            int start = _textBox.SelectionStart;
            int end = start + Math.Max(0, _textBox.SelectionLength - 1);
            int first = _textBox.GetLineIndexFromCharacterIndex(start);
            int last = _textBox.GetLineIndexFromCharacterIndex(end);
            if (first < 0 || last < first) return Array.Empty<int>();
            return Enumerable.Range(first, last - first + 1).ToArray();
        }

        private void RequestRowsUpdate()
        {
            _rowsDirty = true;
            QueueRowsUpdate();
        }

        private void QueueRowsUpdate()
        {
            if (_updatePending) return;
            _updatePending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _updatePending = false;
                if (_rowsDirty) _rowsDirty = !_background.UpdateRows();
            });
        }

        private sealed class DiffBackground : FrameworkElement
        {
            private static readonly Brush DefaultBackground = Brush("#1E1E1E");
            private static readonly Brush AddedBackground = Brush("#183D2A");
            private static readonly Brush DeletedBackground = Brush("#4B2428");
            private static readonly Brush HunkBackground = Brush("#20364B");
            private static readonly Brush NumberForeground = Brush("#999999");
            private static readonly Brush MarkerForeground = Brush("#C8C8C8");
            private static readonly Brush TextForeground = Brush("#D4D4D4");
            private readonly TextBox _textBox;
            private readonly Dictionary<string, Brush> _syntaxBrushes = new(StringComparer.OrdinalIgnoreCase);
            private IReadOnlyList<VisibleRow> _rows = Array.Empty<VisibleRow>();
            public double NumberColumnWidth { get; set; } = 48;
            public double MarkerColumnWidth { get; set; } = 24;

            private readonly record struct VisibleRow(GitDiffLine Line, Rect Bounds, int Index);

            public DiffBackground(TextBox textBox) => _textBox = textBox;
            public IReadOnlyList<GitDiffLine> Lines { get; set; } = Array.Empty<GitDiffLine>();
            public IReadOnlyList<GitDiffSyntaxSpan>[] Syntax { get; set; } = Array.Empty<IReadOnlyList<GitDiffSyntaxSpan>>();

            public void ClearRows()
            {
                _rows = Array.Empty<VisibleRow>();
                InvalidateVisual();
            }

            public bool UpdateRows()
            {
                if (!_textBox.IsLoaded || !_textBox.IsVisible || !_textBox.IsMeasureValid || !_textBox.IsArrangeValid) return false;
                if (Lines.Count == 0) { ClearRows(); return true; }

                try
                {
                    int lineCount = _textBox.LineCount;
                    int first = _textBox.GetFirstVisibleLineIndex();
                    int last = Math.Min(_textBox.GetLastVisibleLineIndex(), Math.Min(lineCount, Lines.Count) - 1);
                    if (lineCount <= 0 || first < 0 || last < first) return false;

                    var rows = new List<VisibleRow>();
                    for (int i = first; i <= last; i++)
                    {
                        int index = _textBox.GetCharacterIndexFromLineIndex(i);
                        if (index < 0) continue;
                        Rect rect = _textBox.GetRectFromCharacterIndex(index);
                        if (!rect.IsEmpty) rows.Add(new VisibleRow(Lines[i], rect, i));
                    }
                    _rows = rows;
                    InvalidateVisual();
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // テキスト更新直後は WPF の表示行情報が古い場合があるため、次のレイアウト後に再取得する。
                    return false;
                }
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                drawingContext.DrawRectangle(DefaultBackground, null, new Rect(RenderSize));
                if (_rows.Count == 0) return;
                double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var typeface = new Typeface(_textBox.FontFamily, _textBox.FontStyle, _textBox.FontWeight, _textBox.FontStretch);

                foreach (var row in _rows)
                {
                    var line = row.Line;
                    Rect rect = row.Bounds;
                    Brush background = line.Kind switch
                    {
                        GitDiffLineKind.Added => AddedBackground,
                        GitDiffLineKind.Deleted => DeletedBackground,
                        GitDiffLineKind.Hunk => HunkBackground,
                        _ => DefaultBackground
                    };
                    drawingContext.DrawRectangle(background, null, new Rect(0, rect.Top, ActualWidth, rect.Height));
                    DrawText(drawingContext, line.OldNumber, NumberForeground, typeface, pixelsPerDip, NumberColumnWidth - 9, rect.Top, false);
                    DrawText(drawingContext, line.NewNumber, NumberForeground, typeface, pixelsPerDip, NumberColumnWidth * 2 - 9, rect.Top, false);
                    DrawText(drawingContext, line.Marker, MarkerForeground, typeface, pixelsPerDip, NumberColumnWidth * 2 + MarkerColumnWidth / 2, rect.Top, true);
                    if (string.IsNullOrEmpty(line.Text)) continue;
                    var text = new FormattedText(line.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        typeface, _textBox.FontSize, TextForeground, pixelsPerDip);
                    if (row.Index < Syntax.Length)
                    {
                        foreach (var span in Syntax[row.Index])
                        {
                            if (!_syntaxBrushes.TryGetValue(span.Color, out var brush))
                                _syntaxBrushes[span.Color] = brush = Brush(span.Color);
                            text.SetForegroundBrush(brush, span.Start, span.Length);
                        }
                    }
                    drawingContext.PushClip(new RectangleGeometry(new Rect(_textBox.Margin.Left, 0,
                        Math.Max(0, ActualWidth - _textBox.Margin.Left), ActualHeight)));
                    drawingContext.DrawText(text, new Point(_textBox.Margin.Left + rect.Left, rect.Top));
                    drawingContext.Pop();
                }
            }

            private void DrawText(DrawingContext context, string value, Brush brush, Typeface typeface, double pixelsPerDip,
                double rightOrCenter, double top, bool centered)
            {
                if (string.IsNullOrEmpty(value)) return;
                var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, _textBox.FontSize, brush, pixelsPerDip);
                context.DrawText(text, new Point(rightOrCenter - (centered ? text.Width / 2 : text.Width), top));
            }

            private static Brush Brush(string color)
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(color);
                brush.Freeze();
                return brush;
            }
        }
    }
}
