using System.Windows;

namespace AMP.UI.Views;

/// <summary>主壳窗口（内容区由 ShellViewModel 的 CurrentViewModel 驱动）。</summary>
public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
    }
}
