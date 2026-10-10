using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kronos.ViewModels;

namespace Kronos.Views;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
    }

    public ChangelogWindow(ChangelogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}