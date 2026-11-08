using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views.Controls
{
    public sealed class GitDiffTextView : Grid
    {
        private const double GutterWidth = 120;
        private readonly TextBox _textBox;
        private readonly DiffBackground _background;
        private bool _rowsDirty;
        private bool _updatePending;
        public event EventHandler SelectionChanged;

        public GitDiffTextView()
        {
            Background = Brushes.Transparent;
            _textBox = new TextBox
            {
                Margin = new Thickness(GutterWidth, 0, 0, 0),
                Padding = new Thickness(0, 2, 16, 2),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
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

        public void SetLines(IReadOnlyList<GitDiffLine> lines)
        {
            _background.Lines = lines;
            _background.ClearRows();
            _textBox.Text = string.Join("\r\n", lines.Select(line => line.Text));
            _textBox.ScrollToHome();
            RequestRowsUpdate();
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
            private readonly TextBox _textBox;
            private IReadOnlyList<VisibleRow> _rows = Array.Empty<VisibleRow>();

            private readonly record struct VisibleRow(GitDiffLine Line, Rect Bounds);

            public DiffBackground(TextBox textBox) => _textBox = textBox;
            public IReadOnlyList<GitDiffLine> Lines { get; set; } = Array.Empty<GitDiffLine>();

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
                        if (!rect.IsEmpty) rows.Add(new VisibleRow(Lines[i], rect));
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
                    DrawText(drawingContext, line.OldNumber, NumberForeground, typeface, pixelsPerDip, 39, rect.Top, false);
                    DrawText(drawingContext, line.NewNumber, NumberForeground, typeface, pixelsPerDip, 87, rect.Top, false);
                    DrawText(drawingContext, line.Marker, MarkerForeground, typeface, pixelsPerDip, 108, rect.Top, true);
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
