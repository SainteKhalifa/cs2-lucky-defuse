using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace LuckyDefuse
{
    public class WireMenu
    {
        // Holding W/S repeats the move after this pause, then at this rate (64 ticks = 1 s)
        private const int RepeatDelayTicks = 16;
        private const int RepeatRateTicks = 8;

        public delegate void OptionConfirmedEventHandler(int option);
        public event OptionConfirmedEventHandler OnOptionConfirmed = static delegate { };

        // One pre-rendered page per highlighted line: nothing is built while the menu is shown
        private readonly string[] _pages;

        private CCSPlayerController? _player;
        private int _selectedLine;
        private int _repeatIn;
        private PlayerButtons _heldButtons;

        public WireMenu(string title, string[] options, string footer)
        {
            _pages = new string[options.Length];
            var html = new StringBuilder();
            for (int selected = 0; selected < options.Length; ++selected)
            {
                html.Clear().Append("<b>").Append(title).Append("</b><br>");
                for (int i = 0; i < options.Length; ++i)
                {
                    if (i == selected)
                        html.Append("▶ ");

                    html.Append(options[i]);
                    if (i == selected)
                        html.Append(" ◀");

                    html.Append("<br>");
                }
                // No blank line before and explicit <br> after: some CS2 builds
                // drop a trailing line that follows a double <br> or is unterminated
                html.Append(footer).Append("<br>");
                _pages[selected] = html.ToString();
            }
        }

        // Called by the plugin on every game tick
        public void Tick()
        {
            if (_player == null)
                return;

            // PrintToCenterHtml throws on an invalid player: a planter or defuser
            // who leaves would otherwise raise an exception every tick
            if (!_player.IsValid || _player.Connected != PlayerConnectedState.Connected)
            {
                Close();
                return;
            }

            var buttons = _player.Buttons;
            var pressed = buttons & ~_heldButtons;  // keys that went down on this tick
            _heldButtons = buttons;

            if ((pressed & PlayerButtons.Jump) != 0)
            {
                var option = _selectedLine;
                _player = null;
                OnOptionConfirmed.Invoke(option);
                return;
            }

            var step = (buttons & PlayerButtons.Forward) != 0 ? -1
                     : (buttons & PlayerButtons.Back) != 0 ? 1
                     : 0;
            if (step != 0)
            {
                // A press moves at once, so quick taps are never swallowed; holding
                // the key keeps scrolling after a short pause
                if ((pressed & (PlayerButtons.Forward | PlayerButtons.Back)) != 0)
                {
                    Move(step);
                    _repeatIn = RepeatDelayTicks;
                }
                else if (--_repeatIn <= 0)
                {
                    Move(step);
                    _repeatIn = RepeatRateTicks;
                }
            }

            // Sent on every tick like every CS2 center menu: resending it only now and
            // then makes the client replay the panel's opening animation (white flash).
            _player.PrintToCenterHtml(_pages[_selectedLine]);
        }

        private void Move(int step)
        {
            _selectedLine = (_selectedLine + step + _pages.Length) % _pages.Length;
        }

        public void Open(CCSPlayerController player)
        {
            _player = player;
            // Keys already down when the menu opens must be released first: SPACE held
            // at that moment must not pick the highlighted wire by itself
            _heldButtons = PlayerButtons.Jump | PlayerButtons.Forward | PlayerButtons.Back;
            _repeatIn = RepeatDelayTicks;
        }

        public void Close()
        {
            _player = null;
            _selectedLine = 0;
        }
    }
}
