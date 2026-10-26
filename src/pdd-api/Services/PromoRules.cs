using PDD.API.Models;

namespace PDD.API.Services;

public interface IPromoRule
{
    string Code { get; }
    int GetDiscount(OrderInput order, int subtotalCents);
}

public sealed class LovePizzaRule : IPromoRule
{
    public string Code => "ILOVEPIZZA";
    public int GetDiscount(OrderInput order, int subtotalCents) =>
        decimal.ToInt32(decimal.Round(subtotalCents * 0.10m, 0, MidpointRounding.AwayFromZero));
}

public sealed class Save5Rule : IPromoRule
{
    public string Code => "SAVE5";

    public int GetDiscount(OrderInput order, int subtotalCents)
    {
        if (subtotalCents <= 2000)
            throw new OrderException(OrderError.Validation, "SAVE5 requires a food subtotal over $20.00 before tax and tip.");
        return 500;
    }
}
