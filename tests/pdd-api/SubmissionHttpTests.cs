using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDD.API.Models;
using PDD.API.Services;

namespace PDD.API.Tests;

public class SubmissionHttpTests
{
    private const string Input = """
        {"items":[{"sizeId":"large","quantity":1,"toppingIds":["pepperoni","bacon"]},
                  {"sizeId":"small","quantity":2,"toppingIds":[]}],
         "tip":{"mode":"dollars","value":"1.005"},"promoCode":"ILOVEPIZZA"}
        """;

    [Test]
    public async Task Submit_QuoteAndLookup_PreservesAcceptedInputAndQuote()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var quoteResponse = await client.PostAsync("/api/quote", Json(Input));
        var quote = JsonNode.Parse(await quoteResponse.Content.ReadAsStringAsync());
        Assert.That(await app.CountOrdersAsync(), Is.Zero, "Quoting must not persist an order.");
        using var response = await SubmitAsync(client, Guid.NewGuid(), Input);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var receipt = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var orderId = receipt["orderId"]!.GetValue<Guid>();
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/orders/{orderId}"));
        Assert.That(JsonNode.DeepEquals(receipt["quote"], quote), Is.True);

        var current = JsonNode.Parse(await client.GetStringAsync($"/api/orders/{orderId}"))!;
        Assert.Multiple(() =>
        {
            Assert.That(current["orderId"]!.GetValue<Guid>(), Is.EqualTo(orderId));
            Assert.That(current["status"]!.GetValue<string>(), Is.EqualTo("NotStarted"));
            Assert.That(JsonNode.DeepEquals(current["order"], JsonNode.Parse(Input)), Is.True);
            Assert.That(JsonNode.DeepEquals(current["quote"], quote), Is.True);
        });
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Submit_LostAcknowledgementAndRestart_ReplaysReceiptWithoutRepricing()
    {
        var original = new ApiFixture { DeleteDatabaseOnDispose = false };
        using var restarted = new ApiFixture(original.DatabasePath);
        var key = Guid.NewGuid();
        string accepted;
        using (original)
        using (var client = original.CreateClient())
        using (var response = await SubmitAsync(client, key, Input))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            accepted = await response.Content.ReadAsStringAsync();
        }

        using var changedMenu = restarted.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(new MenuCatalog(new MenuDefinition([new("tiny", "Tiny", 1)], [], [])))));
        using var retryClient = changedMenu.CreateClient();
        using var retry = await SubmitAsync(retryClient, key, Input);
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(accepted));
        var receipt = JsonNode.Parse(accepted)!;
        var current = JsonNode.Parse(await retryClient.GetStringAsync(retry.Headers.Location))!;
        Assert.That(JsonNode.DeepEquals(current["quote"], receipt["quote"]), Is.True);
        Assert.That(current["quote"]!["lines"]![0]!["sizeName"]!.GetValue<string>(), Is.EqualTo("Large"));
        Assert.That(await restarted.CountOrdersAsync(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Submit_CoordinatedConcurrentRequests_EnforcesOneWinner(bool differentInput)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var key = Guid.NewGuid();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Send(string body)
        {
            await gate.Task;
            return await SubmitAsync(client, key, body);
        }
        var first = Send(Input);
        var second = Send(differentInput ? Input.Replace("\"1.005\"", "\"2\"") : Input);
        gate.SetResult();
        var responses = await Task.WhenAll(first, second);
        using var a = responses[0];
        using var b = responses[1];
        Assert.That(responses.Select(response => response.StatusCode),
            differentInput ? Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }) :
                Is.EqualTo(new[] { HttpStatusCode.Created, HttpStatusCode.Created }));
        if (!differentInput)
        {
            Assert.That(await a.Content.ReadAsStringAsync(), Is.EqualTo(await b.Content.ReadAsStringAsync()));
            Assert.That(a.Headers.Location, Is.EqualTo(b.Headers.Location));
        }
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    [TestCase("tip-text")]
    [TestCase("quantity")]
    [TestCase("topping-order")]
    [TestCase("line-order")]
    [TestCase("invalid-input")]
    public async Task Submit_ReusedKeyWithDifferentTypedInput_ConflictsWithoutMutation(string change)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var key = Guid.NewGuid();
        using var accepted = await SubmitAsync(client, key, Input);
        var before = await client.GetStringAsync(accepted.Headers.Location);
        var changed = JsonNode.Parse(Input)!;
        switch (change)
        {
            case "tip-text": changed["tip"]!["value"] = "1.0050"; break;
            case "quantity": changed["items"]![0]!["quantity"] = 2; break;
            case "topping-order": changed["items"]![0]!["toppingIds"] = new JsonArray("bacon", "pepperoni"); break;
            case "line-order":
                var items = changed["items"]!.AsArray();
                changed["items"] = new JsonArray(items[1]!.DeepClone(), items[0]!.DeepClone());
                break;
            case "invalid-input": changed["items"] = new JsonArray(); break;
        }
        using var rejected = await SubmitAsync(client, key, changed.ToJsonString());
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await client.GetStringAsync(accepted.Headers.Location), Is.EqualTo(before));
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Submit_EquivalentJsonFormatting_ReplaysButNewKeyCreatesAnotherOrder()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var key = Guid.NewGuid();
        using var accepted = await SubmitAsync(client, key, Input);
        var input = JsonNode.Parse(Input)!;
        var reordered = new JsonObject
        {
            ["promoCode"] = input["promoCode"]!.DeepClone(),
            ["tip"] = input["tip"]!.DeepClone(),
            ["items"] = input["items"]!.DeepClone()
        };
        using var replay = await SubmitAsync(client, key, reordered.ToJsonString());
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await accepted.Content.ReadAsStringAsync()));
        using var newOrder = await SubmitAsync(client, Guid.NewGuid(), Input);
        Assert.That(newOrder.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(newOrder.Headers.Location, Is.Not.EqualTo(accepted.Headers.Location));
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(2));
    }

    [TestCase(null, Input)]
    [TestCase("not-a-guid", Input)]
    [TestCase("d94c96ef-5d44-4b82-a386-6380ee53a90b", "{}")]
    [TestCase("d94c96ef-5d44-4b82-a386-6380ee53a90b", "{")]
    public async Task Submit_InvalidInput_Returns400WithoutCreatingAnOrder(string? key, string input)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = Json(input) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(await app.CountOrdersAsync(), Is.Zero);
        Assert.That(app.Logs.Entries.Any(entry => entry.Level >= LogLevel.Error), Is.False);
    }

    [TestCase("not-a-guid", HttpStatusCode.BadRequest)]
    [TestCase("d94c96ef-5d44-4b82-a386-6380ee53a90b", HttpStatusCode.NotFound)]
    public async Task Get_InvalidOrMissingOrder_ReturnsProblemDetails(string id, HttpStatusCode expected)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var response = await client.GetAsync($"/api/orders/{id}");
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task Submit_BusyDatabase_Returns503WithWarningAndAllowsExplicitRetry()
    {
        using var app = new ApiFixture { DefaultTimeout = 1 };
        using var client = app.CreateClient();
        var key = Guid.NewGuid();
        await using (var blocker = new SqliteConnection(app.ConnectionString))
        {
            await blocker.OpenAsync();
            using var transaction = blocker.BeginTransaction();
            using var response = await SubmitAsync(client, key, Input);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That((await response.Content.ReadAsStringAsync()), Does.Contain("outcome may be unknown"));
            Assert.That(app.Logs.Entries.Any(entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("POST /api/orders") && entry.Error is SqliteException), Is.True);
            transaction.Rollback();
        }
        Assert.That(await app.CountOrdersAsync(), Is.Zero);
        using var retry = await SubmitAsync(client, key, Input);
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Get_UnexpectedStorageFailure_ReturnsSafe500WithErrorContext(bool corruptJson)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        using var accepted = await SubmitAsync(client, Guid.NewGuid(), Input);
        await using (var connection = new SqliteConnection(app.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = corruptJson ? "UPDATE Orders SET order_json = 'broken-json';" : "DROP TABLE Orders;";
            await command.ExecuteNonQueryAsync();
        }
        using var response = await client.GetAsync(accepted.Headers.Location);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.That(problem["detail"]!.GetValue<string>(),
            Is.EqualTo("The service could not complete the request. Please retry or check the order status."));
        Assert.That(app.Logs.Entries.Any(entry => entry.Level == LogLevel.Error &&
            entry.Message.Contains("GET /api/orders/") && entry.Error is not null), Is.True);
    }

    private static StringContent Json(string input) => new(input, Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, Guid key, string input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = Json(input) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }
}
