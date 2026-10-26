using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PDD.API.Tests;

public class MenuHttpTests
{
    [Test]
    public async Task GetMenu_ReturnsTheRestaurantMenu_AndPreservesDatabaseStartup()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/menu");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var menu = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(menu.GetProperty("sizes").EnumerateArray()
                .Select(size => (size.GetProperty("name").GetString(), size.GetProperty("basePriceCents").GetInt32())),
                Is.EqualTo(new[] { ("Small", 500), ("Medium", 700), ("Large", 1200) }));
            Assert.That(menu.GetProperty("categories").EnumerateArray()
                .Select(category => (category.GetProperty("id").GetString(), category.GetProperty("percentage").GetDecimal())),
                Is.EqualTo(new[] { ("basic", 8m), ("premium", 11m) }));
            Assert.That(menu.GetProperty("toppings").EnumerateArray()
                .Select(topping => (topping.GetProperty("id").GetString(), topping.GetProperty("categoryId").GetString())),
                Is.EqualTo(new[]
                {
                    ("pepperoni", "basic"), ("sausage", "basic"), ("extra-cheese", "basic"),
                    ("onions", "basic"), ("black-olives", "basic"), ("green-peppers", "basic"),
                    ("bacon", "premium"), ("chicken", "premium"), ("garlic", "premium"),
                    ("artichoke-hearts", "premium"), ("sun-dried-tomatoes", "premium"), ("pineapple", "premium")
                }));
        });

        using var health = await client.GetAsync("/api/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var connection = new SqliteConnection($"Data Source={app.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("wal"));
    }
}
