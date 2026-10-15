using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// Deliberately in the toolkit's namespace, so AcknowledgementsPage.xaml and GitHubUpdater compile
// unchanged. See the remarks on MarkdownTextBlock.
namespace CommunityToolkit.WinUI.Controls;

/// <summary>
/// Stand-in for the Toolkit Labs MarkdownTextBlock, which has no Uno build.
/// </summary>
/// <remarks>
/// CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock ships only net*-windows assets, so it cannot
/// be restored for net10.0-desktop. Phase 1 of the Uno migration needs the XAML to compile unchanged, so
/// this keeps the same type name, namespace and the two properties Kronos sets, and shows the markdown
/// source as plain, selectable text.
///
/// Temporary. Replacing it with a real markdown view, or settling on plain text and dropping the
/// pretence, is phase 2 of spike/UNO-MIGRATION-PLAN.md. Three call sites: AcknowledgementsPage.xaml and
/// the two update dialogs in GitHubUpdater.
/// </remarks>
public sealed partial class MarkdownTextBlock : ContentControl
{
    readonly TextBlock _textBlock = new()
    {
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
    };

    public MarkdownTextBlock()
    {
        Content = _textBlock;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(string.Empty, (d, e) => ((MarkdownTextBlock)d)._textBlock.Text = e.NewValue as string ?? string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(
        nameof(Config),
        typeof(MarkdownConfig),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(null));

    /// <summary>Accepted for source compatibility; plain text has nothing to configure.</summary>
    public MarkdownConfig? Config
    {
        get => (MarkdownConfig?)GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }
}

/// <summary>Stand-in for the Toolkit Labs MarkdownConfig. See <see cref="MarkdownTextBlock"/>.</summary>
public sealed class MarkdownConfig
{
}
