using Kronos.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kronos.UserControls;

class ListGridViewTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ListViewTemplate { get; set; }
    public DataTemplate? GridVeiwTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is GameGridPageModel gameGridPageModel)
        {
            if (gameGridPageModel.GameGridViewType == GameGridViewType.GridView)
            {
                return GridVeiwTemplate;
            }
            else
            {
                return ListViewTemplate;
            }
        }

        return null;
    }
}
