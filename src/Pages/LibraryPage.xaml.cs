using System;
using Kronos.Data;
using Kronos.Helpers;
using Kronos.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kronos.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LibraryPage : Page
{
    public static string PageTag { get; } = "PageTag_Library";

    public LibraryPageModel ViewModel { get; private set; }

    public LibraryPage()
    {
        this.InitializeComponent();
        ViewModel = new LibraryPageModel(this);
    }


    void MainGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateItemWidth(e.NewSize.Width);
    }

    /// <summary>
    /// The panel only exists once the GridView has been templated, which is after its first SizeChanged.
    /// Without this the first width was never applied and the tiles sized themselves to their text.
    /// </summary>
    void ItemsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateItemWidth(MainGridView.ActualWidth);
    }

    void UpdateItemWidth(double width)
    {
        // The panel is a WrapPanel (see LibraryPage.xaml). The hard cast to ItemsWrapGrid that was here
        // threw a NullReferenceException on every resize under Uno, which does not implement it.
        if (width <= 0 || MainGridView.ItemsPanelRoot is not WrapPanel wrapPanel)
        {
            return;
        }

        // via: https://stackoverflow.com/a/41141249
        var columns = Math.Ceiling(width / 400);
        wrapPanel.ItemWidth = (float)((width / columns) - 1);
    }

    private void MainGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DLLRecord dllRecord)
        {
            var dialog = new EasyContentDialog(XamlRoot)
            {
                Title = DLLManager.Instance.GetAssetTypeName(dllRecord.AssetType),
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                Content = new DLLRecordInfoControl(dllRecord),
            };
            _ = dialog.ShowAsync();
        }
    }
}
