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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MediaPortal.Frontend.Gamepads;

namespace MediaPortal.Frontend.Input
{
  /// <summary>
  /// Listens once for the next real input and says which device it came from - the reference's "press any
  /// button" step (<c>InputDevices-DialogSelectDevice.xaml</c> and <c>-DialogMapAction.xaml</c>), rebuilt on
  /// the routes this front end has.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The sniffer only ever <b>observes</b>. It watches the window's key route (which carries keyboards, IR
  /// remotes that arrive as keys, CEC remotes and Android TV remotes alike) and asks
  /// <see cref="GamepadRemote.PeekOnce"/> for the pad's next button. It raises no synthetic event and answers
  /// no action, so the single-router promise stands untouched: while a settings page listens, a press still
  /// travels the ordinary road and is answered there exactly once; the sniffer merely reads the same event off
  /// the wire.
  /// </para>
  /// <para>
  /// The capture rule is the reference's own (<c>MappingProxy.HandleInput</c>): modifiers are collected, the
  /// first non-modifier is the primary input, and the capture is complete with it. One shot per arm; arming
  /// again replaces the listener.
  /// </para>
  /// </remarks>
  public sealed class InputSniffer
  {
    /// <summary>What one captured press was: where it came from ("Keyboard"/"Xbox pad ...") and what to write
    /// into the binding table for it.</summary>
    public sealed record Capture(string DeviceName, string KeyName);

    private readonly Control _root;
    private EventHandler<KeyEventArgs>? _keyHandler;
    private Action<GamepadButton, string?>? _padHandler;

    /// <summary>Whether a capture is wanted right now.</summary>
    public bool IsListening
    {
      get { return _keyHandler != null; }
    }

    public InputSniffer(Control root)
    {
      if (root == null)
        throw new ArgumentNullException(nameof(root));
      _root = root;
    }

    /// <summary>
    /// Arms one capture. <paramref name="onCapture"/> runs on the UI thread with whatever arrives first; a
    /// bare modifier press is reported too (its key name is the modifier's own), because knowing WHICH device
    /// answered matters even when the press itself cannot be bound. Call <see cref="Cancel"/> to stop waiting.
    /// </summary>
    public void ListenOnce(Action<Capture> onCapture)
    {
      if (onCapture == null)
        throw new ArgumentNullException(nameof(onCapture));
      Stop();

      bool done = false;
      void Finish(Capture capture)
      {
        if (done)
          return;
        done = true;
        Stop();
        onCapture(capture);
      }

      _keyHandler = delegate (object? sender, KeyEventArgs args)
      {
        // handledEventsToo: a screen that already answered the key must still name itself to the viewer who
        // just pressed it - the sniffer observes the wire, it does not compete for the answer.
        Key key = args.Key != Key.None ? args.Key : args.PhysicalKey.ToQwertyKey();
        if (key == Key.None)
          return;
        // The press also travels the ordinary road and is answered there once. It must not ALSO leave the
        // screen behind the sniffer's back: while the page listens, its own Back is the page's business (the
        // editor steps table → chooser itself), and every other binding - Up, Down, Select - would walk the
        // hub or the shell one step away while the viewer only meant to name a device. So the listening
        // swallows this one press from the routing road after naming its device.
        args.Handled = true;
        Finish(new Capture("Keyboard", key.ToString()));
      };
      _root.AddHandler(InputElement.KeyDownEvent, _keyHandler, RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
          handledEventsToo: true);

      _padHandler = delegate (GamepadButton button, string? device)
      {
        Key key = GamepadRemote.KeyOf(button);
        if (key == Key.None)
          return;     // a deliberately silent button (Guide) names no binding and no device to bind it on
        Finish(new Capture(device ?? "Gamepad", key.ToString()));
      };
      GamepadRemote.PeekOnce(_padHandler);
    }

    /// <summary>Stops waiting; nothing is reported afterwards.</summary>
    public void Cancel()
    {
      Stop();
    }

    private void Stop()
    {
      if (_keyHandler != null)
      {
        _root.RemoveHandler(InputElement.KeyDownEvent, _keyHandler);
        _keyHandler = null;
      }
      if (_padHandler != null)
      {
        GamepadRemote.CancelPeek(_padHandler);
        _padHandler = null;
      }
    }
  }
}
