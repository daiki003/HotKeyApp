using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using HotKeyCommandApp.Models;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public abstract class CreationStepBase : ICreationStep
    {
        protected DisposableBag _disposables = new();

        public abstract string Title { get; }
        public abstract string Prompt { get; }

        public virtual bool IsTextInputVisible => true;
        
        // UIバインディング用に BindableReactiveProperty に変更
        public BindableReactiveProperty<bool> IsListBoxVisible { get; protected set; } = new(false);

        public virtual bool ShowBrowseButton => false;
        public virtual BrowseFileType BrowseFileType => BrowseFileType.File;
        public virtual bool ShowBehaviorOptions => false;
        public virtual bool ShowInPageIndicator => true;

        public virtual bool IsComplete => true;

        public BindableReactiveProperty<string> InputText { get; } = new("");
        public ObservableCollection<CommandEntry> DisplayCommands { get; } = new();
        public BindableReactiveProperty<CommandEntry?> SelectedItem { get; } = new(null);
        public BindableReactiveProperty<bool> IsSearching { get; } = new(false);

        public event Action<string>? RequestControlFocus;

        protected void TriggerRequestControlFocus(string controlName) => RequestControlFocus?.Invoke(controlName);

        public virtual void Initialize(IDictionary<string, CreationStepResultBase> results) { }
        public virtual CreationStepResultBase? OnCommitted() => null;
        public virtual Task PerformSearchAsync(string query) => Task.CompletedTask;
        public virtual bool ShouldInterceptCommit() => false;
        public virtual void InterceptCommit() { }
        public virtual bool HandleHorizontalNavigation(int direction) => false;

        public virtual void Dispose() => _disposables.Dispose();
    }
}
