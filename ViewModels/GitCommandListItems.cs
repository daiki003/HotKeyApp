using System.Collections;
using System.ComponentModel;

namespace HotKeyCommandApp.ViewModels
{
    public sealed class BranchListItem : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private string _trackingStatusText = "集計中...";
        private string _trackingStatusToolTip = "取得済みのリモート情報からコミット数を集計しています。";

        public BranchListItem(string name, bool isCurrent)
        {
            Name = name;
            _isCurrent = isCurrent;
        }

        public string Name { get; }
        public bool IsCurrent => _isCurrent;
        public string DisplayName => _isCurrent ? $"* {Name}" : $"  {Name}";
        public string PrimaryActionText => _isCurrent ? "プル" : "移動";
        public string TrackingStatusText => _trackingStatusText;
        public string TrackingStatusToolTip => _trackingStatusToolTip;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetTrackingStatus(string text, string toolTip)
        {
            if (_trackingStatusText != text)
            {
                _trackingStatusText = text;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackingStatusText)));
            }
            if (_trackingStatusToolTip != toolTip)
            {
                _trackingStatusToolTip = toolTip;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackingStatusToolTip)));
            }
        }

        public void SetCurrent(bool isCurrent)
        {
            if (_isCurrent == isCurrent) return;
            _isCurrent = isCurrent;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrimaryActionText)));
        }
    }

    public sealed class StashListItem
    {
        public StashListItem(string name, string message, string commitId)
        {
            Name = name;
            Message = message;
            CommitId = commitId;
        }

        public string Name { get; }
        public string Message { get; }
        public string CommitId { get; }
        public string DisplayName => $"{Name}: {Message}";
        public string PrimaryActionText => "適用";
        public string TrackingStatusText => string.Empty;
        public string TrackingStatusToolTip => string.Empty;
    }

    internal sealed class BranchSearchComparer : IComparer
    {
        private readonly Func<string> _getSearchText;

        public BranchSearchComparer(Func<string> getSearchText) => _getSearchText = getSearchText;

        public int Compare(object? x, object? y)
        {
            if (x is not BranchListItem left || y is not BranchListItem right) return 0;
            string query = _getSearchText();
            if (query.Length > 0)
            {
                int leftPosition = left.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                int rightPosition = right.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                int leftRank = string.Equals(left.Name, query, StringComparison.OrdinalIgnoreCase) ? 0 : leftPosition == 0 ? 1 : 2;
                int rightRank = string.Equals(right.Name, query, StringComparison.OrdinalIgnoreCase) ? 0 : rightPosition == 0 ? 1 : 2;
                int rank = leftRank.CompareTo(rightRank);
                if (rank != 0) return rank;
                int position = leftPosition.CompareTo(rightPosition);
                if (position != 0) return position;
                int length = left.Name.Length.CompareTo(right.Name.Length);
                if (length != 0) return length;
            }
            int name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return name != 0 ? name : StringComparer.Ordinal.Compare(left.Name, right.Name);
        }
    }

}
