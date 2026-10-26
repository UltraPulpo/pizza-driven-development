using System.Text.Json;
using Microsoft.Data.Sqlite;
using PDD.API.Data;
using PDD.API.Models;

namespace PDD.API.Services;

public sealed class OrderService(SqliteConnectionFactory connections, PricingService pricing)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

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
        return await FindByIdAsync(connection, id, cancellationToken);
    }

    public async Task<OrderView> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders SET status = 'Cancelled'
            WHERE id = $id AND status = 'NotStarted'
            RETURNING status, order_json;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        var changed = await ReadViewAsync(command, id, cancellationToken);
        if (changed is not null)
            return changed;
        var current = await FindByIdAsync(connection, id, cancellationToken);
        return current.Status == OrderStatus.Cancelled ? current :
            throw new OrderException(OrderError.Conflict, "Cancellation is allowed only while the order is Not Started.");
    }

    public async Task<OrderView> UpdateAsync(Guid id, OrderInput input, CancellationToken cancellationToken = default)
    {
        var quote = pricing.Quote(input);
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders SET order_json = $order
            WHERE id = $id AND status = 'NotStarted'
            RETURNING status, order_json;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$order", JsonSerializer.Serialize(new OrderSnapshot(input, quote), JsonOptions));
        var changed = await ReadViewAsync(command, id, cancellationToken);
        if (changed is not null)
            return changed;
        await FindByIdAsync(connection, id, cancellationToken);
        throw new OrderException(OrderError.Conflict, "Changes are allowed only while the order is Not Started.");
    }

    public async Task<OrderView> AdvanceAsync(Guid id, OrderStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        var next = expectedStatus switch
        {
            OrderStatus.NotStarted => OrderStatus.Started,
            OrderStatus.Started => OrderStatus.ReadyForPickup,
            OrderStatus.ReadyForPickup => OrderStatus.Delivered,
            _ => throw new OrderException(OrderError.Conflict, "Delivered and Cancelled orders cannot advance.")
        };
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Orders SET status = $next
            WHERE id = $id AND status = $expected
            RETURNING status, order_json;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$expected", expectedStatus.ToString());
        command.Parameters.AddWithValue("$next", next.ToString());
        var changed = await ReadViewAsync(command, id, cancellationToken);
        if (changed is not null)
            return changed;
        var current = await FindByIdAsync(connection, id, cancellationToken);
        throw new OrderException(OrderError.Conflict,
            $"The order is {current.Status}, not {expectedStatus}. Check its current status before advancing.");
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
        JsonSerializer.Deserialize<T>(json, StoredJsonOptions)
        ?? throw new InvalidOperationException("The stored order data cannot be null.");

    private static async Task<OrderView> FindByIdAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, order_json FROM Orders WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await ReadViewAsync(command, id, cancellationToken)
            ?? throw new OrderException(OrderError.NotFound, "The order could not be found. Check the order ID.");
    }

    private static async Task<OrderView?> ReadViewAsync(SqliteCommand command, Guid id, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var snapshot = ReadJson<OrderSnapshot>(reader.GetString(1));
        return new(id, Enum.Parse<OrderStatus>(reader.GetString(0)), snapshot.Order, snapshot.Quote);
    }

    private sealed record OrderSnapshot(OrderInput Order, PriceQuote Quote);
}
