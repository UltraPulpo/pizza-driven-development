using PDD.API.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp => new SqliteConnectionFactory(
    builder.Configuration.GetConnectionString(SqliteConnectionFactory.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{SqliteConnectionFactory.ConnectionStringName}' is not configured."),
    builder.Environment.ContentRootPath));
builder.Services.AddSingleton<DatabaseInitializer>();

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

app.MapGet("/", () => "Hello World!");

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
