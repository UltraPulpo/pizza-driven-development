using System.Text.Json;
using PDD.API.Models;

namespace PDD.API.Services;

public sealed class MenuCatalog
{
    private readonly Dictionary<string, PizzaSize> sizes;
    private readonly Dictionary<string, Topping> toppings;
    private readonly Dictionary<string, ToppingCategory> categories;

    public MenuCatalog(MenuDefinition menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (menu.Sizes is null || menu.Sizes.Count == 0 ||
            menu.Categories is null || menu.Toppings is null)
            throw new InvalidOperationException("Menu must contain sizes, categories, and toppings collections.");

        sizes = Index(menu.Sizes, size => size.Id);
        categories = Index(menu.Categories, category => category.Id);
        toppings = Index(menu.Toppings, topping => topping.Id);
        if (sizes.Values.Any(size => string.IsNullOrWhiteSpace(size.Name) || size.BasePriceCents <= 0))
            throw new InvalidOperationException("Menu sizes need a name and a positive base price in cents.");
        if (categories.Values.Any(category => category.Percentage < 0))
            throw new InvalidOperationException("Menu category percentages must be nonnegative.");
        if (toppings.Values.Any(topping => string.IsNullOrWhiteSpace(topping.Name) ||
                topping.CategoryId is null || !categories.ContainsKey(topping.CategoryId)))
            throw new InvalidOperationException("Menu toppings need a name and an existing category.");

        Menu = menu;
    }

    public MenuDefinition Menu { get; }
    public PizzaSize? FindSize(string id) => sizes.GetValueOrDefault(id);
    public Topping? FindTopping(string id) => toppings.GetValueOrDefault(id);
    public ToppingCategory FindCategory(string id) => categories[id];

    public static async Task<MenuCatalog> LoadAsync(string contentRootPath)
    {
        await using var stream = File.OpenRead(Path.Combine(contentRootPath, "menu.json"));
        var menu = await JsonSerializer.DeserializeAsync<MenuDefinition>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true });
        return new MenuCatalog(menu ?? throw new InvalidOperationException("Menu cannot be null."));
    }

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T> entries, Func<T, string> getId)
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(getId(entry)) || !index.TryAdd(getId(entry), entry))
                throw new InvalidOperationException("Menu IDs must be nonempty and unique within each collection.");
        }
        return index;
    }
}
