using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using PDD.API.Data;
using PDD.API.Models;
using PDD.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(await MenuCatalog.LoadAsync(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<IPromoRule, LovePizzaRule>();
builder.Services.AddSingleton<IPromoRule, Save5Rule>();
builder.Services.AddSingleton<PricingService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddSingleton(sp => new SqliteConnectionFactory(
    builder.Configuration.GetConnectionString(SqliteConnectionFactory.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{SqliteConnectionFactory.ConnectionStringName}' is not configured."),
    builder.Environment.ContentRootPath));
builder.Services.AddSingleton<DatabaseInitializer>();

var app = builder.Build();

app.Services.GetRequiredService<PricingService>();
await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = _ => true,
    ExceptionHandler = async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (error is SqliteException { SqliteErrorCode: 5 or 6 })
            app.Logger.LogWarning(error, "SQLite busy during {Method} {Path}", context.Request.Method, context.Request.Path);
        else if (error is not (OrderException or BadHttpRequestException))
            app.Logger.LogError(error, "Order service failed during {Method} {Path}", context.Request.Method, context.Request.Path);
        var (status, detail) = error switch
        {
            OrderException { Kind: OrderError.Validation } => (400, error.Message),
            OrderException { Kind: OrderError.NotFound } => (404, error.Message),
            OrderException { Kind: OrderError.Conflict } => (409, error.Message),
            BadHttpRequestException badRequest => (badRequest.StatusCode,
                "Invalid request body. Send JSON with items, integer quantities, and tip mode/value as text."),
            SqliteException { SqliteErrorCode: 5 or 6 } => (503,
                "The order service is busy. The outcome may be unknown; retry the same submission or check the order status."),
            _ => (500, "The service could not complete the request. Please retry or check the order status.")
        };
        await Results.Problem(statusCode: status, detail: detail).ExecuteAsync(context);
    }
});

app.MapGet("/", () => "Hello World!");
app.MapGet("/api/menu", (MenuCatalog catalog) => catalog.Menu);
app.MapPost("/api/quote", (OrderInput input, PricingService pricing) => pricing.Quote(input));
app.MapPost("/api/orders", async (HttpRequest request, OrderInput input, OrderService orders, CancellationToken cancellationToken) =>
{
    var header = request.Headers["Idempotency-Key"];
    if (header.Count != 1 || !Guid.TryParse(header[0], out var key))
        throw new OrderException(OrderError.Validation, "Provide one GUID Idempotency-Key header for the submission attempt.");
    var receipt = await orders.SubmitAsync(key, input, cancellationToken);
    return Results.Created($"/api/orders/{receipt.OrderId}", receipt);
});
app.MapGet("/api/orders/{id}", async (string id, OrderService orders, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(id, out var orderId))
        throw new OrderException(OrderError.Validation, "The order ID must be a GUID.");
    return await orders.GetAsync(orderId, cancellationToken);
});

app.MapGet("/api/health", async (SqliteConnectionFactory db, CancellationToken cancellationToken) =>
{
    await using var connection = await db.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT sqlite_version();";
    var sqliteVersion = (string?)await command.ExecuteScalarAsync(cancellationToken);
    return Results.Ok(new { status = "ok", sqliteVersion });
});

app.Run();

public partial class Program;
