using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using HotKeyCommandApp.Models;
using System;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public enum BrowseFileType
    {
        File,
        Folder
    }

    public interface ICreationStep : IDisposable
    {
        string Title { get; }
        string Prompt { get; }
        BindableReactiveProperty<string> InputText { get; }
        bool IsTextInputVisible { get; }
        BindableReactiveProperty<bool> IsListBoxVisible { get; }
        bool ShowBrowseButton { get; }
        BrowseFileType BrowseFileType { get; }
        bool ShowBehaviorOptions { get; }
        bool ShowInPageIndicator { get; }
        bool IsComplete { get; }

        ObservableCollection<CommandEntry> DisplayCommands { get; }
        BindableReactiveProperty<CommandEntry?> SelectedItem { get; }
        BindableReactiveProperty<bool> IsSearching { get; }

        event Action<string>? RequestControlFocus;

        /// <summary>Enterキー押下時に、通常の確定（次へ）を中断して独自の処理を行うべきか</summary>
        bool ShouldInterceptCommit();
        /// <summary>ShouldInterceptCommitがtrueの時に実行される独自処理</summary>
        void InterceptCommit();

        void Initialize(IDictionary<string, CreationStepResultBase> results);
        CreationStepResultBase? OnCommitted();
        Task PerformSearchAsync(string query);
        bool HandleHorizontalNavigation(int direction);
    }
}
