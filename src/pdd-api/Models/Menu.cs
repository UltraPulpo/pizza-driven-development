namespace PDD.API.Models;

public sealed record MenuDefinition(
    IReadOnlyList<PizzaSize> Sizes,
    IReadOnlyList<ToppingCategory> Categories,
    IReadOnlyList<Topping> Toppings);

public sealed record PizzaSize(string Id, string Name, int BasePriceCents);
public sealed record ToppingCategory(string Id, decimal Percentage);
public sealed record Topping(string Id, string Name, string CategoryId);
