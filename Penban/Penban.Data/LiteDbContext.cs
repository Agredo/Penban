using LiteDB;

namespace Penban.Data;

/// <summary>
/// Owns the single <see cref="LiteDatabase"/> instance used by the app. The database file
/// path is supplied by the caller (the Maui head project resolves the platform-appropriate
/// app data directory), keeping this project free of any platform dependency.
/// </summary>
/// <remarks>
/// The file is opened on first use rather than in the constructor, and it is given back on
/// <see cref="Suspend"/>. An open LiteDB holds a lock on the file for as long as it lives: under .NET
/// on Unix that is a <c>flock</c> taken by the handle of the stream the engine keeps open, and iOS
/// kills an app that is suspended while it holds one, reporting <c>0xdead10cc</c>. Closing on the way
/// out of the app and opening again on the next read is what keeps the app out of that rule - nothing
/// but the work itself ever sees the file open.
/// </remarks>
public sealed class LiteDbContext : IDisposable
{
    private readonly string databaseFilePath;
    private readonly Lock gate = new();

    /// <summary>The collections whose preparation has already run for the file that is open now.</summary>
    private readonly HashSet<string> prepared = [];

    private LiteDatabase? database;

    public LiteDbContext(string databaseFilePath) => this.databaseFilePath = databaseFilePath;

    /// <summary>
    /// The database, opened on first use. Callers want <see cref="Collection{T}"/> instead: what is
    /// taken from here belongs to the instance that is open now, and that one is disposed on
    /// <see cref="Suspend"/>.
    /// </summary>
    public LiteDatabase Database
    {
        get
        {
            lock (gate)
            {
                return database ??= new LiteDatabase(databaseFilePath);
            }
        }
    }

    /// <summary>
    /// The named collection, with <paramref name="prepare"/> run once for each time the file is opened.
    /// <para>
    /// Collections are asked for per call rather than kept in a field: an <see cref="ILiteCollection{T}"/>
    /// holds the database it came from, and that one is disposed by <see cref="Suspend"/>. Preparation
    /// (an index, say) runs once per opened file for the same reason - it belongs to opening the file,
    /// not to every single read and write.
    /// </para>
    /// </summary>
    public ILiteCollection<T> Collection<T>(string name, Action<ILiteCollection<T>>? prepare = null)
    {
        lock (gate)
        {
            var collection = (database ??= new LiteDatabase(databaseFilePath)).GetCollection<T>(name);

            if (prepare is not null && prepared.Add($"{typeof(T).FullName}:{name}"))
            {
                prepare(collection);
            }

            return collection;
        }
    }

    /// <summary>
    /// Gives the file back and holds every later call until <see cref="Resume"/>. Called when the app
    /// is left: the lock has to be gone before iOS suspends the process, and work that is still on its
    /// way - a note being read, a card being written - must not open it again afterwards.
    /// </summary>
    public void Suspend() => DatabaseWork.Suspend(this);

    /// <summary>Lets the held calls through again; the file opens itself on the next one.</summary>
    public void Resume() => DatabaseWork.Resume();

    public void Dispose() => Close();

    internal void Close()
    {
        lock (gate)
        {
            prepared.Clear();

            // Cleared before it is disposed: should the engine refuse to go, the next read opens a
            // fresh one rather than handing out a collection of a database that is on its way out.
            var open = database;
            database = null;
            open?.Dispose();
        }
    }
}
