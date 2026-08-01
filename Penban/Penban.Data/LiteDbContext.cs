using LiteDB;

namespace Penban.Data;

/// <summary>
/// Owns the single <see cref="LiteDatabase"/> instance used by the app. The database file
/// path is supplied by the caller (the Maui head project resolves the platform-appropriate
/// app data directory), keeping this project free of any platform dependency.
/// </summary>
public sealed class LiteDbContext : IDisposable
{
    public LiteDbContext(string databaseFilePath)
    {
        Database = new LiteDatabase(databaseFilePath);
    }

    public LiteDatabase Database { get; }

    public void Dispose() => Database.Dispose();
}
