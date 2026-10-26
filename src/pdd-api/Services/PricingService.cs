using System.Globalization;
using PDD.API.Models;

namespace PDD.API.Services;

public sealed class PricingService
{
    private readonly MenuCatalog catalog;
    private readonly Dictionary<string, IPromoRule> promos = new(StringComparer.Ordinal);

    public PricingService(MenuCatalog catalog, IEnumerable<IPromoRule> rules)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rules);
        this.catalog = catalog;
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Code) || !promos.TryAdd(rule.Code, rule))
                throw new InvalidOperationException("Registered promo codes must be nonempty and unique.");
        }
    }

    public PriceQuote Quote(OrderInput input)
    {
        try
        {
            return Calculate(input);
        }
        catch (OverflowException)
        {
            throw Invalid("The order exceeds the supported amount. Reduce the quantity or tip.");
        }
    }

    private PriceQuote Calculate(OrderInput input)
    {
        if (input?.Items is null || input.Items.Count == 0)
            throw Invalid("Add at least one pizza to the order.");
        if (input.Tip is null || input.Tip.Mode is not ("dollars" or "percent"))
            throw Invalid("Choose a tip mode of dollars or percent.");
        var tipValue = ParseTip(input.Tip.Value);
        var lines = new List<PricedLine>();
        var subtotal = 0;
        foreach (var item in input.Items)
        {
            if (item is null || item.SizeId is null || catalog.FindSize(item.SizeId) is not { } size)
                throw Invalid("Choose a known pizza size for each line.");
            if (item.Quantity <= 0)
                throw Invalid("Each pizza quantity must be a positive whole number.");
            if (item.ToppingIds is null)
                throw Invalid("Provide a topping list for each pizza; use an empty list for no toppings.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var toppings = new List<PricedTopping>();
            var unitPrice = size.BasePriceCents;
            foreach (var id in item.ToppingIds)
            {
                if (id is null || catalog.FindTopping(id) is not { } topping)
                    throw Invalid("Choose only known toppings.");
                if (!seen.Add(id))
                    throw Invalid($"Select {topping.Name} at most once per pizza line.");
                var charge = RoundCents(size.BasePriceCents * (catalog.FindCategory(topping.CategoryId).Percentage / 100m));
                unitPrice = checked(unitPrice + charge);
                toppings.Add(new(topping.Id, topping.Name, charge));
            }
            var lineTotal = checked(unitPrice * item.Quantity);
            subtotal = checked(subtotal + lineTotal);
            lines.Add(new(size.Id, size.Name, size.BasePriceCents, toppings, unitPrice, item.Quantity, lineTotal));
        }

        var discount = 0;
        if (input.PromoCode is not null)
        {
            if (!promos.TryGetValue(input.PromoCode, out var promo))
                throw Invalid("Unknown promo code. Enter one code exactly as printed, or remove it.");
            discount = promo.GetDiscount(input, subtotal);
            if (discount < 0 || discount > subtotal)
                throw new InvalidOperationException($"Promo '{promo.Code}' returned an invalid discount.");
        }
        var taxableSubtotal = checked(subtotal - discount);
        var tax = RoundCents(taxableSubtotal * 0.065m);
        var tip = RoundCents(input.Tip.Mode == "dollars"
            ? tipValue * 100m : taxableSubtotal * (tipValue / 100m));
        var total = checked(taxableSubtotal + tax + tip);
        var description = string.Join("; ", lines.Select(line =>
            $"{line.Quantity} x {line.SizeName} pizza with " +
            (line.Toppings.Count == 0 ? "no toppings" : string.Join(", ", line.Toppings.Select(topping => topping.Name)))));
        return new(lines, subtotal, discount, taxableSubtotal, tax, tip, total, description);
    }

    private static decimal ParseTip(string? value)
    {
        if (value is null || !decimal.TryParse(value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0)
            throw Invalid("Enter a nonnegative decimal tip value using a dot, for example 1.50 or 15.");

        // Decimal.TryParse can round excess precision; reject it rather than silently changing the input.
        var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0');
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        var normalized = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? "" : "." + fraction);
        if (normalized != amount.ToString("0.############################", CultureInfo.InvariantCulture))
            throw Invalid("The tip value exceeds the supported decimal precision.");
        return amount;
    }

    private static int RoundCents(decimal amount) =>
        checked((int)decimal.Round(amount, 0, MidpointRounding.AwayFromZero));

    private static OrderException Invalid(string message) => new(OrderError.Validation, message);
}
