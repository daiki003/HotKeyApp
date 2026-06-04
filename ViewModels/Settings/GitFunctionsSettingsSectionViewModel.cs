using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class GitFunctionsSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly GitSettings _source;
        private bool _isUpdatingSelection;
        private ObservableCollection<GitFunctionEntry> _functions;
        private GitFunctionEntry? _selectedFunction;
        private string _editingFunctionName = string.Empty;
        private string _editingFunctionDescription = string.Empty;
        private string _editingFunctionCommands = string.Empty;

        public GitFunctionsSettingsSectionViewModel(GitSettings settings)
            : base("Functions", "関数")
        {
            _source = settings;
            _functions = new ObservableCollection<GitFunctionEntry>();
            AddFunctionCommand = new RelayCommand<object>(_ => AddFunction());
            DeleteFunctionCommand = new RelayCommand<object>(_ => DeleteSelectedFunction(), _ => SelectedFunction != null);
            Reset();
        }

        public ObservableCollection<GitFunctionEntry> Functions
        {
            get => _functions;
            private set { _functions = value; OnPropertyChanged(); }
        }

        public GitFunctionEntry? SelectedFunction
        {
            get => _selectedFunction;
            set
            {
                if (ReferenceEquals(_selectedFunction, value))
                {
                    return;
                }

                _selectedFunction = value;
                OnPropertyChanged();
                UpdateEditingFieldsFromSelectedFunction();
            }
        }

        public string EditingFunctionName
        {
            get => _editingFunctionName;
            set
            {
                _editingFunctionName = value;
                OnPropertyChanged();
                UpdateSelectedFunction(entry => entry.Name = value);
            }
        }

        public string EditingFunctionDescription
        {
            get => _editingFunctionDescription;
            set
            {
                _editingFunctionDescription = value;
                OnPropertyChanged();
                UpdateSelectedFunction(entry => entry.Description = value);
            }
        }

        public string EditingFunctionCommands
        {
            get => _editingFunctionCommands;
            set
            {
                _editingFunctionCommands = value;
                OnPropertyChanged();
                UpdateSelectedFunction(entry =>
                {
                    entry.Commands = value
                        .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(cmd => cmd.Trim())
                        .Where(cmd => !string.IsNullOrEmpty(cmd))
                        .ToList();
                });
            }
        }

        public ICommand AddFunctionCommand { get; }
        public ICommand DeleteFunctionCommand { get; }

        public override void Save()
        {
            _source.Functions = Functions.ToList();
        }

        public override void Reset()
        {
            Functions = new ObservableCollection<GitFunctionEntry>(_source.Functions);
            SelectedFunction = Functions.FirstOrDefault();
        }

        private void AddFunction()
        {
            var newEntry = new GitFunctionEntry
            {
                Name = "new_func",
                Description = string.Empty,
                Commands = new System.Collections.Generic.List<string> { "echo test" }
            };

            Functions.Add(newEntry);
            SelectedFunction = newEntry;
        }

        private void DeleteSelectedFunction()
        {
            if (SelectedFunction == null)
            {
                return;
            }

            GitFunctionEntry removed = SelectedFunction;
            int index = Functions.IndexOf(removed);
            Functions.Remove(removed);
            SelectedFunction = index >= 0 && index < Functions.Count
                ? Functions[index]
                : Functions.LastOrDefault();
        }

        private void UpdateEditingFieldsFromSelectedFunction()
        {
            _isUpdatingSelection = true;
            if (SelectedFunction == null)
            {
                EditingFunctionName = string.Empty;
                EditingFunctionDescription = string.Empty;
                EditingFunctionCommands = string.Empty;
            }
            else
            {
                EditingFunctionName = SelectedFunction.Name;
                EditingFunctionDescription = SelectedFunction.Description;
                EditingFunctionCommands = string.Join(Environment.NewLine, SelectedFunction.Commands ?? new System.Collections.Generic.List<string>());
            }

            _isUpdatingSelection = false;
        }

        private void UpdateSelectedFunction(Action<GitFunctionEntry> updateAction)
        {
            if (_isUpdatingSelection || SelectedFunction == null)
            {
                return;
            }

            updateAction(SelectedFunction);
        }
    }
}
