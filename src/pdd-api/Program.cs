using Microsoft.AspNetCore.Diagnostics;
using PDD.API.Data;
using PDD.API.Models;
using PDD.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(await MenuCatalog.LoadAsync(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<IPromoRule, LovePizzaRule>();
builder.Services.AddSingleton<IPromoRule, Save5Rule>();
builder.Services.AddSingleton<PricingService>();
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
    SuppressDiagnosticsCallback = context => context.Exception is OrderException or BadHttpRequestException,
    ExceptionHandler = async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, detail) = error switch
        {
            OrderException { Kind: OrderError.Validation } => (400, error.Message),
            OrderException { Kind: OrderError.NotFound } => (404, error.Message),
            OrderException { Kind: OrderError.Conflict } => (409, error.Message),
            BadHttpRequestException badRequest => (badRequest.StatusCode,
                "Invalid request body. Send JSON with items, integer quantities, and tip mode/value as text."),
            _ => (500, "The service could not complete the request. Please retry or check the order status.")
        };
        await Results.Problem(statusCode: status, detail: detail).ExecuteAsync(context);
    }
});

app.MapGet("/", () => "Hello World!");
app.MapGet("/api/menu", (MenuCatalog catalog) => catalog.Menu);
app.MapPost("/api/quote", (OrderInput input, PricingService pricing) => pricing.Quote(input));

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
