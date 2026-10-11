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
/// stretches across the whole cell. So an element declared after something that needs the mouse will
/// cover it unless it opts out of hit testing — and nothing in the XAML says so.
///
/// This is checked structurally rather than by searching for a string, because the fault is about
/// ordering within a Grid, and a text search cannot see ordering. The test parses the template and
/// requires every full-tile sibling to be non-hit-testable.
///
/// The tile carried a selection checkbox here once. It could not be reached: the cover art was declared
/// after it and stretched over the whole tile, so every click on the tick opened the game instead. The
/// selection is gone now, so the checkbox is gone with it — but the class of fault has not, and the next
/// control to need the mouse on a tile will hit it again.
/// </remarks>
public class GameGridPageXamlTests
{
    /// <summary>
    /// The XAML language namespace. Spelled out rather than prefixed, because an attribute lookup of
    /// "x:Key" asks XName to build a name containing a colon and throws.
    /// </summary>
    static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

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
    /// The outermost Grid of the item template - the one holding the tile's children.
    /// </summary>
    /// <remarks>
    /// Found through the item template's <c>x:DataType</c> rather than by taking the first Grid under
    /// the view template. Both templates also contain a group header template, whose Grid comes first
    /// in document order and has nothing to do with the tile.
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
    public void NoControlOnTheTileIsCoveredBySomethingDrawnOverIt(string resourceKey)
    {
        var root = ItemTemplateRoot(LoadPage(), resourceKey);

        // Only controls that need the mouse. A TextBlock drawn over another TextBlock is harmless -
        // the click still reaches the tile and still opens the game. What must never happen is an
        // interactive control being covered, because then it cannot be operated at all, and nothing
        // in the XAML indicates that is what has happened.
        var interactive = new[]
        {
            "CheckBox", "Button", "AppBarButton", "ToggleSwitch", "TextBox",
            "ComboBox", "Slider", "HyperlinkButton", "ToggleButton", "RadioButton",
        };

        var children = root.Elements()
            .Where(e => e.Name.LocalName.EndsWith("Definitions", StringComparison.Ordinal) == false)
            .ToList();

        for (var index = 0; index < children.Count; index++)
        {
            var element = children[index];
            if (interactive.Contains(element.Name.LocalName) == false)
            {
                continue;
            }

            foreach (var drawnAbove in children.Skip(index + 1))
            {
                var inItsOwnCell = drawnAbove.Attributes().Any(a => a.Name.LocalName is "Row" or "Column");
                var stretches = !inItsOwnCell
                                && (string?)drawnAbove.Attribute("HorizontalAlignment") is null
                                && (string?)drawnAbove.Attribute("VerticalAlignment") is null
                                && (string?)drawnAbove.Attribute("Width") is null
                                && (string?)drawnAbove.Attribute("Height") is null;

                if (!stretches)
                {
                    continue;
                }

                Assert.True(
                    (string?)drawnAbove.Attribute("IsHitTestVisible") == "False",
                    $"{resourceKey}: <{element.Name.LocalName}> is covered by <{drawnAbove.Name.LocalName}>, "
                        + "which stretches over the whole tile. Being drawn on top is also being hit "
                        + "first, so the control cannot be operated and nothing looks wrong.");
            }
        }
    }

    [Theory]
    [InlineData("GridVeiwTemplate")]
    [InlineData("ListViewTemplate")]
    public void NoTileCarriesAMassUpdateCheckboxAnyMore(string resourceKey)
    {
        // The selection was the reason the tile needed hit testing at all. "Update all" acts on the
        // whole library now, so a checkbox over each tile would be a control that does nothing.
        var root = ItemTemplateRoot(LoadPage(), resourceKey);

        Assert.False(
            root.Descendants().Any(e => e.Name.LocalName == "CheckBox"),
            $"{resourceKey} still has a checkbox on the tile. Mass update no longer selects games.");
    }

    [Fact]
    public void TheMassUpdateButtonHasNoSelectionFlyout()
    {
        // "Select all shown" and "Clear selection" described a selection that no longer exists.
        var page = LoadPage();

        var button = page.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "AppBarButton"
                                  && (string?)e.Attribute("Label") == "Update all");

        Assert.True(button is not null, "The Update all button was not found.");

        Assert.False(
            button!.Descendants().Any(e => e.Name.LocalName == "MenuFlyout"),
            "Update all still opens a flyout, which described the removed selection.");
    }

    [Fact]
    public void TheMassUpdateButtonIsDisabledOnlyWhileARunIsInProgress()
    {
        // It used to be disabled until games were ticked. With no selection there is nothing to wait
        // for, and a button that is greyed out for no stated reason looks broken.
        var page = LoadPage();

        var button = page.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "AppBarButton"
                                  && (string?)e.Attribute("Label") == "Update all");

        Assert.True(button is not null, "The Update all button was not found.");

        var enabled = (string?)button!.Attribute("IsEnabled");
        Assert.Contains("CanUpdate", enabled!, StringComparison.Ordinal);
    }
}
