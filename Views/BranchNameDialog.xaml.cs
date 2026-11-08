using System;
using System.Windows;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    public partial class BranchNameDialog : Window
    {
        public string BranchName => BranchNameBox.Text.Trim();
        private readonly bool _isStash;

        public BranchNameDialog(bool isStash = false, string currentBranchName = null)
        {
            InitializeComponent();
            _isStash = isStash;
            if (isStash)
            {
                Title = "スタッシュ作成";
                HeadingText.Text = "スタッシュ作成";
                NameLabel.Text = "メッセージ（省略可）";
            }
            else if (currentBranchName != null)
            {
                Title = "ブランチ名の変更";
                HeadingText.Text = "ブランチ名の変更";
                SubmitButton.Content = "リネーム";
                BranchNameBox.Text = currentBranchName;
            }
            Loaded += (s, e) => { BranchNameBox.Focus(); BranchNameBox.SelectAll(); };
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isStash && string.IsNullOrWhiteSpace(BranchName))
            {
                ErrorText.Text = "ブランチ名を入力してください。";
                BranchNameBox.Focus();
                return;
            }

            DialogResult = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
