using System.Net;
using System.Text;
using System.Text.Json;

namespace PDD.API.Tests;

public class QuoteHttpTests
{
    private const string ReferenceOrder = """
        {"items":[{"sizeId":"large","quantity":2,"toppingIds":["pepperoni","bacon"]}],
         "tip":{"mode":"percent","value":"15"},"promoCode":"ILOVEPIZZA"}
        """;

    [Test]
    public async Task Quote_ReferenceOrder_ReturnsExactCamelCaseBreakdown()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var response = await client.PostAsync("/api/quote", Json(ReferenceOrder));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var quote = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(quote.GetProperty("subtotalCents").GetInt32(), Is.EqualTo(2856));
            Assert.That(quote.GetProperty("discountCents").GetInt32(), Is.EqualTo(286));
            Assert.That(quote.GetProperty("taxableSubtotalCents").GetInt32(), Is.EqualTo(2570));
            Assert.That(quote.GetProperty("taxCents").GetInt32(), Is.EqualTo(167));
            Assert.That(quote.GetProperty("tipCents").GetInt32(), Is.EqualTo(386));
            Assert.That(quote.GetProperty("totalCents").GetInt32(), Is.EqualTo(3123));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("unitPriceCents").GetInt32(), Is.EqualTo(1428));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("sizeId").GetString(), Is.EqualTo("large"));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("sizeName").GetString(), Is.EqualTo("Large"));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("basePriceCents").GetInt32(), Is.EqualTo(1200));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("quantity").GetInt32(), Is.EqualTo(2));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("lineTotalCents").GetInt32(), Is.EqualTo(2856));
            Assert.That(quote.GetProperty("lines")[0].GetProperty("toppings").EnumerateArray()
                .Select(topping => (topping.GetProperty("id").GetString(), topping.GetProperty("name").GetString(),
                    topping.GetProperty("chargeCents").GetInt32())),
                Is.EqualTo(new[] { ("pepperoni", "Pepperoni", 96), ("bacon", "Bacon", 132) }));
            Assert.That(quote.GetProperty("description").GetString(), Is.EqualTo("2 x Large pizza with Pepperoni, Bacon"));
        });
    }

    [TestCase("""{"items":[],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":0,"toppingIds":[]}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":2147483648,"toppingIds":[]}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":[]}],"tip":{"mode":"dollars","value":1}}""")]
    [TestCase("""{"items":[null],"tip":null}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":[]}]}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1.5,"toppingIds":[]}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":null}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":[null]}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":null,"quantity":1,"toppingIds":[]}],"tip":{"mode":"dollars","value":"0"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":[]}],"tip":{"mode":"dollars"}}""")]
    [TestCase("""{"items":[{"sizeId":"small","quantity":1,"toppingIds":[]}],"tip":{"value":"0"}}""")]
    [TestCase("{}")]
    [TestCase("null")]
    [TestCase("{")]
    [TestCase("")]
    public async Task Quote_InvalidOrMalformedInput_ReturnsActionableProblemDetails(string input)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var response = await client.PostAsync("/api/quote", Json(input));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(document.RootElement.GetProperty("detail").GetString(), Is.Not.Null.And.Not.Empty);
    }

    private static StringContent Json(string input) => new(input, Encoding.UTF8, "application/json");
}
