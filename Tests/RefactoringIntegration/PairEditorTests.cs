using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Views.Controls;
using static TestSupport;

internal static class PairEditorTests
{
    public static async Task RunAsync()
    {
        foreach (var factory in new Func<IPairEntryEditable>[]
        {
            () => new GitAliasEntry(), () => new RepositoryNameMapping(),
            () => new ConstantEntry(), () => new SelectTemplate()
        })
        {
            TestOrdering(factory);
            await TestEditorAsync(factory);
        }
        await TestTabsAsync();
    }

    private static void TestOrdering(Func<IPairEntryEditable> factory)
    {
        var root = factory();
        root.SortOrder = 2;
        var nested = factory();
        nested.ParentFolderId = "inner";
        var orphan = factory();
        orphan.ParentFolderId = "missing";
        var records = new ArrayList { root, orphan, nested };
        var folders = new ArrayList
        {
            new PairEntryFolder { Id = "outer", SortOrder = 0 },
            new PairEntryFolder { Id = "inner", ParentFolderId = "outer", SortOrder = 0 }
        };
        var visible = PairEntryListEditor.ReadVisibleItems(records, folders);
        string name = root.GetType().Name;
        Check(visible.SequenceEqual(new[] { nested, root, orphan }) && folders.Count == 0 &&
            visible.All(item => item.ParentFolderId == string.Empty), name + ": 旧フォルダ構造と参照先のない行を維持して平坦化");
        Check(PairEntryListEditor.TryMove(visible, nested, 2) &&
            visible.SequenceEqual(new[] { root, orphan, nested }) && visible.Select(item => item.SortOrder).SequenceEqual(new[] { 0, 1, 2 }),
            name + ": 移動方向にかかわらず表示順と保存順を同期");
        Check(!PairEntryListEditor.TryMove(visible, root, -1) && visible[0] == root, name + ": 先頭から上には移動しない");
    }

    private static async Task TestEditorAsync(Func<IPairEntryEditable> factory)
    {
        var first = factory();
        first.FirstValue = "first";
        first.SecondValue = "value";
        first.SortOrder = 1;
        var second = factory();
        second.FirstValue = "second";
        second.SortOrder = 0;
        var records = new ArrayList { first, second };
        var editor = new AliasPairEditorControl
        {
            ItemsSource = records,
            AddItemCommand = new RelayCommand<object>(_ => records.Add(factory())),
            ShowColumnHeaders = false
        };
        var window = new Window { Width = 600, Height = 350, Content = editor, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Activate();
            await LayoutAsync(window);
            string name = first.GetType().Name;
            Check(editor.VisibleItems.SequenceEqual(new[] { second, first }), name + ": 共通画面の初期表示順");
            var container = GetRowContainer(editor, second);
            double normalHeight = container.ActualHeight;
            SavePreview(editor, $"pair-{name}-display.png");
            var button = FindChild<Button>(container, element => element.Name == "RowButton");
            PressKey(button, Key.F2);
            await LayoutAsync(window);
            double editingHeight = GetRowContainer(editor, second).ActualHeight;
            SavePreview(editor, $"pair-{name}-edit.png");
            Check(ReferenceEquals(editor.CurrentEditingItem, second) && normalHeight > 0 &&
                Math.Abs(editingHeight - normalHeight) < 0.1,
                $"{name}: F2で行の高さを保って編集開始 (編集中={ReferenceEquals(editor.CurrentEditingItem, second)}, 通常={normalHeight}, 編集={editingHeight})");
            Invoke(editor, "ExitEditMode", second, false);
            Invoke(editor, "MoveItem", second, false, false, false);
            await LayoutAsync(window);
            Check(editor.VisibleItems.SequenceEqual(new[] { first, second }) && first.SortOrder == 0 && second.SortOrder == 1,
                name + ": 画面からの並び替え");
            PressKey(FindChild<Button>(GetRowContainer(editor, second), element => element.Name == "RowButton"), Key.Delete);
            await LayoutAsync(window);
            Check(records.Count == 1 && editor.VisibleItems.Single() == first, name + ": Deleteで対象行を削除");
            editor.AddNewItem();
            await LayoutAsync(window);
            Check(records.Count == 2 && ReferenceEquals(editor.CurrentEditingItem, records[1]) && editor.VisibleItems[1].SortOrder == 1,
                name + ": 追加した行を末尾で編集");
        }
        finally
        {
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        }
    }

    private static async Task TestTabsAsync()
    {
        var first = new GitAliasTab { Name = "first", Aliases = new() { new GitAliasEntry { Alias = "item" } } };
        var second = new GitAliasTab { Name = "second" };
        var host = new GitAliasTabHost(new GitSettings { AliasTabs = new() { first, second } });
        var entry = first.Aliases[0];
        Check(!PairEntryListEditor.TryMoveToAdjacentTab(host, entry, false, out _), "最初のタブから左へ行を移動しない");
        Check(PairEntryListEditor.TryMoveToAdjacentTab(host, entry, true, out var destination) && destination == second &&
            first.Aliases.Count == 0 && second.Aliases.Single() == entry && entry.SortOrder == 0, "隣接タブへ行を移動して両方の順序を更新");
        var session = new PairEditorTabEditSession();
        session.Start(first);
        first.Name = "  renamed  ";
        session.Start(second);
        Check(first.Name == "renamed" && !first.IsEditing && second.IsEditing, "別のタブを編集すると前の名前を確定");
        second.Name = "discard";
        session.End(commit: false);
        Check(second.Name == "second" && !second.IsEditing, "タブ名の編集をキャンセル");
        session.Start(second);
        second.Name = " ";
        session.End(commit: true);
        Check(second.Name == "second", "空のタブ名は元の名前を維持");

        var editor = new AliasPairEditorControl { TabHost = host };
        var window = new Window { Width = 600, Height = 350, Content = editor, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Activate();
            await LayoutAsync(window);
            var tabs = (ListBox)editor.FindName("TabsListBox");
            double height = ((ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(first)).ActualHeight;
            SavePreview(editor, "pair-tabs-display.png");
            Invoke(editor, "StartTabRename", first);
            await LayoutAsync(window);
            double editingHeight = ((ListBoxItem)tabs.ItemContainerGenerator.ContainerFromItem(first)).ActualHeight;
            SavePreview(editor, "pair-tabs-edit.png");
            Check(first.IsEditing && Math.Abs(editingHeight - height) < 0.1,
                $"タブ名の編集開始前後で高さを維持 (通常={height}, 編集={editingHeight})");
            first.Name = "discard";
            Invoke(editor, "EndTabRename", false);
            Check(first.Name == "renamed" && !first.IsEditing, "画面のタブ名編集をキャンセル");
        }
        finally { window.Close(); }
    }

    private static async Task LayoutAsync(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        window.UpdateLayout();
    }

    private static FrameworkElement GetRowContainer(AliasPairEditorControl editor, IPairEntryEditable item) =>
        (FrameworkElement)((ItemsControl)editor.FindName("PairItemsControl")).ItemContainerGenerator.ContainerFromItem(item);

    private static void PressKey(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static T FindChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match)) return match;
            try { return FindChild(child, predicate); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("コントロールが見つかりません。");
    }
}
