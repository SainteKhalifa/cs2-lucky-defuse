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
        public override string ModuleName => "Lucky Defuse Plugin";
        public override string ModuleAuthor => "Jon-Mailes Graeffe <mail@jonni.it>";

        private readonly Color[] _colors =
        {
            Color.Red,
            Color.Green,
            Color.Blue,
            Color.Yellow
        };

        private readonly char[] _chatColors =
        {
            ChatColors.Red,
            ChatColors.Green,
            ChatColors.Blue,
            ChatColors.Yellow
        };

        // 🔑 CLÉS DE TRADUCTION DES COULEURS
        private readonly string[] _colorKeys =
        {
            "color_red",
            "color_green",
            "color_blue",
            "color_yellow"
        };

        private CCSPlayerController? _defuser;
        private CCSPlayerController? _planter;
        private WireMenu? _planterMenu;
        private WireMenu? _defuserMenu;
        private string[]? _menuOptions;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _notificationTimer;
        private int _wire;
        private bool _wireChosenManually;
        private bool _roundEnded;
        private bool _isPlanting;

        private string Prefix(string message)
        {
            return $" {ChatColors.Default}{message}";
        }

        public override void Load(bool hotReload)
        {
            var culture = Config.Language.ToLowerInvariant() == "fr" ? "fr-FR" : "en-US";
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            // 🟢 MENU AVEC COULEURS TRADUITES
            _menuOptions = new string[_colors.Length];
            for (int i = 0; i < _colors.Length; ++i)
            {
                var translatedColor = Localizer[_colorKeys[i]].Value;

                _menuOptions[i] =
                    $"<span color=\"{_colors[i].Name.ToLowerInvariant()}\">{i + 1}. {translatedColor}</span>";
            }

            _planterMenu = new(this, Localizer["planterMenuTitle"].Value, _menuOptions);
            _defuserMenu = new(this, Localizer["defuserMenuTitle"].Value, _menuOptions);

            RegisterEventHandler<EventRoundStart>((_, _) =>
            {
                _roundEnded = false;
                return HookResult.Continue;
            });

            RegisterEventHandler<EventRoundEnd>((_, _) =>
            {
                _planter = null;
                _defuser = null;
                _wireChosenManually = false;
                _isPlanting = false;
                _roundEnded = true;

                _notificationTimer?.Kill();
                _notificationTimer = null;

                _defuserMenu?.Close();
                _planterMenu?.Close();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventBombBeginplant>((@event, _) =>
            {
                if (_roundEnded || @event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _planter = @event.Userid;
                _isPlanting = true;
                _wireChosenManually = false;
                _wire = Random.Shared.Next(_colors.Length);

                _planterMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

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

            RegisterEventHandler<EventBombPlanted>((@event, _) =>
            {
                if (_roundEnded || @event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _isPlanting = false;

                _notificationTimer = AddTimer(
                    Config.NotificationDelay,
                    Notify,
                    TimerFlags.STOP_ON_MAPCHANGE
                );

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

            RegisterEventHandler<EventBombBegindefuse>((@event, _) =>
            {
                if (@event.Userid == null || !@event.Userid.IsValid)
                    return HookResult.Continue;

                _defuser = @event.Userid;
                _defuserMenu?.Open(@event.Userid);
                return HookResult.Continue;
            });

            RegisterEventHandler<EventBombAbortdefuse>((_, _) =>
            {
                _defuserMenu?.Close();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventBombExploded>((_, _) =>
            {
                _planter = null;
                _defuser = null;
                _defuserMenu?.Close();
                _planterMenu?.Close();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventBombDefused>((_, _) =>
            {
                _planter = null;
                _defuser = null;
                _defuserMenu?.Close();
                _planterMenu?.Close();
                return HookResult.Continue;
            });

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

            _defuserMenu!.OnOptionConfirmed += CutWire;

            AddCommand("ld_choose_wire", "choose a wire", (player, info) =>
            {
                if (player == null)
                {
                    Server.PrintToConsole("consoleNotAllowed");
                    return;
                }

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

        private void Notify()
        {
            if (_roundEnded || _planter == null || !_planter.IsValid)
                return;

            Server.PrintToChatAll(Prefix(Localizer["notification"].Value));
        }

        private void CutWire(int wire)
        {
            var bomb = Utilities
                .FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4")
                .FirstOrDefault();

            if (bomb == null || !bomb.IsValid || _defuser == null || !_defuser.IsValid)
            {
                Server.PrintToChatAll("Huh?");
                return;
            }

            if (_wire != wire)
            {
                bomb.C4Blow = 1f;
                Server.PrintToChatAll(
                    Prefix(Localizer["cutWrongWire"].Value
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{Localizer[_colorKeys[wire]].Value}"))
                );
            }
            else
            {
                bomb.DefuseCountDown = 0f;
                Server.PrintToChatAll(
                    Prefix(Localizer["cutCorrectWire"].Value
                        .Replace("{player}", _defuser.PlayerName)
                        .Replace("{wire}", $"{_chatColors[wire]}{Localizer[_colorKeys[wire]].Value}"))
                );
            }
        }
    }
}
