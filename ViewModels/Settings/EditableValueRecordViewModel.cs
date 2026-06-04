using System;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class EditableValueRecordViewModel : ViewModelBase
    {
        private readonly Func<string> _getValueText;
        private readonly Func<string, bool> _tryApplyValueText;
        private string _editingText;
        private string _originalEditingText;
        private bool _isEditing;

        public EditableValueRecordViewModel(string label, Func<string> getValueText, Func<string, bool> tryApplyValueText)
        {
            Label = label;
            _getValueText = getValueText;
            _tryApplyValueText = tryApplyValueText;
            _editingText = getValueText();
            _originalEditingText = _editingText;
        }

        public string Label { get; }

        public string DisplayValue => _getValueText();

        public string EditingText
        {
            get => _editingText;
            set
            {
                if (_editingText == value)
                {
                    return;
                }

                _editingText = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (_isEditing == value)
                {
                    return;
                }

                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public void BeginEdit()
        {
            _originalEditingText = _getValueText();
            EditingText = _originalEditingText;
            IsEditing = true;
        }

        public bool CommitEdit()
        {
            if (!_tryApplyValueText(EditingText))
            {
                return false;
            }

            IsEditing = false;
            RefreshFromSource();
            return true;
        }

        public void CancelEdit()
        {
            EditingText = _originalEditingText;
            IsEditing = false;
            RefreshFromSource();
        }

        public void RefreshFromSource()
        {
            string current = _getValueText();
            _originalEditingText = current;
            if (!IsEditing)
            {
                _editingText = current;
                OnPropertyChanged(nameof(EditingText));
            }

            OnPropertyChanged(nameof(DisplayValue));
        }
    }
}
