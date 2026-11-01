using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LuckyDefuse
{
    public partial class LuckyDefuse : BasePlugin
    {
        // Plugin basic information
        public override string ModuleName => "Lucky Defuse Plugin";
        public override string ModuleAuthor => "SainteKhalifa fork of Jon-Mailes Graeffe <mail@jonni.it>";

        // List of colors used for wires (logic only)
        private readonly Color[] _colors =
        {
            Color.Red,
            Color.Green,
            Color.Blue,
            Color.Yellow
        };

        // Chat color codes matching wire colors
        private readonly char[] _chatColors =
        {
            ChatColors.Red,
            ChatColors.Green,
            ChatColors.Blue,
            ChatColors.Yellow
        };

        // Localization keys for wire colors
        private readonly string[] _colorKeys =
        {
            "color_red",
            "color_green",
            "color_blue",
            "color_yellow"
        };

        // Current CT defusing the bomb
        private CCSPlayerController? _defuser;

        // Terrorist who planted the bomb
        private CCSPlayerController? _planter;

        // Menus for planter and defuser
        private WireMenu? _planterMenu;
        private WireMenu? _defuserMenu;

        // Menu options text
        private string[]? _menuOptions;

        // Timer used for delayed notification
        private CounterStrikeSharp.API.Modules.Timers.Timer? _notificationTimer;

        // Index of the correct wire
        private int _wire;

        // Indicates if planter chose a wire manually
        private bool _wireChosenManually;

        // Indicates if the round has ended
        private bool _roundEnded;

        // Indicates if bomb is currently being planted
        private bool _isPlanting;

        // SQLite stats, only ever touched from their own thread
        private DatabaseWorker? _stats;

        // Indicates if the defuse was triggered by cutting the correct wire
        private bool _wasWireDefuse;

        // Indicates if the defuser cut a wrong wire (bomb blew up because of it)
        private bool _wrongWireCut;

        // A wire was already cut for this bomb: one try per bomb
        private bool _wireCut;

        // Timer used for auto-choosing a wire if planter doesn't pick one
        private CounterStrikeSharp.API.Modules.Timers.Timer? _autoWireTimer;

        // Rounds elapsed since the wire trends were last shown
        private int _roundsSinceWireTrends;

        // Translations loaded from lang/{language}.json, color tags already resolved
        private Dictionary<string, string> _translations = new();

        // Adds default chat prefix
        private string Prefix(string message)
        {
            return $" {ChatColors.Default}{message}";
        }

        // Look up a translation for the configured language
        private string T(string key)
        {
            return _translations.TryGetValue(key, out var value) ? value : key;
        }

        // Read the language file matching the config, independent of server culture
        private void LoadTranslations()
        {
            var lang = Config.Language.ToLowerInvariant() == "fr" ? "fr" : "en";
            var path = Path.Combine(ModuleDirectory, "lang", $"{lang}.json");
            if (!File.Exists(path))
                path = Path.Combine(ModuleDirectory, "lang", "en.json");

            _translations = new Dictionary<string, string>();
            if (!File.Exists(path))
                return;

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw == null)
                    return;

                foreach (var (key, value) in raw)
                    _translations[key] = ReplaceColorTags(value);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // Messages then show their keys instead of the whole plugin failing to load
                Logger.LogError(e, "Lucky Defuse: cannot read {Path}", path);
            }
        }

        // Replace {colorname} tags with chat color codes; data placeholders
        // like {wire} or {player} don't match any color and are kept as-is
        private static string ReplaceColorTags(string text)
        {
            return Regex.Replace(text, @"\{(\w+)\}", match =>
            {
                var name = match.Groups[1].Value;
                const BindingFlags flags =
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Static;
                var value = typeof(ChatColors).GetField(name, flags)?.GetValue(null)
                            ?? typeof(ChatColors).GetProperty(name, flags)?.GetValue(null);
                return value?.ToString() ?? match.Value;
            });
        }

        public override void Load(bool hotReload)
        {
            // Store the database next to the plugin config so it survives plugin updates
            var configDir = Path.GetFullPath(
                Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", "LuckyDefuse"));
            Directory.CreateDirectory(configDir);
            var dbPath = Path.Combine(configDir, "stats.db");

            // Migrate the database from the old location (plugin folder, wiped on updates)
            var oldDbPath = Path.Combine(ModuleDirectory, "stats.db");
            if (File.Exists(oldDbPath) && !File.Exists(dbPath))
                File.Move(oldDbPath, dbPath);

            // No database host: the local SQLite file, as before. Otherwise MySQL or MariaDB
            var database = string.IsNullOrWhiteSpace(Config.DatabaseHost)
                ? Database.Sqlite(dbPath)
                : Database.MySql(Config.DatabaseHost, Config.DatabasePort, Config.DatabaseName,
                    Config.DatabaseUser, Config.DatabasePassword);
            _stats = new DatabaseWorker(database, Logger);

            // Load translations for the configured language
            LoadTranslations();

            // Build menu options with translated color names
            _menuOptions = new string[_colors.Length];
            for (int i = 0; i < _colors.Length; ++i)
            {
                var translatedColor = T(_colorKeys[i]);
                _menuOptions[i] =
                    $"<span color=\"{_colors[i].Name.ToLowerInvariant()}\">{i + 1}. {translatedColor}</span>";
            }

            // Create menus
            _planterMenu = new(T("planterMenuTitle"), _menuOptions, T("menuFooter"));
            _defuserMenu = new(T("defuserMenuTitle"), _menuOptions, T("menuFooter"));

            // One listener for the whole plugin: hands database results back to the
            // game thread and drives whichever menu is open
            RegisterListener<Listeners.OnTick>(() =>
            {
                _stats?.Pump();
                _planterMenu?.Tick();
                _defuserMenu?.Tick();
            });

            // Round start reset
            RegisterEventHandler<EventRoundStart>((_, _) =>
            {
                _roundEnded = false;
                return HookResult.Continue;
            });

            // Round end cleanup
            RegisterEventHandler<EventRoundEnd>((_, _) =>
            {
                _planter = null;
                _defuser = null;
                _wireChosenManually = false;
                _isPlanting = false;
                _roundEnded = true;
                _wasWireDefuse = false;
                _wrongWireCut = false;
                _wireCut = false;

                // Kill pending timers
                _notificationTimer?.Kill();
                _notificationTimer = null;
                _autoWireTimer?.Kill();
                _autoWireTimer = null;

                _planterMenu?.Close();
                _defuserMenu?.Close();

                // Periodically show the server-wide wire trends between rounds
                if (Config.WireTrendsEveryRounds > 0 &&
                    ++_roundsSinceWireTrends >= Config.WireTrendsEveryRounds)
                {
                    _roundsSinceWireTrends = 0;
                    ShowWireTrends();
                }

                return HookResult.Continue;
            });

            // Bomb planting started
            RegisterEventHandler<EventBombBeginplant>((@event, _) =>
            {
                if (_roundEnded || @event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _planter = @event.Userid;
                _isPlanting = true;
                _wireChosenManually = false;

                // Preselect a random wire
                _wire = Random.Shared.Next(_colors.Length);

                // Bots can't read the menu and keep the random wire: left open, the
                // menu would turn their movement and jumps into a wire choice
                if (!@event.Userid.IsBot)
                    _planterMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

            // Bomb planting aborted
            RegisterEventHandler<EventBombAbortplant>((@event, _) =>
            {
                if (IsSamePlayer(@event.Userid, _planter))
                {
                    _planterMenu?.Close();
                    _isPlanting = false;
                    _wireChosenManually = false;
                    _planter = null;
                }
                return HookResult.Continue;
            });

            // Bomb planted successfully
            RegisterEventHandler<EventBombPlanted>((@event, _) =>
            {
                if (_roundEnded || @event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _isPlanting = false;
                _wireCut = false;

                // A wire picked while planting is final now: counting it here, once,
                // keeps re-picks and aborted plants out of the wire trends
                if (_wireChosenManually)
                    CountWireChosen(_wire);

                // Start delayed notification timer
                _notificationTimer?.Kill();
                _notificationTimer = AddTimer(
                    Config.NotificationDelay,
                    Notify,
                    TimerFlags.STOP_ON_MAPCHANGE
                );

                // Auto choose wire if planter did not choose
                _autoWireTimer?.Kill();
                _autoWireTimer = AddTimer(Config.PlanterMenuDuration, () =>
                {
                    if (!_wireChosenManually && _planter != null && _planter.IsValid)
                    {
                        _wire = Random.Shared.Next(_colors.Length);
                        _planterMenu?.Close();

                        _planter.PrintToChat(
                            T("randomWireChosen")
                                .Replace("{wire}", $"{_chatColors[_wire]}{T(_colorKeys[_wire])}")
                        );
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);

                return HookResult.Continue;
            });

            // CT starts defusing
            RegisterEventHandler<EventBombBegindefuse>((@event, _) =>
            {
                // One try per bomb: once a wire is cut, whoever cut it keeps the credit
                if (_wireCut || @event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _defuser = @event.Userid;

                // No menu for bots (see the planter menu)
                if (!@event.Userid.IsBot)
                    _defuserMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

            // CT aborts defuse
            RegisterEventHandler<EventBombAbortdefuse>((_, _) =>
            {
                _defuserMenu?.Close();

                // Wires can only be cut while defusing, ld_choose_wire included. Once
                // a wire is cut, keep the defuser: he is credited when the bomb goes off.
                if (!_wireCut)
                    _defuser = null;
                return HookResult.Continue;
            });

            // Bomb exploded
            RegisterEventHandler<EventBombExploded>((_, _) =>
            {
                SaveAndShowStats(bombDefused: false);
                _planter = null;
                _defuser = null;
                _planterMenu?.Close();
                _defuserMenu?.Close();
                return HookResult.Continue;
            });

            // Bomb defused normally
            RegisterEventHandler<EventBombDefused>((_, _) =>
            {
                // Kill all terrorists on normal defuse too
                if (Config.KillTerroristsOnDefuse)
                    KillAllTerrorists();

                SaveAndShowStats(bombDefused: true);
                _planter = null;
                _defuser = null;
                _planterMenu?.Close();
                _defuserMenu?.Close();
                return HookResult.Continue;
            });

            // Planter selects a wire
            _planterMenu.OnOptionConfirmed += option =>
            {
                if (_planter != null && TryChoosePlanterWire(_planter, option))
                    _planter.PrintToChat(WireChosenMessage(option));
            };

            // Defuser selects a wire
            _defuserMenu.OnOptionConfirmed += CutWire;

            // Console command to choose a wire
            AddCommand("ld_choose_wire", "choose a wire", (player, info) =>
            {
                if (player == null)
                    return;

                if (info.ArgCount < 2)
                {
                    info.ReplyToCommand(Prefix(T("missingArgument")));
                    return;
                }

                if (!int.TryParse(info.GetArg(1), out int option) ||
                    option <= 0 || option > _colors.Length)
                {
                    info.ReplyToCommand(Prefix(T("malformedArgument")));
                    return;
                }

                option--;

                if (IsSamePlayer(player, _defuser))
                {
                    CutWire(option);
                }
                else if (TryChoosePlanterWire(player, option))
                {
                    info.ReplyToCommand(WireChosenMessage(option));
                    _planterMenu?.Close();
                }
                else
                {
                    info.ReplyToCommand(Prefix(T("noBomb")));
                }
            });

            // Chat command !ldstats - show your own all-time stats
            AddCommand("css_ldstats", "show your Lucky Defuse stats", (player, info) =>
            {
                if (player == null || player.AuthorizedSteamID == null)
                    return;

                var steamId = player.AuthorizedSteamID.SteamId64.ToString();
                var name = player.PlayerName;
                var context = info.CallingContext;

                _stats?.Query(db => db.GetStats(steamId), stats =>
                {
                    if (stats == null)
                    {
                        Reply(player, context, Prefix(T("noStats")));
                        return;
                    }

                    Reply(player, context, Prefix(T("statsSelfHeader")));
                    Reply(player, context, Prefix(FormatDefuserLine(name, stats)));
                    Reply(player, context, Prefix(FormatPlanterLine(name, stats)));
                });
            });

            // Chat command !ldtop - show top 5 defusers
            AddCommand("css_ldtop", "show the top 5 Lucky Defuse defusers", (player, info) =>
            {
                if (player == null)
                    return;

                var context = info.CallingContext;

                _stats?.Query(db => db.GetTopDefusers(5), top =>
                {
                    if (top.Count == 0)
                    {
                        Reply(player, context, Prefix(T("noStats")));
                        return;
                    }

                    Reply(player, context, Prefix(T("topHeader")));
                    for (int i = 0; i < top.Count; ++i)
                    {
                        Reply(player, context, Prefix(T("topLine")
                            .Replace("{rank}", (i + 1).ToString())
                            .Replace("{player}", $"{ChatColors.Lime}{top[i].LastName}{ChatColors.Default}")
                            .Replace("{correct}", top[i].CorrectWires.ToString())
                            .Replace("{wrong}", top[i].WrongWires.ToString())
                            .Replace("{normal}", top[i].NormalDefuses.ToString())));
                    }
                });
            });
        }

        // Delayed notification for all players
        private void Notify()
        {
            if (_roundEnded || _planter == null || !_planter.IsValid)
                return;

            Server.PrintToChatAll(Prefix(T("notification")));
        }

        public override void Unload(bool hotReload)
        {
            _notificationTimer?.Kill();
            _autoWireTimer?.Kill();

            // Waits for pending writes, then closes the database
            _stats?.Dispose();
        }

        // Same connected player? Compares entity indexes: SteamIDs are null for
        // bots and for players Steam hasn't authenticated yet
        private static bool IsSamePlayer(CCSPlayerController? a, CCSPlayerController? b)
        {
            return a != null && b != null && a.IsValid && b.IsValid && a.Index == b.Index;
        }

        // Answer a command once its database result is back: the CommandInfo is only
        // valid while the command runs, so reply straight to the player instead
        private static void Reply(CCSPlayerController player, CommandCallingContext context, string message)
        {
            if (!player.IsValid)
                return;

            if (context == CommandCallingContext.Console)
                player.PrintToConsole(message);
            else
                player.PrintToChat(message);
        }

        // The planter picks the wire while planting, or once afterwards if he
        // hadn't picked one yet. Returns false when he can't choose now.
        private bool TryChoosePlanterWire(CCSPlayerController player, int option)
        {
            if (!IsSamePlayer(player, _planter) || !(_isPlanting || !_wireChosenManually))
                return false;

            _wire = option;
            _wireChosenManually = true;

            // Picked after the plant: final right away (otherwise counted in EventBombPlanted)
            if (!_isPlanting)
                CountWireChosen(option);
            return true;
        }

        private void CountWireChosen(int wire)
        {
            _stats?.Enqueue(db => db.IncrementWireChosen(wire));
        }

        private string WireChosenMessage(int wire)
        {
            return T("wireChosen").Replace("{wire}", $"{_chatColors[wire]}{T(_colorKeys[wire])}");
        }

        // Format the defuser stats line for chat
        private string FormatDefuserLine(string name, PlayerStats stats)
        {
            return T("statsDefuser")
                .Replace("{player}", $"{ChatColors.Lime}{name}{ChatColors.Default}")
                .Replace("{correct}", stats.CorrectWires.ToString())
                .Replace("{wrong}", stats.WrongWires.ToString())
                .Replace("{normal}", stats.NormalDefuses.ToString());
        }

        // Format the planter stats line for chat
        private string FormatPlanterLine(string name, PlayerStats stats)
        {
            return T("statsPlanter")
                .Replace("{player}", $"{ChatColors.Lime}{name}{ChatColors.Default}")
                .Replace("{planted}", stats.BombsPlanted.ToString())
                .Replace("{manual}", stats.WiresChosenManually.ToString())
                .Replace("{random}", stats.WiresChosenRandomly.ToString());
        }

        private void SaveAndShowStats(bool bombDefused)
        {
            if (_stats == null)
                return;

            // Read everything from the game objects now: the database answers a
            // few ticks later, once _planter and _defuser have been cleared
            string? defuserId = null, defuserName = null;
            string? planterId = null, planterName = null;
            var correctWire = bombDefused && _wasWireDefuse;
            var normalDefuse = bombDefused && !_wasWireDefuse;
            var manual = _wireChosenManually;

            // Only credit the defuser if he actually defused the bomb,
            // or actually cut a wrong wire (not just "was defusing when it blew up")
            if (_defuser != null && _defuser.IsValid &&
                _defuser.AuthorizedSteamID != null &&
                (bombDefused || _wrongWireCut))
            {
                defuserId = _defuser.AuthorizedSteamID.SteamId64.ToString();
                defuserName = _defuser.PlayerName;
            }

            if (_planter != null && _planter.IsValid &&
                _planter.AuthorizedSteamID != null)
            {
                planterId = _planter.AuthorizedSteamID.SteamId64.ToString();
                planterName = _planter.PlayerName;
            }

            if (defuserId == null && planterId == null)
                return;

            var showStats = Config.ShowRoundStats;
            _stats.Query(db =>
            {
                PlayerStats? defuserStats = null, planterStats = null;

                if (defuserId != null)
                {
                    db.UpdateDefuser(defuserId, defuserName!, correctWire, normalDefuse);
                    if (showStats)
                        defuserStats = db.GetStats(defuserId);
                }

                if (planterId != null)
                {
                    db.UpdatePlanter(planterId, planterName!, manual);
                    if (showStats)
                        planterStats = db.GetStats(planterId);
                }

                return (defuserStats, planterStats);
            }, result =>
            {
                var lines = new List<string>();
                if (result.defuserStats != null)
                    lines.Add(FormatDefuserLine(defuserName!, result.defuserStats));
                if (result.planterStats != null)
                    lines.Add(FormatPlanterLine(planterName!, result.planterStats));

                if (lines.Count == 0)
                    return;

                Server.PrintToChatAll(Prefix(T("statsRoundHeader")));
                foreach (var line in lines)
                    Server.PrintToChatAll(Prefix(line));
            });
        }

        // Called when a CT cuts a wire
        private void CutWire(int wire)
        {
            // One try per bomb: ld_choose_wire could otherwise cut wire after wire.
            // Cheap checks first, so the entity scan below only runs for a real cut.
            if (_wireCut || _defuser == null || !_defuser.IsValid)
                return;

            var bomb = Utilities
                .FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4")
                .FirstOrDefault();

            if (bomb == null || !bomb.IsValid)
                return;

            _wireCut = true;
            _defuserMenu?.Close();
            _stats?.Enqueue(db => db.IncrementWireCut(wire));

            // Wrong wire selected
            if (_wire != wire)
            {
                // Flag as real wire cut so stats count it as a wrong wire
                _wrongWireCut = true;
                bomb.C4Blow = 1f;

                Server.PrintToChatAll(
                    Prefix(T("cutWrongWire")
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{T(_colorKeys[wire])}"))
                );
            }
            // Correct wire selected
            else
            {
                // Instantly defuse the bomb
                bomb.DefuseCountDown = 0f;
                if (Config.KillTerroristsOnDefuse)
                    KillAllTerrorists();

                Server.PrintToChatAll(
                    Prefix(T("cutCorrectWire")
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{T(_colorKeys[wire])}"))
                );

                // Stats will be saved via EventBombDefused, flag as wire defuse
                _wasWireDefuse = true;
            }
        }

        // Build "Red 35% | Green 22% | ..." or null when nothing was counted yet
        private string? FormatWireDistribution(int[] counts)
        {
            var total = counts.Sum();
            if (total == 0)
                return null;

            var parts = new string[counts.Length];
            for (int i = 0; i < counts.Length; ++i)
            {
                var percent = (int)Math.Round(counts[i] * 100.0 / total);
                parts[i] = $"{_chatColors[i]}{T(_colorKeys[i])}{ChatColors.Default} {percent}%";
            }
            return string.Join(" | ", parts);
        }

        // Show which wires get picked and cut the most, across all players
        private void ShowWireTrends()
        {
            var colorCount = _colors.Length;
            _stats?.Query(db => db.GetWireTotals(colorCount), totals =>
            {
                var chosen = FormatWireDistribution(totals.Chosen);
                var cut = FormatWireDistribution(totals.Cut);

                if (chosen == null && cut == null)
                    return;

                Server.PrintToChatAll(Prefix(T("wireTrendsHeader")));
                if (chosen != null)
                    Server.PrintToChatAll(Prefix(T("wireTrendsChosen").Replace("{stats}", chosen)));
                if (cut != null)
                    Server.PrintToChatAll(Prefix(T("wireTrendsCut").Replace("{stats}", cut)));
            });
        }

        // Kill every terrorist still alive
        private static void KillAllTerrorists()
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid &&
                    player.Team == CsTeam.Terrorist &&
                    player.PawnIsAlive)
                {
                    // Kill player with explosion and force death
                    player.CommitSuicide(true, true);
                }
            }
        }
    }
}
