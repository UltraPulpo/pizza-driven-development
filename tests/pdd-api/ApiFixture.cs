using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace PDD.API.Tests;

internal sealed class ApiFixture : WebApplicationFactory<Program>
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    public string DatabasePath { get; } =
        Path.Combine(RepositoryRoot, "data", $"pdd-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Pdd"] = $"Data Source={DatabasePath}"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        SqliteConnection.ClearPool(connection);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(DatabasePath + suffix);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PDD.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Cannot find PDD.slnx.");
    }
}
