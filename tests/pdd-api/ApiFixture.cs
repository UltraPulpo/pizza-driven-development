using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PDD.API.Tests;

internal sealed class ApiFixture(string? databasePath = null) : WebApplicationFactory<Program>
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    public string DatabasePath { get; } = databasePath ??
        Path.Combine(RepositoryRoot, "data", $"pdd-test-{Guid.NewGuid():N}.db");
    public bool DeleteDatabaseOnDispose { get; init; } = true;
    public int DefaultTimeout { get; init; } = 30;
    public string ConnectionString => new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, DefaultTimeout = DefaultTimeout }.ToString();
    public TestLogProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pdd"] = ConnectionString
            }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        using var connection = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(connection);
        if (!DeleteDatabaseOnDispose)
            return;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(DatabasePath + suffix);
    }

    public async Task<long> CountOrdersAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Orders;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PDD.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Cannot find PDD.slnx.");
    }

    internal sealed class TestLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new TestLogger(this);
        public void Dispose() { }

        private sealed class TestLogger(TestLogProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                provider.Entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }
}
