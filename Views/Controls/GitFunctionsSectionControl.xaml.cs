using System.Windows.Controls;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class GitFunctionsSectionControl : UserControl
    {
        public GitFunctionsSectionControl()
        {
            InitializeComponent();
        }

        public void FocusPrimaryInput()
        {
            FunctionNameTextBox.Focus();
            FunctionNameTextBox.SelectAll();
        }
    }
}
