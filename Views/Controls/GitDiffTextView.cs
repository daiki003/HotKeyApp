using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views.Controls
{
    public sealed class GitDiffTextView : Grid
    {
        private const double LineLinkColumnWidth = 24;
        private const double DefaultGutterWidth = 144;
        private readonly TextBox _textBox;
        private readonly DiffBackground _background;
        private readonly Canvas _lineLinks;
        private readonly Dictionary<int, Button> _lineLinkButtons = new();
        private readonly Style _lineLinkStyle = CreateLineLinkStyle();
        private bool _canOpenLines;
        private bool _rowsDirty;
        private bool _updatePending;
        private CancellationTokenSource _highlightCts;
        private string _fileKey;
        private HwndSource _source;
        public event EventHandler SelectionChanged;
        public event Action<int> LineOpenRequested;

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
            _lineLinks = new Canvas { Width = LineLinkColumnWidth, HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            Children.Add(_background);
            Children.Add(_textBox);
            Children.Add(_lineLinks);
            _textBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RequestRowsUpdate()));
            _textBox.SizeChanged += (_, _) => RequestRowsUpdate();
            _textBox.Loaded += (_, _) => RequestRowsUpdate();
            _textBox.IsVisibleChanged += (_, _) => RequestRowsUpdate();
            _textBox.LayoutUpdated += (_, _) => { if (_rowsDirty) QueueRowsUpdate(); };
            _textBox.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
            PreviewMouseWheel += OnPreviewMouseWheel;
            Loaded += (_, _) =>
            {
                _source = PresentationSource.FromVisual(this) as HwndSource;
                _source?.AddHook(OnWindowMessage);
            };
            Unloaded += (_, _) => { _source?.RemoveHook(OnWindowMessage); _source = null; };
        }

        public void SetLines(IReadOnlyList<GitDiffLine> lines, GitDiffFile file = null, string repositoryPath = null)
        {
            bool preservePosition = file != null && file.Key == _fileKey;
            int selectionStart = _textBox.SelectionStart, selectionLength = _textBox.SelectionLength;
            double verticalOffset = _textBox.VerticalOffset, horizontalOffset = _textBox.HorizontalOffset;
            _fileKey = file?.Key;
            _canOpenLines = file != null && repositoryPath != null && System.IO.File.Exists(System.IO.Path.Combine(repositoryPath, file.Path));
            CancelHighlighting();
            _background.Lines = lines;
            _background.Syntax = Array.Empty<IReadOnlyList<GitDiffSyntaxSpan>>();
            _background.Changes = Array.Empty<IReadOnlyList<GitDiffChangeSpan>>();
            _background.ClearRows();
            ClearLineLinks();
            UpdateGutterWidth();
            _textBox.Text = string.Join("\r\n", lines.Select(line => line.Text));
            if (preservePosition)
            {
                int start = Math.Min(selectionStart, _textBox.Text.Length);
                _textBox.Select(start, Math.Min(selectionLength, _textBox.Text.Length - start));
                _textBox.ScrollToVerticalOffset(verticalOffset);
                _textBox.ScrollToHorizontalOffset(horizontalOffset);
            }
            else _textBox.ScrollToHome();
            RequestRowsUpdate();
            if (lines.Count > 0 && file != null)
            {
                _highlightCts = new CancellationTokenSource();
                _ = HighlightChangesAsync(lines, _highlightCts.Token);
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

        private async Task HighlightChangesAsync(IReadOnlyList<GitDiffLine> lines, CancellationToken token)
        {
            try
            {
                var changes = await Task.Run(() => GitDiffIntralineHighlighter.Highlight(lines, token), token);
                if (token.IsCancellationRequested) return;
                _background.Changes = changes;
                _background.InvalidateVisual();
            }
            catch (OperationCanceledException) { }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
            _textBox.ScrollToHorizontalOffset(_textBox.HorizontalOffset - e.Delta / 120.0 * 48);
            e.Handled = true;
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int mouseHorizontalWheel = 0x020E;
            if (message != mouseHorizontalWheel || !IsVisible) return IntPtr.Zero;
            long position = lParam.ToInt64();
            var point = PointFromScreen(new Point(unchecked((short)(position & 0xffff)), unchecked((short)((position >> 16) & 0xffff))));
            if (!new Rect(RenderSize).Contains(point)) return IntPtr.Zero;
            int delta = unchecked((short)(((long)wParam >> 16) & 0xffff));
            _textBox.ScrollToHorizontalOffset(_textBox.HorizontalOffset - delta / 120.0 * 48);
            handled = true;
            return IntPtr.Zero;
        }

        private int? GetLineNumber(int index)
        {
            if (index < 0 || index >= _background.Lines.Count) return null;
            var line = _background.Lines[index];
            if (line.Kind is GitDiffLineKind.Hunk or GitDiffLineKind.Notice) return null;
            if (int.TryParse(line.NewNumber, out int number)) return number;
            for (int i = index + 1; i < _background.Lines.Count; i++)
            {
                if (_background.Lines[i].Kind == GitDiffLineKind.Hunk) break;
                if (int.TryParse(_background.Lines[i].NewNumber, out number)) return number;
            }
            for (int i = index - 1; i >= 0; i--)
            {
                if (_background.Lines[i].Kind == GitDiffLineKind.Hunk) break;
                if (int.TryParse(_background.Lines[i].NewNumber, out number)) return number + 1;
            }
            return int.TryParse(line.OldNumber, out number) ? Math.Max(1, number) : null;
        }

        public void SetFontSize(double fontSize)
        {
            _textBox.SetCurrentValue(Control.FontSizeProperty, fontSize);
            UpdateGutterWidth();
            _background.ClearRows();
            ClearLineLinks();
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
            _textBox.Margin = new Thickness(LineLinkColumnWidth + numberWidth * 2 + markerWidth, 0, 0, 0);
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
                if (_rowsDirty)
                {
                    _rowsDirty = !_background.UpdateRows();
                    if (!_rowsDirty) UpdateLineLinks();
                }
            });
        }

        private void ClearLineLinks()
        {
            _lineLinks.Children.Clear();
            _lineLinkButtons.Clear();
        }

        private void UpdateLineLinks()
        {
            var visible = new HashSet<int>();
            if (_canOpenLines)
            {
                foreach (var row in _background.VisibleRows)
                {
                    int? line = GetLineNumber(row.Index);
                    if (!line.HasValue) continue;
                    visible.Add(row.Index);
                    if (!_lineLinkButtons.TryGetValue(row.Index, out var button))
                    {
                        button = new Button
                        {
                            Name = "OpenDiffLineButton", Content = "↗", Style = _lineLinkStyle,
                            Width = LineLinkColumnWidth, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                            Focusable = false, Cursor = Cursors.Hand, FontSize = 11, Foreground = Brushes.LightSkyBlue
                        };
                        button.Click += (_, args) => { args.Handled = true; LineOpenRequested?.Invoke((int)button.Tag); };
                        _lineLinkButtons.Add(row.Index, button);
                        _lineLinks.Children.Add(button);
                    }
                    button.Tag = line.Value;
                    button.ToolTip = $"VS Code の {line.Value} 行目を開く";
                    System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
                    button.Height = row.Bounds.Height;
                    Canvas.SetTop(button, row.Bounds.Top);
                }
            }
            foreach (int index in _lineLinkButtons.Keys.Where(index => !visible.Contains(index)).ToArray())
            {
                _lineLinks.Children.Remove(_lineLinkButtons[index]);
                _lineLinkButtons.Remove(index);
            }
        }

        private static Style CreateLineLinkStyle()
        {
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Button.Background)) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(38, 79, 120))));
            style.Triggers.Add(hover);
            return style;
        }

        private sealed class DiffBackground : FrameworkElement
        {
            private static readonly Brush DefaultBackground = Brush("#1E1E1E");
            private static readonly Brush AddedBackground = Brush("#183D2A");
            private static readonly Brush DeletedBackground = Brush("#4B2428");
            private static readonly Brush AddedChangeBackground = Brush("#2D6D43");
            private static readonly Brush DeletedChangeBackground = Brush("#873B40");
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
            public IEnumerable<(int Index, Rect Bounds)> VisibleRows => _rows.Select(row => (row.Index, row.Bounds));
            public IReadOnlyList<GitDiffSyntaxSpan>[] Syntax { get; set; } = Array.Empty<IReadOnlyList<GitDiffSyntaxSpan>>();
            public IReadOnlyList<GitDiffChangeSpan>[] Changes { get; set; } = Array.Empty<IReadOnlyList<GitDiffChangeSpan>>();

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
                    DrawText(drawingContext, line.OldNumber, NumberForeground, typeface, pixelsPerDip, LineLinkColumnWidth + NumberColumnWidth - 9, rect.Top, false);
                    DrawText(drawingContext, line.NewNumber, NumberForeground, typeface, pixelsPerDip, LineLinkColumnWidth + NumberColumnWidth * 2 - 9, rect.Top, false);
                    DrawText(drawingContext, line.Marker, MarkerForeground, typeface, pixelsPerDip, LineLinkColumnWidth + NumberColumnWidth * 2 + MarkerColumnWidth / 2, rect.Top, true);
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
                    var origin = new Point(_textBox.Margin.Left + rect.Left, rect.Top);
                    if (row.Index < Changes.Length)
                    {
                        var changeBrush = line.Kind == GitDiffLineKind.Added ? AddedChangeBackground : DeletedChangeBackground;
                        foreach (var span in Changes[row.Index])
                            drawingContext.DrawGeometry(changeBrush, null, text.BuildHighlightGeometry(origin, span.Start, span.Length));
                    }
                    drawingContext.DrawText(text, origin);
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
