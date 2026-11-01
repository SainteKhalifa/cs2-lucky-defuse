using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
namespace LuckyDefuse
{

    public class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("planter_menu_duration")]
        public int PlanterMenuDuration { get; set; } = 10;

        [JsonPropertyName("notification_delay")]
        public int NotificationDelay { get; set; } = 30;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "en";  // "en" ou "fr" (par défaut anglais)

        [JsonPropertyName("kill_terrorists_on_defuse")]
        public bool KillTerroristsOnDefuse { get; set; } = true;

        [JsonPropertyName("show_round_stats")]
        public bool ShowRoundStats { get; set; } = true;

        // Show the server-wide wire trends every N rounds (0 = never)
        [JsonPropertyName("wire_trends_every_rounds")]
        public int WireTrendsEveryRounds { get; set; } = 5;

        // Empty host: the stats stay in the local SQLite file (stats.db). With a host,
        // they go to that MySQL or MariaDB database, whose tables are created if needed
        [JsonPropertyName("database_host")]
        public string DatabaseHost { get; set; } = "";

        [JsonPropertyName("database_port")]
        public int DatabasePort { get; set; } = 3306;

        [JsonPropertyName("database_name")]
        public string DatabaseName { get; set; } = "";

        [JsonPropertyName("database_user")]
        public string DatabaseUser { get; set; } = "";

        [JsonPropertyName("database_password")]
        public string DatabasePassword { get; set; } = "";
    }

    public partial class LuckyDefuse : IPluginConfig<PluginConfig>
    {
        public PluginConfig Config { get; set; } = null!;

        public void OnConfigParsed(PluginConfig? config)
        {
            if (config == null)
            {
                return;
            }

            Config = config;
        }
    }
}