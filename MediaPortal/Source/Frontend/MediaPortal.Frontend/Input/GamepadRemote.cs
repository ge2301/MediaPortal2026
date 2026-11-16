/*
    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MediaPortal.Frontend.Gamepads;
using MediaPortal.Frontend.Settings;

namespace MediaPortal.Frontend.Input
{
  /// <summary>
  /// Makes a gamepad steer the front end: every button reads as the key a remote would have sent, through the
  /// one door every other input source already uses.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is the front-end half of the input devices plan (<c>docs/multiplatform/Input-Devices-Plan.md</c>). The
  /// pad itself is read by <see cref="IGamepadSource"/> behind the seam - evdev on Linux, XInput on Windows,
  /// nothing where there is no route - and this class turns each held button into a key event raised on the
  /// window, exactly the way <see cref="MouseRemote"/> turns a double-click into an Enter. That is the single
  /// router promise: a pad's A does not carry its own action logic, it asks the same <see cref="KeyBindings"/>
  /// table the remote's OK asks, so whatever a screen answers for the remote it answers for the pad without
  /// being asked twice - and one press can never be answered twice because there is only ever one key event on
  /// the wire.
  /// </para>
  /// <para>
  /// Buttons answer on their edge, like a key does: pressed since the last read means one key event. Holding a
  /// direction is the stick's job, which repeats here the way a held arrow key repeats on a desktop, so browsing
  /// a rail with a lean feels like holding a remote's arrow rather than tapping it.
  /// </para>
  /// <para>
  /// What is deliberately unanswered: the Guide button ("wake the television" is a CEC Stufe B question and goes
  /// through ITvControl when that lands, never through a key) and the small left Select/Back button (its pad
  /// meaning "select" collides with what B already says).
  /// </para>
  /// </remarks>
  public static class GamepadRemote
  {
    /// <summary>How often the pad is asked what it holds. XInput is a poll API anyway, and 20 Hz tracks a hand
    /// pressing buttons while costing nothing measurable.</summary>
    private static readonly TimeSpan POLL = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a stick lean must hold before it starts repeating, and how fast it then repeats -
    /// the same feel the desktop gives a held arrow key, because that is what it replaces.</summary>
    private static readonly TimeSpan REPEAT_FIRST = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan REPEAT_THEN = TimeSpan.FromMilliseconds(90);

    /// <summary>The lean past which a direction counts. The backend already cut the factory dead zone; this is
    /// the deliberate part of a lean, not its noise.</summary>
    private const float HOLD_TO_MOVE = 0.5f;

    private static DispatcherTimer? _timer;
    private static IGamepadSource? _source;
    private static Control? _root;
    private static GamepadState _last;

    /// <summary>The clock the held-stick repeat reads. Overridable so a test can hand time to the repeat rather
    /// than sleep through it - the headless dispatcher runs timers on its own clock, which a waiting test cannot
    /// push.</summary>
    private static Func<DateTime> _now = () => DateTime.UtcNow;

    internal static void OverrideClockForTests(Func<DateTime>? clock)
    {
      _now = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>One poll's worth of work, callable by a test whose headless timer may not have ticked yet. The
    /// reader is idempotent between polls, so an extra call only sees edges that really happened.</summary>
    internal static void PollOnceForTests()
    {
      Poll();
    }

    private static DateTime _nextRepeat;
    private static Key _repeated = Key.None;

    /// <summary>
    /// Starts steering <paramref name="root"/>'s key handlers from <paramref name="source"/>. Call once with the
    /// main window. A source that reports itself unavailable says why to the console and leaves everything
    /// exactly as it was - which is the whole behaviour of a machine with no pad route.
    /// </summary>
    public static void Attach(Control root, IGamepadSource source)
    {
      if (root == null)
        throw new ArgumentNullException(nameof(root));
      if (_timer != null)
        return;   // one pad path per process, like MouseRemote is one helper class

      if (!source.IsAvailable)
      {
        Console.WriteLine("gamepad:  " + source.Reason + " - the front end steers by keyboard and remote only");
        return;
      }
      Console.WriteLine("gamepad:  reading a pad"
          + (source.Reason.Length > 0 ? " (" + source.Reason + ")" : ""));

      _root = root;
      _source = source;
      _last = source.Read();
      _device = _last.DeviceName.Length > 0 ? _last.DeviceName : null;

      _timer = new DispatcherTimer { Interval = POLL };
      _timer.Tick += delegate { Poll(); };
      _timer.Start();
    }

    /// <summary>Stops reading the pad. The tests use it between cases; so could a settings switch, if one comes.</summary>
    public static void Detach()
    {
      _timer?.Stop();
      _timer = null;
      _source?.Dispose();
      _source = null;
      _root = null;
      _device = null;
      _repeated = Key.None;
      _peeks.Clear();
    }

    private static void Poll()
    {
      if (_source == null || _root == null)
        return;

      GamepadState now = _source.Read();
      DateTime utc = _now();

      // Button edges: pressed since the last read means one key event; keys do not announce their leaving here
      // because no screen waits for it.
      foreach (GamepadButton button in now.Buttons)
      {
        if (!_last.Buttons.Contains(button))
        {
          Press(button);
          HandToPeeks(button, now.DeviceName);
        }
      }

      // The stick: a lean becomes an arrow press, and a held lean repeats like a held key would.
      Key? arrow = ArrowFor(Direction(now.LeftX), Direction(now.LeftY));
      if (arrow != null)
      {
        if (arrow.Value != _repeated)
        {
          Raise(arrow.Value);
          _repeated = arrow.Value;
          _nextRepeat = utc + REPEAT_FIRST;
        }
        else if (utc >= _nextRepeat)
        {
          Raise(arrow.Value);
          _nextRepeat = utc + REPEAT_THEN;
        }
      }
      else
      {
        _repeated = Key.None;
      }

      _last = now;
    }

    /// <summary>The key each button stands for - the plan's table, in code. Everything the pad can ask, the
    /// remote can already ask, which is the point of spelling the pad as keys.</summary>
    internal static Key KeyOf(GamepadButton button)
    {
      switch (button)
      {
        case GamepadButton.DPadUp: return Key.Up;
        case GamepadButton.DPadDown: return Key.Down;
        case GamepadButton.DPadLeft: return Key.Left;
        case GamepadButton.DPadRight: return Key.Right;
        case GamepadButton.South: return SelectKey();            // A is the remote's OK, whatever that means today
        case GamepadButton.East: return Key.Escape;              // B is away again
        case GamepadButton.West: return Key.F16;                 // X is the menu key the bindings already know
        case GamepadButton.North: return Key.I;                  // Y is the screen's own menu, Info's key
        case GamepadButton.Start: return Key.M;                  // Start calls up Now Playing, as M does
        case GamepadButton.LeftShoulder: return Key.VolumeDown;
        case GamepadButton.RightShoulder: return Key.VolumeUp;
        default: return Key.None;                                // Guide, Select, stick press: silent by design
      }
    }

    /// <summary>
    /// What the viewer's own OK is right now: the first key the Select action answers to, which on a shipped
    /// file is Enter and on a refitted one is whatever the viewer wrote. The pad asks the same question the
    /// screens do rather than assuming Enter, because a television whose OK reports as F7 refits ONE table and
    /// the pad follows it for free.
    /// </summary>
    private static Key SelectKey()
    {
      IReadOnlyList<Key> keys = KeyBindings.Current.ForDevice(_device).KeysFor(KeyAction.Select);
      return keys.Count > 0 ? keys[0] : Key.Enter;
    }

    /// <summary>The pad's name since the last attach, so per-device bindings can be asked with it.</summary>
    private static string? _device;

    private static readonly List<Action<GamepadButton, string?>> _peeks = new List<Action<GamepadButton, string?>>();

    /// <summary>
    /// Hands the next button press - the button and the pad's own name - to <paramref name="onPress"/>, once.
    /// This is what lets the settings screen ask "press a button on the device you want to configure" without
    /// the pad needing a route of its own: the poll already runs, this only reads an edge off it. A machine
    /// with no pad route simply never calls back, which is the honest answer there.
    /// </summary>
    public static void PeekOnce(Action<GamepadButton, string?> onPress)
    {
      if (onPress == null)
        throw new ArgumentNullException(nameof(onPress));
      if (!_peeks.Contains(onPress))
        _peeks.Add(onPress);
    }

    /// <summary>Stops waiting for a peeked press. Safe with a handler that already fired or was never armed.</summary>
    public static void CancelPeek(Action<GamepadButton, string?> onPress)
    {
      _peeks.Remove(onPress);
    }

    private static void HandToPeeks(GamepadButton button, string deviceName)
    {
      if (_peeks.Count == 0)
        return;
      // Copied first: a handler that arms the next peek (the settings screen moving from chooser to table)
      // must not be joined by this same press.
      Action<GamepadButton, string?>[] waiting = _peeks.ToArray();
      foreach (Action<GamepadButton, string?> peek in waiting)
      {
        _peeks.Remove(peek);
        peek(button, deviceName.Length > 0 ? deviceName : null);
      }
    }

    private static void Press(GamepadButton button)
    {
      Key key = KeyOf(button);
      if (key != Key.None)
        Raise(key);
    }

    private static void Raise(Key key)
    {
      // Raised on the dispatcher for the same reason MouseRemote raises there: a pad read runs between frames,
      // and a key event landing mid-layout of the screen that just changed is a key asked about yesterday's
      // focus. Posted, it arrives against the state the viewer can see.
      Control root = _root!;
      Dispatcher.UIThread.Post(delegate
      {
        root.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = root
        });
      });
    }

    private static int Direction(float lean)
    {
      if (lean >= HOLD_TO_MOVE) return 1;
      if (lean <= -HOLD_TO_MOVE) return -1;
      return 0;
    }

    private static Key? ArrowFor(int x, int y)
    {
      // One axis at a time wins over a diagonal; a diagonal is a hand moving between two rails, and the vertical
      // move is the one it meant first (rails are rows).
      if (y < 0) return Key.Up;
      if (y > 0) return Key.Down;
      if (x < 0) return Key.Left;
      if (x > 0) return Key.Right;
      return null;
    }
  }
}
