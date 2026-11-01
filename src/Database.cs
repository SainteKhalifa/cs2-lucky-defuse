using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace LuckyDefuse
{
    public class PlayerStats
    {
        public string LastName { get; set; } = "";
        public int CorrectWires { get; set; }
        public int WrongWires { get; set; }
        public int NormalDefuses { get; set; }
        public int BombsPlanted { get; set; }
        public int WiresChosenManually { get; set; }
        public int WiresChosenRandomly { get; set; }
    }

    // The stats store: the local SQLite file, or a MySQL/MariaDB database when the
    // config gives a host. Same tables and queries on both; only the database
    // thread (DatabaseWorker) uses it, so the connection, opened on the first
    // query, never blocks the game thread.
    public class Database : IDisposable
    {
        private const int NameLength = 128;

        private readonly Func<DbConnection> _create;
        private readonly bool _mysql;
        private DbConnection? _connection;

        private Database(Func<DbConnection> create, bool mysql)
        {
            _create = create;
            _mysql = mysql;
        }

        public static Database Sqlite(string path) =>
            new(() => new SqliteConnection($"Data Source={path}"), mysql: false);

        public static Database MySql(string host, int port, string database, string user, string password) =>
            new(() => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = (uint)port,
                Database = database,
                UserID = user,
                Password = password,
                ConnectionTimeout = 5,
            }.ConnectionString), mysql: true);

        // The open connection. A failed command drops it (the database may have
        // restarted): the next command opens a fresh one.
        private DbConnection Connection
        {
            get
            {
                if (_connection is { State: ConnectionState.Open }) return _connection;
                _connection?.Dispose();
                _connection = null;
                var connection = _create();
                connection.Open();
                if (!_mysql)
                {
                    // WAL + synchronous=NORMAL: a commit no longer waits for the disk
                    // (measured on a game server: 12 ms -> 0.02 ms per write). A power cut
                    // can lose the last writes, but never corrupts the file.
                    Run(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
                }
                InitSchema(connection);
                _connection = connection;
                return connection;
            }
        }

        private static void Run(DbConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private void InitSchema(DbConnection connection)
        {
            // Server-wide counters, one row per wire color, are kept apart from
            // player_stats so reading the trends stays a 4-row lookup instead of an
            // aggregation over every player
            var sql = _mysql
                ? """
                  CREATE TABLE IF NOT EXISTS player_stats (
                      steam_id               BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                      last_name              VARCHAR(128) NOT NULL DEFAULT '',
                      correct_wires          INT NOT NULL DEFAULT 0,
                      wrong_wires            INT NOT NULL DEFAULT 0,
                      normal_defuses         INT NOT NULL DEFAULT 0,
                      bombs_planted          INT NOT NULL DEFAULT 0,
                      wires_chosen_manually  INT NOT NULL DEFAULT 0,
                      wires_chosen_randomly  INT NOT NULL DEFAULT 0
                  ) DEFAULT CHARSET = utf8mb4;

                  CREATE TABLE IF NOT EXISTS wire_stats (
                      color   INT NOT NULL PRIMARY KEY,
                      chosen  INT NOT NULL DEFAULT 0,
                      cut     INT NOT NULL DEFAULT 0
                  );
                  """
                : """
                  CREATE TABLE IF NOT EXISTS player_stats (
                      steam_id               TEXT PRIMARY KEY,
                      last_name              TEXT NOT NULL DEFAULT '',
                      correct_wires          INTEGER NOT NULL DEFAULT 0,
                      wrong_wires            INTEGER NOT NULL DEFAULT 0,
                      normal_defuses         INTEGER NOT NULL DEFAULT 0,
                      bombs_planted          INTEGER NOT NULL DEFAULT 0,
                      wires_chosen_manually  INTEGER NOT NULL DEFAULT 0,
                      wires_chosen_randomly  INTEGER NOT NULL DEFAULT 0
                  );

                  CREATE TABLE IF NOT EXISTS wire_stats (
                      color   INTEGER PRIMARY KEY,
                      chosen  INTEGER NOT NULL DEFAULT 0,
                      cut     INTEGER NOT NULL DEFAULT 0
                  );
                  """;
            try
            {
                Run(connection, sql);
            }
            catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.TableAccessDenied)
            {
                // An account without CREATE: the tables were created by an admin
            }
        }

        // The upsert, spelled differently by the two databases; the SET list follows
        private string OnConflict(string key) => _mysql ? "ON DUPLICATE KEY UPDATE" : $"ON CONFLICT({key}) DO UPDATE SET";

        private static string Clip(string name) => name.Length > NameLength ? name[..NameLength] : name;

        private T Use<T>(string sql, (string Name, object Value)[] parameters, Func<DbCommand, T> run)
        {
            try
            {
                using var cmd = Connection.CreateCommand();
                cmd.CommandText = sql;
                foreach (var (name, value) in parameters)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value;
                    cmd.Parameters.Add(parameter);
                }
                return run(cmd);
            }
            catch
            {
                _connection?.Dispose();
                _connection = null;
                throw;
            }
        }

        private void Execute(string sql, params (string Name, object Value)[] parameters) =>
            Use(sql, parameters, cmd => cmd.ExecuteNonQuery());

        // Count a wire deliberately picked by a planter.
        // Rows are created on demand, so no seeding is needed.
        public void IncrementWireChosen(int color) =>
            Execute($"INSERT INTO wire_stats (color, chosen) VALUES (@color, 1) {OnConflict("color")} chosen = chosen + 1",
                ("@color", color));

        // Count a wire cut by a defuser, right or wrong
        public void IncrementWireCut(int color) =>
            Execute($"INSERT INTO wire_stats (color, cut) VALUES (@color, 1) {OnConflict("color")} cut = cut + 1",
                ("@color", color));

        // Totals indexed by color; colors never counted yet stay at 0
        public (int[] Chosen, int[] Cut) GetWireTotals(int colorCount)
        {
            var chosen = new int[colorCount];
            var cut = new int[colorCount];
            Use("SELECT color, chosen, cut FROM wire_stats", [], cmd =>
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var color = reader.GetInt32(0);
                    if (color < 0 || color >= colorCount) continue;
                    chosen[color] = reader.GetInt32(1);
                    cut[color] = reader.GetInt32(2);
                }
                return 0;
            });
            return (chosen, cut);
        }

        public void UpdateDefuser(string steamId, string name, bool correctWire, bool normalDefuse) =>
            Execute($"""
                INSERT INTO player_stats (steam_id, last_name, correct_wires, wrong_wires, normal_defuses)
                VALUES (@steamId, @name, @correct, @wrong, @normal)
                {OnConflict("steam_id")}
                    last_name      = @name,
                    correct_wires  = correct_wires  + @correct,
                    wrong_wires    = wrong_wires    + @wrong,
                    normal_defuses = normal_defuses + @normal
                """,
                ("@steamId", steamId), ("@name", Clip(name)),
                ("@correct", correctWire && !normalDefuse ? 1 : 0),
                ("@wrong", !correctWire && !normalDefuse ? 1 : 0),
                ("@normal", normalDefuse ? 1 : 0));

        public void UpdatePlanter(string steamId, string name, bool manual) =>
            Execute($"""
                INSERT INTO player_stats (steam_id, last_name, bombs_planted, wires_chosen_manually, wires_chosen_randomly)
                VALUES (@steamId, @name, 1, @manual, @random)
                {OnConflict("steam_id")}
                    last_name             = @name,
                    bombs_planted         = bombs_planted + 1,
                    wires_chosen_manually = wires_chosen_manually + @manual,
                    wires_chosen_randomly = wires_chosen_randomly + @random
                """,
                ("@steamId", steamId), ("@name", Clip(name)), ("@manual", manual ? 1 : 0), ("@random", manual ? 0 : 1));

        private const string StatsColumns =
            "last_name, correct_wires, wrong_wires, normal_defuses, bombs_planted, wires_chosen_manually, wires_chosen_randomly";

        private static PlayerStats ReadStats(DbDataReader reader)
        {
            return new PlayerStats
            {
                LastName             = reader.GetString(0),
                CorrectWires         = reader.GetInt32(1),
                WrongWires           = reader.GetInt32(2),
                NormalDefuses        = reader.GetInt32(3),
                BombsPlanted         = reader.GetInt32(4),
                WiresChosenManually  = reader.GetInt32(5),
                WiresChosenRandomly  = reader.GetInt32(6)
            };
        }

        public PlayerStats? GetStats(string steamId) =>
            Use($"SELECT {StatsColumns} FROM player_stats WHERE steam_id = @steamId", [("@steamId", steamId)], cmd =>
            {
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadStats(reader) : null;
            });

        public List<PlayerStats> GetTopDefusers(int limit) =>
            Use($"""
                SELECT {StatsColumns} FROM player_stats
                WHERE correct_wires + wrong_wires + normal_defuses > 0
                ORDER BY correct_wires DESC, wrong_wires ASC
                LIMIT @limit
                """, [("@limit", limit)], cmd =>
            {
                var result = new List<PlayerStats>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(ReadStats(reader));
                }
                return result;
            });

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
