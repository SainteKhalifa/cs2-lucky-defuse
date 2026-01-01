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

        // Adds default chat prefix
        private string Prefix(string message)
        {
            return $" {ChatColors.Default}{message}";
        }

        public override void Load(bool hotReload)
        {
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

                // Kill pending notification timer
                _notificationTimer?.Kill();
                _notificationTimer = null;

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
                AddTimer(5.0f, () =>
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
                _planter = null;
                _defuser = null;
                _planterMenu?.Close();
                _defuserMenu?.Close();
                return HookResult.Continue;
            });

            // Bomb defused normally
            RegisterEventHandler<EventBombDefused>((_, _) =>
            {
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

                // Kill all terrorists
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

                Server.PrintToChatAll(
                    Prefix(Localizer["cutCorrectWire"].Value
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{Localizer[_colorKeys[wire]].Value}"))
                );
            }
        }
    }
}
