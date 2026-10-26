using PDD.API.Models;
using PDD.API.Services;

namespace PDD.API.Tests;

public class MenuCatalogTests
{
    private static MenuDefinition Menu => new(
        [new("small", "Small", 500)],
        [new("basic", 8)],
        [new("onions", "Onions", "basic")]);

    private static IEnumerable<MenuDefinition> InvalidMenus()
    {
        yield return Menu with { Sizes = [Menu.Sizes[0], Menu.Sizes[0]] };
        yield return Menu with { Categories = [Menu.Categories[0], Menu.Categories[0]] };
        yield return Menu with { Toppings = [Menu.Toppings[0], Menu.Toppings[0]] };
        yield return Menu with { Toppings = [new("garlic", "Garlic", "missing")] };
        yield return Menu with { Sizes = [new("small", "Small", 0)] };
        yield return Menu with { Sizes = [new("small", "Small", -1)] };
        yield return Menu with { Categories = [new("basic", -1)] };
        yield return Menu with { Sizes = [] };
        yield return Menu with { Sizes = [new("", "Small", 500)] };
    }

    [TestCaseSource(nameof(InvalidMenus))]
    public void Constructor_InvalidMenu_RejectsConfiguration(MenuDefinition menu)
    {
        Assert.Throws<InvalidOperationException>(() => new MenuCatalog(menu));
    }

    [Test]
    public void Constructor_ZeroRateCategory_IsValid()
    {
        Assert.DoesNotThrow(() => new MenuCatalog(Menu with { Categories = [new("basic", 0)] }));
    }
}
