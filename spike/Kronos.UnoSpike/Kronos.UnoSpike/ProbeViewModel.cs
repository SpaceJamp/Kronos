using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kronos.UnoSpike;

/// <summary>
/// Stands in for the view models Kronos binds to.
/// </summary>
/// <remarks>
/// Deliberately shaped like the real ones: an <see cref="ObservableObject"/> with
/// <c>[ObservableProperty]</c> fields and <c>[RelayCommand]</c> methods, plus a nested
/// <c>TranslationProperties</c> object reached through <c>x:Bind</c>.
///
/// That last part is the one worth testing. Kronos does not bind to a resource string directly - it
/// binds to a generated property that calls <c>ResourceHelper.GetString</c> - on every page. If Uno's
/// x:Bind compiler cannot see through two levels of property on a nested object, the migration
/// rewrites every binding in the app.
/// </remarks>
public partial class ProbeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsEnabledFlag { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scanning 12 of 340";

    [ObservableProperty]
    public partial double Progress { get; set; } = 42.0;

    /// <summary>A list, for the DataTemplate and x:DataType checks.</summary>
    public List<string> Items { get; } = new() { "nvngx.dll", "nvngx_dlss.dll", "nvngx_dlss2.dll" };

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [RelayCommand]
    void DoSomething()
    {
    }

    /// <summary>The nested-translation shape, matching Kronos's *TranslationProperties.</summary>
    public ProbeTranslationProperties TranslationProperties { get; } = new();

    /// <summary>Counts, like MassUpgradePlanner.Summarise's caller.</summary>
    public int UpgradeCount => 7;
}

/// <summary>Mirrors Kronos's generated translation property classes.</summary>
public partial class ProbeTranslationProperties
{
    public string PageTitleText => "Kronos";
    public string RefreshText => "Refresh";
    public string FilterText => "Filter";
    public string ThemeModeText => "Theme mode";
}
