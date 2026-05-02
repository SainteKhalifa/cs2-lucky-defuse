using Microsoft.Data.Sqlite;

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

    public class Database : IDisposable
    {
        private readonly SqliteConnection _connection;

        public Database(string path)
        {
            _connection = new SqliteConnection($"Data Source={path}");
            _connection.Open();
            InitSchema();
        }

        private void InitSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS player_stats (
                    steam_id               TEXT PRIMARY KEY,
                    last_name              TEXT NOT NULL DEFAULT '',
                    correct_wires          INTEGER NOT NULL DEFAULT 0,
                    wrong_wires            INTEGER NOT NULL DEFAULT 0,
                    normal_defuses         INTEGER NOT NULL DEFAULT 0,
                    bombs_planted          INTEGER NOT NULL DEFAULT 0,
                    wires_chosen_manually  INTEGER NOT NULL DEFAULT 0,
                    wires_chosen_randomly  INTEGER NOT NULL DEFAULT 0
                )
                """;
            cmd.ExecuteNonQuery();
        }

        public void UpdateDefuser(string steamId, string name, bool correctWire, bool normalDefuse)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO player_stats (steam_id, last_name, correct_wires, wrong_wires, normal_defuses)
                VALUES (@steamId, @name,
                    @correct,
                    @wrong,
                    @normal)
                ON CONFLICT(steam_id) DO UPDATE SET
                    last_name      = @name,
                    correct_wires  = correct_wires  + @correct,
                    wrong_wires    = wrong_wires    + @wrong,
                    normal_defuses = normal_defuses + @normal
                """;
            cmd.Parameters.AddWithValue("@steamId", steamId);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@correct", correctWire && !normalDefuse ? 1 : 0);
            cmd.Parameters.AddWithValue("@wrong", !correctWire && !normalDefuse ? 1 : 0);
            cmd.Parameters.AddWithValue("@normal", normalDefuse ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void UpdatePlanter(string steamId, string name, bool manual)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO player_stats (steam_id, last_name, bombs_planted, wires_chosen_manually, wires_chosen_randomly)
                VALUES (@steamId, @name, 1, @manual, @random)
                ON CONFLICT(steam_id) DO UPDATE SET
                    last_name             = @name,
                    bombs_planted         = bombs_planted + 1,
                    wires_chosen_manually = wires_chosen_manually + @manual,
                    wires_chosen_randomly = wires_chosen_randomly + @random
                """;
            cmd.Parameters.AddWithValue("@steamId", steamId);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@manual", manual ? 1 : 0);
            cmd.Parameters.AddWithValue("@random", manual ? 0 : 1);
            cmd.ExecuteNonQuery();
        }

        public PlayerStats? GetStats(string steamId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM player_stats WHERE steam_id = @steamId";
            cmd.Parameters.AddWithValue("@steamId", steamId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new PlayerStats
            {
                LastName             = reader.GetString(1),
                CorrectWires         = reader.GetInt32(2),
                WrongWires           = reader.GetInt32(3),
                NormalDefuses        = reader.GetInt32(4),
                BombsPlanted         = reader.GetInt32(5),
                WiresChosenManually  = reader.GetInt32(6),
                WiresChosenRandomly  = reader.GetInt32(7)
            };
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
