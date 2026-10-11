using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the games page XAML against hit testing faults that are invisible in the markup.
/// </summary>
/// <remarks>
/// A Grid draws later children on top of earlier ones, and a child with no width, height or alignment
/// stretches across the whole cell. So an element declared after the selection checkbox will cover it
/// unless it opts out of hit testing — and nothing in the XAML says so. The checkbox simply stops
/// responding.
///
/// This is checked structurally rather than by searching for a string, because the fault is about
/// ordering within a Grid, and a text search cannot see ordering. The test parses the template, finds
/// the checkbox, and requires every sibling after it to be non-hit-testable.
/// </remarks>
public class GameGridPageXamlTests
{
    static XDocument LoadPage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Pages", "GameGridPage.xaml");
        Assert.True(File.Exists(path), $"Could not find {path}");

        return XDocument.Load(path);
    }

    /// <summary>
    /// The XAML language namespace. Spelled out rather than prefixed, because an attribute lookup of
    /// "x:Key" asks XName to build a name containing a colon and throws.
    /// </summary>
    static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The outermost Grid of the item template - the one holding the checkbox and its siblings.
    /// </summary>
    /// <remarks>
    /// Found through the item template's <c>x:DataType</c> rather than by taking the first Grid under
    /// the view template. Both templates also contain a group header template, whose Grid comes first
    /// in document order and has nothing to do with the selection.
    /// </remarks>
    static XElement ItemTemplateRoot(XDocument page, string resourceKey)
    {
        var view = page.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "DataTemplate"
                                  && (string?)e.Attribute(Xaml + "Key") == resourceKey);

        Assert.NotNull(view);

        var itemTemplate = view!.Descendants()
            .FirstOrDefault(e =>
                e.Name.LocalName == "DataTemplate"
                && (string?)e.Attribute(Xaml + "DataType") is { } dataType
                && dataType.EndsWith(":Game", StringComparison.Ordinal));

        Assert.True(itemTemplate is not null, $"No item template for a Game found in {resourceKey}.");

        return itemTemplate!.Descendants().First(e => e.Name.LocalName == "Grid");
    }

    [Theory]
    [InlineData("GridVeiwTemplate")]
    [InlineData("ListViewTemplate")]
    public void EveryElementDrawnOverTheSelectionCheckboxIsTransparentToInput(string resourceKey)
    {
        var root = ItemTemplateRoot(LoadPage(), resourceKey);

        var checkbox = root.Elements().FirstOrDefault(e => e.Name.LocalName == "CheckBox");
        Assert.True(checkbox is not null, $"No selection checkbox found in {resourceKey}.");

        var siblings = root.Elements().ToList();
        var index = siblings.IndexOf(checkbox!);
        var drawnAbove = siblings.Skip(index + 1).ToList();

        foreach (var element in drawnAbove)
        {
            // Only elements that actually stretch over the tile can cover the checkbox. One placed in
            // its own Grid cell, as in the list view, cannot. The attributes are namespace-qualified
            // as Grid.Row and Grid.Column, so the local name is what is compared.
            var inItsOwnCell = element.Attributes()
                .Any(a => a.Name.LocalName is "Row" or "Column");

            var stretches = !inItsOwnCell
                            && (string?)element.Attribute("HorizontalAlignment") is null
                            && (string?)element.Attribute("VerticalAlignment") is null
                            && (string?)element.Attribute("Width") is null
                            && (string?)element.Attribute("Height") is null;

            if (!stretches)
            {
                continue;
            }

            Assert.True(
                (string?)element.Attribute("IsHitTestVisible") == "False",
                $"{resourceKey}: <{element.Name.LocalName}> is declared after the selection checkbox and "
                    + "stretches over the whole tile, so it is drawn on top of it and swallows every click "
                    + "on the tick. It needs IsHitTestVisible=\"False\".");
        }
    }

    [Theory]
    [InlineData("GridVeiwTemplate")]
    [InlineData("ListViewTemplate")]
    public void BothViewsOfferASelectionCheckboxBoundToTheGameAndWiredToRefresh(string resourceKey)
    {
        var root = ItemTemplateRoot(LoadPage(), resourceKey);

        var checkbox = root.Elements().FirstOrDefault(e => e.Name.LocalName == "CheckBox");
        Assert.NotNull(checkbox);

        // Without the handler the count never updates and "Update all" stays disabled, which reads as
        // the button being broken rather than as a selection that never registered.
        Assert.Equal("GameSelectionCheckBox_Click", (string?)checkbox!.Attribute("Click"));

        var isChecked = (string?)checkbox.Attribute("IsChecked");
        Assert.NotNull(isChecked);
        Assert.Contains("IsSelectedForMassUpdate", isChecked!, StringComparison.Ordinal);
        Assert.Contains("TwoWay", isChecked!, StringComparison.Ordinal);
    }
}
