using System.Text.Json.Serialization;

namespace PDD.API.Models;

public sealed record OrderInput(IReadOnlyList<LineItemInput> Items, TipInput Tip, string? PromoCode = null);
public sealed record LineItemInput(string SizeId, int Quantity, IReadOnlyList<string> ToppingIds);
public sealed record TipInput(string Mode, string Value);

public sealed record PriceQuote(
    IReadOnlyList<PricedLine> Lines,
    int SubtotalCents,
    int DiscountCents,
    int TaxableSubtotalCents,
    int TaxCents,
    int TipCents,
    int TotalCents,
    string Description);

public sealed record PricedLine(
    string SizeId, string SizeName, int BasePriceCents, IReadOnlyList<PricedTopping> Toppings,
    int UnitPriceCents, int Quantity, int LineTotalCents);
public sealed record PricedTopping(string Id, string Name, int ChargeCents);

public sealed record SubmissionReceipt(Guid OrderId, PriceQuote Quote);
public sealed record OrderView(Guid OrderId, OrderStatus Status, OrderInput Order, PriceQuote Quote);
public sealed record AdvanceInput(string ExpectedStatus);

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus { NotStarted, Started, ReadyForPickup, Delivered, Cancelled }

public enum OrderError { Validation, NotFound, Conflict }

public sealed class OrderException(OrderError kind, string message) : Exception(message)
{
    public OrderError Kind { get; } = kind;
}
