using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Kronos.ViewModels;

namespace Kronos.Views;

public partial class AddGameDialog : Window
{
    public AddGameDialog()
    {
        InitializeComponent();
    }

    public AddGameDialog(AddGameDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}