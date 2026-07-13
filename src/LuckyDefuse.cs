using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.Globalization;

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

        // SQLite database for persistent stats
        private Database? _db;

        // Indicates if the defuse was triggered by cutting the correct wire
        private bool _wasWireDefuse;

        // Indicates if the defuser cut a wrong wire (bomb blew up because of it)
        private bool _wrongWireCut;

        // Timer used for auto-choosing a wire if planter doesn't pick one
        private CounterStrikeSharp.API.Modules.Timers.Timer? _autoWireTimer;

        // Adds default chat prefix
        private string Prefix(string message)
        {
            return $" {ChatColors.Default}{message}";
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

            _db = new Database(dbPath);

            // Set culture based on config language
            var culture = Config.Language.ToLowerInvariant() == "fr" ? "fr-FR" : "en-US";
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            // Build menu options with translated color names
            _menuOptions = new string[_colors.Length];
            for (int i = 0; i < _colors.Length; ++i)
            {
                var translatedColor = Localizer[_colorKeys[i]].Value;
                _menuOptions[i] =
                    $"<span color=\"{_colors[i].Name.ToLowerInvariant()}\">{i + 1}. {translatedColor}</span>";
            }

            // Create menus
            _planterMenu = new(this, Localizer["planterMenuTitle"].Value, _menuOptions);
            _defuserMenu = new(this, Localizer["defuserMenuTitle"].Value, _menuOptions);

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

                // Kill pending timers
                _notificationTimer?.Kill();
                _notificationTimer = null;
                _autoWireTimer?.Kill();
                _autoWireTimer = null;

                _planterMenu?.Close();
                _defuserMenu?.Close();
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

                _planterMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

            // Bomb planting aborted
            RegisterEventHandler<EventBombAbortplant>((@event, _) =>
            {
                if (_planter != null && @event.Userid != null &&
                    @event.Userid.AuthorizedSteamID == _planter.AuthorizedSteamID)
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

                // Start delayed notification timer
                _notificationTimer = AddTimer(
                    Config.NotificationDelay,
                    Notify,
                    TimerFlags.STOP_ON_MAPCHANGE
                );

                // Auto choose wire if planter did not choose
                _autoWireTimer = AddTimer(Config.PlanterMenuDuration, () =>
                {
                    if (!_wireChosenManually && _planter != null && _planter.IsValid)
                    {
                        _wire = Random.Shared.Next(_colors.Length);
                        _planterMenu?.Close();

                        _planter.PrintToChat(
                            Localizer["randomWireChosen"].Value
                                .Replace("{wire}", $"{_chatColors[_wire]}{Localizer[_colorKeys[_wire]].Value}")
                        );
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);

                return HookResult.Continue;
            });

            // CT starts defusing
            RegisterEventHandler<EventBombBegindefuse>((@event, _) =>
            {
                if (@event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _defuser = @event.Userid;
                _defuserMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

            // CT aborts defuse
            RegisterEventHandler<EventBombAbortdefuse>((_, _) =>
            {
                _defuserMenu?.Close();
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
            _planterMenu!.OnOptionConfirmed += option =>
            {
                if (_planter != null && (_isPlanting || !_wireChosenManually))
                {
                    _wire = option;
                    _wireChosenManually = true;

                    _planter.PrintToChat(
                        Localizer["wireChosen"].Value
                            .Replace("{wire}", $"{_chatColors[option]}{Localizer[_colorKeys[option]].Value}")
                    );
                }
            };

            // Defuser selects a wire
            _defuserMenu!.OnOptionConfirmed += CutWire;

            // Console command to choose a wire
            AddCommand("ld_choose_wire", "choose a wire", (player, info) =>
            {
                if (player == null)
                    return;

                if (info.ArgCount < 2)
                {
                    info.ReplyToCommand(Prefix(Localizer["missingArgument"].Value));
                    return;
                }

                if (!int.TryParse(info.GetArg(1), out int option) ||
                    option <= 0 || option > _colors.Length)
                {
                    info.ReplyToCommand(Prefix(Localizer["malformedArgument"].Value));
                    return;
                }

                option--;

                if (_defuser != null && _defuser.IsValid &&
                    player.AuthorizedSteamID == _defuser.AuthorizedSteamID)
                {
                    CutWire(option);
                }
                else if (_planter != null &&
                         player.AuthorizedSteamID == _planter.AuthorizedSteamID &&
                         (_isPlanting || !_wireChosenManually))
                {
                    _wire = option;
                    _wireChosenManually = true;

                    info.ReplyToCommand(
                        Localizer["wireChosen"].Value
                            .Replace("{wire}", $"{_chatColors[option]}{Localizer[_colorKeys[option]].Value}")
                    );

                    _planterMenu?.Close();
                }
                else
                {
                    info.ReplyToCommand(Prefix(Localizer["noBomb"].Value));
                }
            });

            // Chat command !ldstats - show your own all-time stats
            AddCommand("css_ldstats", "show your Lucky Defuse stats", (player, info) =>
            {
                if (player == null || player.AuthorizedSteamID == null)
                    return;

                var stats = _db?.GetStats(player.AuthorizedSteamID.SteamId64.ToString());
                if (stats == null)
                {
                    info.ReplyToCommand(Prefix(Localizer["noStats"].Value));
                    return;
                }

                info.ReplyToCommand(Prefix(Localizer["statsSelfHeader"].Value));
                info.ReplyToCommand(Prefix(FormatDefuserLine(player.PlayerName, stats)));
                info.ReplyToCommand(Prefix(FormatPlanterLine(player.PlayerName, stats)));
            });

            // Chat command !ldtop - show top 5 defusers
            AddCommand("css_ldtop", "show the top 5 Lucky Defuse defusers", (player, info) =>
            {
                if (player == null)
                    return;

                var top = _db?.GetTopDefusers(5);
                if (top == null || top.Count == 0)
                {
                    info.ReplyToCommand(Prefix(Localizer["noStats"].Value));
                    return;
                }

                info.ReplyToCommand(Prefix(Localizer["topHeader"].Value));
                for (int i = 0; i < top.Count; ++i)
                {
                    info.ReplyToCommand(Prefix(Localizer["topLine"].Value
                        .Replace("{rank}", (i + 1).ToString())
                        .Replace("{player}", $"{ChatColors.Lime}{top[i].LastName}{ChatColors.Default}")
                        .Replace("{correct}", top[i].CorrectWires.ToString())
                        .Replace("{wrong}", top[i].WrongWires.ToString())
                        .Replace("{normal}", top[i].NormalDefuses.ToString())));
                }
            });

            _planterMenu.Load();
            _defuserMenu.Load();
        }

        // Delayed notification for all players
        private void Notify()
        {
            if (_roundEnded || _planter == null || !_planter.IsValid)
                return;

            Server.PrintToChatAll(Prefix(Localizer["notification"].Value));
        }

        public override void Unload(bool hotReload)
        {
            _db?.Dispose();
        }

        // Format the defuser stats line for chat
        private string FormatDefuserLine(string name, PlayerStats stats)
        {
            return Localizer["statsDefuser"].Value
                .Replace("{player}", $"{ChatColors.Lime}{name}{ChatColors.Default}")
                .Replace("{correct}", stats.CorrectWires.ToString())
                .Replace("{wrong}", stats.WrongWires.ToString())
                .Replace("{normal}", stats.NormalDefuses.ToString());
        }

        // Format the planter stats line for chat
        private string FormatPlanterLine(string name, PlayerStats stats)
        {
            return Localizer["statsPlanter"].Value
                .Replace("{player}", $"{ChatColors.Lime}{name}{ChatColors.Default}")
                .Replace("{planted}", stats.BombsPlanted.ToString())
                .Replace("{manual}", stats.WiresChosenManually.ToString())
                .Replace("{random}", stats.WiresChosenRandomly.ToString());
        }

        private void SaveAndShowStats(bool bombDefused)
        {
            var lines = new List<string>();

            // Only credit the defuser if he actually defused the bomb,
            // or actually cut a wrong wire (not just "was defusing when it blew up")
            if (_defuser != null && _defuser.IsValid &&
                _defuser.AuthorizedSteamID != null &&
                (bombDefused || _wrongWireCut))
            {
                var steamId = _defuser.AuthorizedSteamID.SteamId64.ToString();
                var name = _defuser.PlayerName;

                _db?.UpdateDefuser(steamId, name,
                    correctWire: bombDefused && _wasWireDefuse,
                    normalDefuse: bombDefused && !_wasWireDefuse);

                var stats = _db?.GetStats(steamId);
                if (stats != null)
                    lines.Add(FormatDefuserLine(name, stats));
            }

            if (_planter != null && _planter.IsValid &&
                _planter.AuthorizedSteamID != null)
            {
                var steamId = _planter.AuthorizedSteamID.SteamId64.ToString();
                var name = _planter.PlayerName;

                _db?.UpdatePlanter(steamId, name, _wireChosenManually);

                var stats = _db?.GetStats(steamId);
                if (stats != null)
                    lines.Add(FormatPlanterLine(name, stats));
            }

            if (!Config.ShowRoundStats || lines.Count == 0) return;

            Server.PrintToChatAll(Prefix(Localizer["statsRoundHeader"].Value));
            foreach (var line in lines)
                Server.PrintToChatAll(Prefix(line));
        }

        // Called when a CT cuts a wire
        private void CutWire(int wire)
        {
            var bomb = Utilities
                .FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4")
                .FirstOrDefault();

            if (bomb == null || !bomb.IsValid || _defuser == null || !_defuser.IsValid)
                return;

            // Wrong wire selected
            if (_wire != wire)
            {
                // Flag as real wire cut so stats count it as a wrong wire
                _wrongWireCut = true;
                bomb.C4Blow = 1f;

                Server.PrintToChatAll(
                    Prefix(Localizer["cutWrongWire"].Value
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{Localizer[_colorKeys[wire]].Value}"))
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
                    Prefix(Localizer["cutCorrectWire"].Value
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{Localizer[_colorKeys[wire]].Value}"))
                );

                // Stats will be saved via EventBombDefused, flag as wire defuse
                _wasWireDefuse = true;
            }
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
