using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LuckyDefuse
{
    // Runs every SQLite call on a dedicated thread. A write can wait on the disk
    // (fsync) for 10-20 ms, and anything that blocks the game thread stalls the
    // whole server. Results come back through a queue that the game thread drains
    // on every tick, so CounterStrikeSharp natives are only ever called from there.
    public sealed class DatabaseWorker : IDisposable
    {
        private readonly Database _db;
        private readonly ILogger _logger;
        private readonly BlockingCollection<Action<Database>> _jobs = new();
        private readonly ConcurrentQueue<Action> _results = new();
        private readonly Thread _thread;

        // The database opens its connection on the first job, on this thread
        public DatabaseWorker(Database db, ILogger logger)
        {
            _logger = logger;
            _db = db;
            _thread = new Thread(Run) { IsBackground = true, Name = "LuckyDefuse DB" };
            _thread.Start();
        }

        // Queue a write; the game thread never waits for it
        public void Enqueue(Action<Database> job)
        {
            if (!_jobs.IsAddingCompleted)
                _jobs.Add(job);
        }

        // Run a query on the database thread and hand its result to the game thread
        public void Query<T>(Func<Database, T> query, Action<T> onResult)
        {
            Enqueue(db =>
            {
                var result = query(db);
                _results.Enqueue(() => onResult(result));
            });
        }

        // Deliver finished query results; called on the game thread every tick
        public void Pump()
        {
            while (_results.TryDequeue(out var callback))
            {
                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Lucky Defuse: failed to use database results");
                }
            }
        }

        private void Run()
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                try
                {
                    job(_db);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Lucky Defuse: database error");
                }
            }

            // Queue closed and drained: nothing else can use the database now
            _db.Dispose();
            _jobs.Dispose();
        }

        // Stop taking jobs; the database thread finishes the pending writes and
        // closes the database itself
        public void Dispose()
        {
            _jobs.CompleteAdding();

            // Pending writes take microseconds in WAL mode. Wait a little so they are
            // saved before a server shutdown, but never hold the game thread hostage
            // to a slow disk.
            if (!_thread.Join(TimeSpan.FromMilliseconds(500)))
                _logger.LogWarning("Lucky Defuse: database writes still pending on unload, finishing them in the background");
        }
    }
}
