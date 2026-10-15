using System;
using Markdig;
using Markdig.Syntax;
using Block = Markdig.Syntax.Block;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Kronos.UserControls;

/// <summary>
/// Shows markdown: release notes in the update dialogs, and the acknowledgements' notes.
/// </summary>
/// <remarks>
/// Replaces the Toolkit Labs MarkdownTextBlock, which ships only Windows App SDK builds and so cannot run
/// on Uno. Markdig parses; the rendering is built here from plain TextBlocks with inlines (Run, Bold,
/// Italic, Hyperlink), which Uno implements on every desktop platform. RichTextBlock would be the obvious
/// choice and is deliberately not used: Uno does not implement it on Skia.
///
/// It covers what the two sources actually contain - headings, paragraphs, bulleted and numbered lists,
/// emphasis, inline and block code, links and quotes. Anything else (tables, raw HTML) falls back to its
/// source text, so nothing is silently dropped.
/// </remarks>
public sealed partial class MarkdownView : ContentControl
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .Build();

    static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, Courier New, monospace");

    public MarkdownView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        IsTabStop = false;
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(string.Empty, (d, e) => ((MarkdownView)d).Render(e.NewValue as string)));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    void Render(string? markdown)
    {
        var panel = new StackPanel { Spacing = 8 };

        if (string.IsNullOrEmpty(markdown) == false)
        {
            var document = Markdown.Parse(markdown, Pipeline);
            AddBlocks(panel, document, markdown);
        }

        Content = panel;
    }

    static void AddBlocks(Panel panel, ContainerBlock container, string source)
    {
        foreach (var block in container)
        {
            if (CreateBlock(block, source) is UIElement element)
            {
                panel.Children.Add(element);
            }
        }
    }

    static UIElement? CreateBlock(Block block, string source)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var textBlock = NewTextBlock();
                textBlock.FontWeight = FontWeights.SemiBold;
                textBlock.FontSize = heading.Level switch
                {
                    1 => 24,
                    2 => 20,
                    3 => 17,
                    _ => 15,
                };
                AddInlines(textBlock.Inlines, heading.Inline);
                return textBlock;
            }

            case ParagraphBlock paragraph:
            {
                var textBlock = NewTextBlock();
                AddInlines(textBlock.Inlines, paragraph.Inline);
                return textBlock;
            }

            case ListBlock list:
                return CreateList(list, source);

            case QuoteBlock quote:
            {
                var inner = new StackPanel { Spacing = 8 };
                AddBlocks(inner, quote, source);
                return new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
                    Padding = new Thickness(12, 0, 0, 0),
                    Child = inner,
                };
            }

            // FencedCodeBlock derives from CodeBlock, so this covers both.
            case CodeBlock code:
                return new Border
                {
                    Background = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Child = new TextBlock
                    {
                        Text = code.Lines.ToString(),
                        FontFamily = Monospace,
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.Wrap,
                    },
                };

            case ThematicBreakBlock:
                return new Rectangle
                {
                    Height = 1,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Fill = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
                };

            case LinkReferenceDefinitionGroup:
            case BlankLineBlock:
                return null;

            default:
                // Tables, raw HTML and anything else: the source text, rather than nothing.
                var fallback = NewTextBlock();
                fallback.Text = source.Substring(block.Span.Start, Math.Max(0, block.Span.Length));
                return fallback;
        }
    }

    static UIElement CreateList(ListBlock list, string source)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;

        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
            {
                continue;
            }

            var marker = new TextBlock
            {
                Text = list.IsOrdered ? $"{number++}{list.OrderedDelimiter}" : "•",
                Margin = new Thickness(0, 0, 8, 0),
            };

            var content = new StackPanel { Spacing = 4 };
            AddBlocks(content, listItem, source);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);

            panel.Children.Add(row);
        }

        panel.Margin = new Thickness(8, 0, 0, 0);
        return panel;
    }

    static TextBlock NewTextBlock()
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
    }

    static void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            if (CreateInline(inline) is Microsoft.UI.Xaml.Documents.Inline created)
            {
                target.Add(created);
            }
        }
    }

    static Microsoft.UI.Xaml.Documents.Inline? CreateInline(Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run { Text = literal.Content.ToString() };

            case CodeInline code:
                return new Run { Text = code.Content, FontFamily = Monospace };

            case LineBreakInline lineBreak:
                // A soft break is a space in rendered markdown, not a new line.
                return lineBreak.IsHard ? new LineBreak() : new Run { Text = " " };

            case HtmlEntityInline entity:
                return new Run { Text = entity.Transcoded.ToString() };

            case AutolinkInline autolink:
            {
                var hyperlink = NewHyperlink(autolink.Url);
                hyperlink.Inlines.Add(new Run { Text = autolink.Url });
                return hyperlink;
            }

            case LinkInline { IsImage: true } image:
            {
                // No images: they would need fetching, and both sources only use them decoratively.
                var span = new Span();
                AddInlines(span.Inlines, image);
                return span;
            }

            case LinkInline link:
            {
                var hyperlink = NewHyperlink(link.Url);
                AddInlines(hyperlink.Inlines, link);
                return hyperlink;
            }

            case EmphasisInline emphasis:
            {
                Span span = emphasis.DelimiterChar is '*' or '_'
                    ? emphasis.DelimiterCount >= 2 ? new Bold() : new Italic()
                    : new Span();
                AddInlines(span.Inlines, emphasis);
                return span;
            }

            case HtmlInline html:
                return new Run { Text = html.Tag };

            case ContainerInline container:
            {
                var span = new Span();
                AddInlines(span.Inlines, container);
                return span;
            }

            default:
                return null;
        }
    }

    static Hyperlink NewHyperlink(string? url)
    {
        var hyperlink = new Hyperlink();

        // Only absolute http(s) links navigate. A relative link has nothing to resolve against here, and
        // anything else - file:, javascript:, a custom scheme - is not something a release note should open.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            hyperlink.NavigateUri = uri;
        }

        return hyperlink;
    }
}
