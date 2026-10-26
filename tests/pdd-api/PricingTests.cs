using PDD.API.Models;
using PDD.API.Services;

namespace PDD.API.Tests;

public class PricingTests
{
    private MenuCatalog menu = null!;
    private PricingService pricing = null!;

    [OneTimeSetUp]
    public async Task LoadMenu()
    {
        menu = await MenuCatalog.LoadAsync(Path.Combine(ApiFixture.RepositoryRoot, "src", "pdd-api"));
        pricing = new PricingService(menu, [new LovePizzaRule(), new Save5Rule()]);
    }

    private static OrderInput Order(string size = "small", int quantity = 1, string[]? toppings = null,
        string tipMode = "dollars", string tip = "0", string? promo = null) =>
        new([new(size, quantity, toppings ?? [])], new(tipMode, tip), promo);

    [TestCase("small", 500, 40, 55, 33, 634)]
    [TestCase("medium", 700, 56, 77, 46, 887)]
    [TestCase("large", 1200, 96, 132, 78, 1521)]
    public void Quote_SizeAndToppings_RoundsEachComponent(
        string size, int baseCents, int basicCents, int premiumCents, int plainTax, int toppedTotal)
    {
        var plain = pricing.Quote(Order(size));
        var topped = pricing.Quote(Order(size, toppings: ["pepperoni", "bacon"]));
        Assert.Multiple(() =>
        {
            Assert.That(plain.SubtotalCents, Is.EqualTo(baseCents));
            Assert.That(plain.TaxCents, Is.EqualTo(plainTax));
            Assert.That(plain.TipCents, Is.Zero);
            Assert.That(plain.TotalCents, Is.EqualTo(baseCents + plainTax));
            Assert.That(topped.Lines[0].Toppings.Select(topping => topping.ChargeCents),
                Is.EqualTo(new[] { basicCents, premiumCents }));
            Assert.That(topped.TotalCents, Is.EqualTo(toppedTotal));
            Assert.That(topped.TotalCents, Is.EqualTo(topped.SubtotalCents - topped.DiscountCents + topped.TaxCents + topped.TipCents));
        });
    }

    [Test]
    public void Quote_ReferencePromoAndTip_UsesDiscountedBases()
    {
        var quote = pricing.Quote(Order("large", 2, ["pepperoni", "bacon"], "percent", "15", "ILOVEPIZZA"));
        Assert.Multiple(() =>
        {
            Assert.That(quote.Lines[0].UnitPriceCents, Is.EqualTo(1428));
            Assert.That(quote.Lines[0].LineTotalCents, Is.EqualTo(2856));
            Assert.That(quote.SubtotalCents, Is.EqualTo(2856));
            Assert.That(quote.DiscountCents, Is.EqualTo(286));
            Assert.That(quote.TaxableSubtotalCents, Is.EqualTo(2570));
            Assert.That(quote.TaxCents, Is.EqualTo(167));
            Assert.That(quote.TipCents, Is.EqualTo(386));
            Assert.That(quote.TotalCents, Is.EqualTo(3123));
        });
    }

    [Test]
    public void Quote_MultipleLines_PreservesQuantitiesNamesAndComponentSums()
    {
        var input = new OrderInput(
            [new("small", 2, []), new("medium", 3, ["onions", "garlic"])], new("dollars", "1.005"));
        var quote = pricing.Quote(input);
        Assert.Multiple(() =>
        {
            Assert.That(quote.Lines.Select(line => line.LineTotalCents), Is.EqualTo(new[] { 1000, 2499 }));
            Assert.That(quote.SubtotalCents, Is.EqualTo(3499));
            Assert.That(quote.TaxCents, Is.EqualTo(227));
            Assert.That(quote.TipCents, Is.EqualTo(101));
            Assert.That(quote.TotalCents, Is.EqualTo(3827));
            Assert.That(quote.Description, Is.EqualTo("2 x Small pizza with no toppings; 3 x Medium pizza with Onions, Garlic"));
        });
    }

    [Test]
    public void Quote_NewMenuData_RoundsToppingsBeforeMultiplyingQuantity()
    {
        var extended = new MenuCatalog(menu.Menu with
        {
            Sizes = [.. menu.Menu.Sizes, new("tiny", "Tiny", 101)],
            Categories = [.. menu.Menu.Categories, new("special", 50)],
            Toppings = [.. menu.Menu.Toppings, new("test-topping", "Test topping", "special")]
        });
        var service = new PricingService(extended, []);
        var quote = service.Quote(Order("tiny", 2, ["test-topping"]));
        Assert.Multiple(() =>
        {
            Assert.That(quote.Lines[0].Toppings[0].ChargeCents, Is.EqualTo(51));
            Assert.That(quote.SubtotalCents, Is.EqualTo(304));
            Assert.That(quote.TaxCents, Is.EqualTo(20));
            Assert.That(quote.TotalCents, Is.EqualTo(324));
        });
    }

    [Test]
    public void Quote_Save5_StrictlyRequiresMoreThanTwentyDollars()
    {
        var catalog = new MenuCatalog(new([new("boundary", "Boundary", 2001)],
            [], []));
        var service = new PricingService(catalog, [new Save5Rule()]);
        var quote = service.Quote(Order("boundary", promo: "SAVE5"));
        Assert.That(quote.DiscountCents, Is.EqualTo(500));
        Assert.That(quote.TotalCents, Is.EqualTo(1599));
        var error = Assert.Throws<OrderException>(() => pricing.Quote(Order("small", 4, promo: "SAVE5")));
        Assert.That(error!.Kind, Is.EqualTo(OrderError.Validation));
    }

    [TestCase("unknown")]
    [TestCase("ilovepizza")]
    [TestCase(" ILOVEPIZZA")]
    [TestCase("ILOVEPIZZA ")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("SAVE5,ILOVEPIZZA")]
    public void Quote_NonExactPromo_RejectsInsteadOfIgnoring(string promo)
    {
        Assert.Throws<OrderException>(() => pricing.Quote(Order(promo: promo)));
    }

    [Test]
    public void Quote_OrderDependentPromo_DistinguishesEqualSubtotals()
    {
        var service = new PricingService(menu, [new LargeOnlyRule()]);
        var large = service.Quote(Order("large", promo: "LARGEONLY"));
        var other = new OrderInput([new("medium", 1, []), new("small", 1, [])],
            new("dollars", "0"), "LARGEONLY");
        Assert.Multiple(() =>
        {
            Assert.That(large.SubtotalCents, Is.EqualTo(1200));
            Assert.That(large.DiscountCents, Is.EqualTo(100));
            Assert.That(large.TotalCents, Is.EqualTo(1172));
            Assert.Throws<OrderException>(() => service.Quote(other));
        });
    }

    [Test]
    public void Constructor_DuplicatePromoCodes_FailsConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => new PricingService(menu, [new LovePizzaRule(), new LovePizzaRule()]));
    }

    [TestCase(-1)]
    [TestCase(501)]
    public void Quote_InvalidRegisteredDiscount_IsAProgrammingError(int discount)
    {
        var service = new PricingService(menu, [new InvalidRule(discount)]);
        Assert.Throws<InvalidOperationException>(() => service.Quote(Order(promo: "INVALID")));
    }

    private static IEnumerable<OrderInput> InvalidOrders()
    {
        yield return Order() with { Items = [] };
        yield return Order(quantity: 0);
        yield return Order(quantity: -1);
        yield return Order("missing");
        yield return Order(toppings: ["missing"]);
        yield return Order(toppings: ["onions", "onions"]);
        yield return Order(tipMode: "other");
        yield return Order(tip: "-1");
        yield return Order(tip: "");
        yield return Order(tip: "NaN");
        yield return Order(tip: "1,000");
        yield return Order(tip: "0.00000000000000000000000000001");
        yield return Order(tip: "1.00000000000000000000000000001");
        yield return Order(tip: "79228162514264337593543950336");
        yield return Order(tip: "21474836.48");
        yield return Order(tipMode: "percent", tip: "79228162514264337593543950335");
        yield return Order(quantity: int.MaxValue);
        yield return Order(quantity: 4_200_000);
        yield return Order() with { Items = [new("large", 1_000_000, []), new("large", 1_000_000, [])] };
    }

    [TestCaseSource(nameof(InvalidOrders))]
    public void Quote_InvalidInputOrOverflow_ProducesClearValidationError(OrderInput input)
    {
        var error = Assert.Throws<OrderException>(() => pricing.Quote(input));
        Assert.That(error!.Kind, Is.EqualTo(OrderError.Validation));
        Assert.That(error.Message, Is.Not.Empty);
    }

    [Test]
    public void Quote_PercentageAboveOneHundred_IsAllowed()
    {
        var quote = pricing.Quote(Order(tipMode: "percent", tip: "150"));
        Assert.That(quote.TipCents, Is.EqualTo(750));
        Assert.That(quote.TotalCents, Is.EqualTo(1283));
    }

    [TestCase("dollars", "-0.00")]
    [TestCase("percent", "-0")]
    public void Quote_SignedZeroTip_IsNonnegative(string mode, string value)
    {
        var quote = pricing.Quote(Order(tipMode: mode, tip: value));
        Assert.That(quote.TipCents, Is.Zero);
        Assert.That(quote.TotalCents, Is.EqualTo(533));
    }

    private sealed class LargeOnlyRule : IPromoRule
    {
        public string Code => "LARGEONLY";
        public int GetDiscount(OrderInput order, int subtotalCents) =>
            order.Items.Any(item => item.SizeId == "large") ? 100 :
                throw new OrderException(OrderError.Validation, "LARGEONLY requires a Large pizza.");
    }

    private sealed class InvalidRule(int discount) : IPromoRule
    {
        public string Code => "INVALID";
        public int GetDiscount(OrderInput order, int subtotalCents) => discount;
    }
}
