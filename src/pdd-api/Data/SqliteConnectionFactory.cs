using Microsoft.Data.Sqlite;

namespace PDD.API.Data;

public sealed class SqliteConnectionFactory
{
    public const string ConnectionStringName = "Pdd";

    public SqliteConnectionFactory(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        IsInMemory = builder.Mode == SqliteOpenMode.Memory
            || builder.DataSource is "" or ":memory:";

        // Relative paths resolve against the API's content root, not the process
        // working directory, so `dotnet run`, the debugger and tests agree.
        if (!IsInMemory && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
        }

        DataSource = builder.DataSource;
        ConnectionString = builder.ToString();
    }

    public string ConnectionString { get; }

    public string DataSource { get; }

    public bool IsInMemory { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
