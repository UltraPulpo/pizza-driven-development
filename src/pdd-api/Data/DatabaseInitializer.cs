namespace PDD.API.Data;

public sealed class DatabaseInitializer(SqliteConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger)
{
    private const string SchemaResourceName = "PDD.API.Data.schema.sql";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsInMemory)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(connectionFactory.DataSource)!);
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            await pragma.ExecuteScalarAsync(cancellationToken);
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = await ReadSchemaAsync(cancellationToken);
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("SQLite database ready at {DataSource}", connectionFactory.DataSource);
    }

    private static async Task<string> ReadSchemaAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{SchemaResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
