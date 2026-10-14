using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Booking.Tests;

/// <summary>
/// Hosts the real application pipeline against a private SQLite database.
/// </summary>
/// <remarks>
/// A temporary file rather than <c>:memory:</c> on purpose. An in-memory database is shared by
/// one connection, and a <see cref="System.Data.Common.DbConnection"/> cannot run concurrent
/// commands — that would make the concurrency test measure the test harness instead of the
/// application. A file gives each request its own connection, exactly as it does in production,
/// and the unique path keeps tests isolated from each other.
/// </remarks>
public sealed class BookingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"booking-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Booking", $"Data Source={_databasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Pooled connections keep the file open on Windows; clear them before deleting.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}