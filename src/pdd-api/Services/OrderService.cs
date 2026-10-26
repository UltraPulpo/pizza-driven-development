using System.Text.Json;
using Microsoft.Data.Sqlite;
using PDD.API.Data;
using PDD.API.Models;

namespace PDD.API.Services;

public sealed class OrderService(SqliteConnectionFactory connections, PricingService pricing)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmissionReceipt> SubmitAsync(Guid key, OrderInput input, CancellationToken cancellationToken = default)
    {
        var requestJson = JsonSerializer.Serialize(input, JsonOptions);
        await using var connection = await connections.OpenAsync(cancellationToken);
        var replay = await FindReplayAsync(connection, key, requestJson, cancellationToken);
        if (replay is not null)
            return replay;

        var quote = pricing.Quote(input);
        var receipt = new SubmissionReceipt(Guid.NewGuid(), quote);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Orders (id, submission_key, submission_request_json, submission_receipt_json, status, order_json)
            VALUES ($id, $key, $request, $receipt, 'NotStarted', $order)
            ON CONFLICT (submission_key) DO NOTHING
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$id", receipt.OrderId.ToString());
        command.Parameters.AddWithValue("$key", key.ToString());
        command.Parameters.AddWithValue("$request", requestJson);
        command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, JsonOptions));
        command.Parameters.AddWithValue("$order", JsonSerializer.Serialize(new OrderSnapshot(input, quote), JsonOptions));
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
            return receipt;

        return await FindReplayAsync(connection, key, requestJson, cancellationToken)
            ?? throw new InvalidOperationException("The competing submission could not be found.");
    }

    public async Task<OrderView> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, order_json FROM Orders WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new OrderException(OrderError.NotFound, "The order could not be found. Check the order ID.");
        var snapshot = ReadJson<OrderSnapshot>(reader.GetString(1));
        return new(id, Enum.Parse<OrderStatus>(reader.GetString(0)), snapshot.Order, snapshot.Quote);
    }

    private static async Task<SubmissionReceipt?> FindReplayAsync(
        SqliteConnection connection, Guid key, string requestJson, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT submission_request_json, submission_receipt_json
            FROM Orders WHERE submission_key = $key;
            """;
        command.Parameters.AddWithValue("$key", key.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        if (!string.Equals(reader.GetString(0), requestJson, StringComparison.Ordinal))
            throw new OrderException(OrderError.Conflict,
                "This submission key belongs to a different order attempt. Retry with the original input and key.");
        return ReadJson<SubmissionReceipt>(reader.GetString(1));
    }

    private static T ReadJson<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("The stored order data cannot be null.");

    private sealed record OrderSnapshot(OrderInput Order, PriceQuote Quote);
}
