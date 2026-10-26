using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace PDD.API.Tests;

public class LifecycleHttpTests
{
    private const string Input = """
        {"items":[{"sizeId":"small","quantity":1,"toppingIds":[]}],
         "tip":{"mode":"dollars","value":"0"},"promoCode":null}
        """;
    private const string Replacement = """
        {"items":[{"sizeId":"large","quantity":2,"toppingIds":["pepperoni","bacon"]}],
         "tip":{"mode":"percent","value":"15"},"promoCode":"ILOVEPIZZA"}
        """;

    [Test]
    public async Task Update_QuoteAndLookup_PreservesReplacementAndOriginalReceipt()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (key, id, originalReceipt) = await CreateOrderAsync(client);
        using var quote = await client.PostAsync("/api/quote", Json(Replacement));
        var expectedQuote = await ReadAsync(quote);
        using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updated = await ReadAsync(update);
        Assert.That(JsonNode.DeepEquals(updated["quote"], expectedQuote), Is.True);
        Assert.That(updated["quote"]!["totalCents"]!.GetValue<int>(), Is.EqualTo(3123));
        Assert.That(JsonNode.DeepEquals(updated["order"], JsonNode.Parse(Replacement)), Is.True);
        var stored = await client.GetStringAsync($"/api/orders/{id}");
        Assert.That(JsonNode.DeepEquals(JsonNode.Parse(stored), updated), Is.True);
        using var invalid = await client.PutAsync($"/api/orders/{id}", Json("{}"));
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await client.GetStringAsync($"/api/orders/{id}"), Is.EqualTo(stored));
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = Json(Input) };
        replayRequest.Headers.Add("Idempotency-Key", key.ToString());
        using var replay = await client.SendAsync(replayRequest);
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(originalReceipt));
        Assert.That(await client.GetStringAsync($"/api/orders/{id}"), Is.EqualTo(stored));
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    private static StringContent Json(string input) => new(input, Encoding.UTF8, "application/json");

    [Test]
    public async Task Advance_EveryLegalStage_PreventsReplayAndTerminalChanges()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        foreach (var (from, to) in new[]
        {
            ("NotStarted", "Started"), ("Started", "ReadyForPickup"), ("ReadyForPickup", "Delivered")
        })
        {
            using var advanced = await AdvanceAsync(client, id, from);
            Assert.That(advanced.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await ReadAsync(advanced))["status"]!.GetValue<string>(), Is.EqualTo(to));
            using var replay = await AdvanceAsync(client, id, from);
            using var cancel = await client.PostAsync($"/api/orders/{id}/cancel", null);
            using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
            Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That((JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!)["status"]!.GetValue<string>(), Is.EqualTo(to));
        }
        using var terminal = await AdvanceAsync(client, id, "Delivered");
        Assert.That(terminal.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private static async Task<(Guid Key, Guid Id, string Receipt)> CreateOrderAsync(HttpClient client)
    {
        var key = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = new StringContent(Input, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key.ToString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadAsStringAsync();
        return (key, JsonNode.Parse(receipt)!["orderId"]!.GetValue<Guid>(), receipt);
    }

    private static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, Guid id, string expected) =>
        client.PostAsJsonAsync($"/api/orders/{id}/advance", new { expectedStatus = expected });

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [Test]
    public async Task Cancel_RepeatedAndReplayedSubmission_RemainsTerminalWithOriginalReceipt()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (key, id, receipt) = await CreateOrderAsync(client);
        for (var i = 0; i < 2; i++)
        {
            using var cancelled = await client.PostAsync($"/api/orders/{id}/cancel", null);
            Assert.That(cancelled.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await ReadAsync(cancelled))["status"]!.GetValue<string>(), Is.EqualTo("Cancelled"));
        }
        using var advance = await AdvanceAsync(client, id, "NotStarted");
        using var terminal = await AdvanceAsync(client, id, "Cancelled");
        using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
        Assert.That(advance.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(terminal.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = new StringContent(Input, Encoding.UTF8, "application/json")
        };
        replayRequest.Headers.Add("Idempotency-Key", key.ToString());
        using var replay = await client.SendAsync(replayRequest);
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(receipt));
        Assert.That(JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!["status"]!.GetValue<string>(),
            Is.EqualTo("Cancelled"));
        Assert.That(await app.CountOrdersAsync(), Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CancelVersusStart_ForcedOrdering_AllowsOnlyFirstAction(bool cancelFirst)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        using var first = cancelFirst ? await client.PostAsync($"/api/orders/{id}/cancel", null) :
            await AdvanceAsync(client, id, "NotStarted");
        using var second = cancelFirst ? await AdvanceAsync(client, id, "NotStarted") :
            await client.PostAsync($"/api/orders/{id}/cancel", null);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!["status"]!.GetValue<string>(),
            Is.EqualTo(cancelFirst ? "Cancelled" : "Started"));
    }

    [Test]
    public async Task CancelVersusStart_CoordinatedOverlap_StoresExactlyOneWinner()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Cancel() { await gate.Task; return await client.PostAsync($"/api/orders/{id}/cancel", null); }
        async Task<HttpResponseMessage> Start() { await gate.Task; return await AdvanceAsync(client, id, "NotStarted"); }
        var cancelTask = Cancel();
        var startTask = Start();
        gate.SetResult();
        var results = await Task.WhenAll(cancelTask, startTask);
        using var cancelled = results[0];
        using var started = results[1];
        Assert.That(results.Select(result => result.StatusCode),
            Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var expected = cancelled.IsSuccessStatusCode ? "Cancelled" : "Started";
        Assert.That(JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!["status"]!.GetValue<string>(), Is.EqualTo(expected));
    }

    [TestCase("{}")]
    [TestCase("""{"expectedStatus":null}""")]
    [TestCase("""{"expectedStatus":"started"}""")]
    [TestCase("""{"expectedStatus":"Unknown"}""")]
    [TestCase("""{"expectedStatus":0}""")]
    [TestCase("""{"expectedStatus":"0"}""")]
    public async Task Advance_InvalidExpectedStatus_RejectsWithoutAdvancing(string input)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        using var response = await client.PostAsync($"/api/orders/{id}/advance",
            new StringContent(input, Encoding.UTF8, "application/json"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!["status"]!.GetValue<string>(), Is.EqualTo("NotStarted"));
    }

    [Test]
    public async Task Mutate_MissingOrder_Returns404()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var id = Guid.NewGuid();
        using var cancel = await client.PostAsync($"/api/orders/{id}/cancel", null);
        using var advance = await AdvanceAsync(client, id, "NotStarted");
        using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
        Assert.That(cancel.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(advance.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UpdateVersusStart_ForcedOrdering_PreservesOnlyAnAllowedReplacement(bool updateFirst)
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        if (updateFirst)
        {
            using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        using var start = await AdvanceAsync(client, id, "NotStarted");
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        if (!updateFirst)
        {
            using var update = await client.PutAsync($"/api/orders/{id}", Json(Replacement));
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        }
        var current = JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!;
        Assert.That(current["status"]!.GetValue<string>(), Is.EqualTo("Started"));
        Assert.That(JsonNode.DeepEquals(current["order"], JsonNode.Parse(updateFirst ? Replacement : Input)), Is.True);
    }

    [Test]
    public async Task UpdateVersusStart_CoordinatedOverlap_CannotCommitAfterStart()
    {
        using var app = new ApiFixture();
        using var client = app.CreateClient();
        var (_, id, _) = await CreateOrderAsync(client);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Update() { await gate.Task; return await client.PutAsync($"/api/orders/{id}", Json(Replacement)); }
        async Task<HttpResponseMessage> Start() { await gate.Task; return await AdvanceAsync(client, id, "NotStarted"); }
        var updateTask = Update();
        var startTask = Start();
        gate.SetResult();
        var results = await Task.WhenAll(updateTask, startTask);
        using var updated = results[0];
        using var started = results[1];
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(updated.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.Conflict));
        var current = JsonNode.Parse(await client.GetStringAsync($"/api/orders/{id}"))!;
        Assert.That(current["status"]!.GetValue<string>(), Is.EqualTo("Started"));
        Assert.That(JsonNode.DeepEquals(current["order"],
            JsonNode.Parse(updated.IsSuccessStatusCode ? Replacement : Input)), Is.True);
    }
}
