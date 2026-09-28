using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kronos.UserControls;

public class EasyContentDialog : ContentDialog
{
    public EasyContentDialog(XamlRoot xamlRoot) : base()
    {
        XamlRoot = xamlRoot;
        RequestedTheme = Settings.Instance.AppTheme;
    }
}
