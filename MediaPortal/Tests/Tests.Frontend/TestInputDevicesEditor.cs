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
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using NUnit.Framework;
using MediaPortal.Frontend.Gamepads;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.Settings;
using MediaPortal.Frontend.ViewModels;

namespace Tests.Frontend
{
  /// <summary>
  /// The mapping editor the input devices plan ends with: the sniffer names the device a press came from and
  /// reports its key, one shot per arming, and the view model writes what was captured into the file's table -
  /// global for "(all devices / defaults)", per-device for a named pad - then clears the running front end's
  /// cached table so the answer takes effect at once.
  /// </summary>
  [TestFixture]
  public class TestInputDevicesEditor
  {
    private string? _file;

    [SetUp]
    public void SetUp()
    {
      _file = Path.Combine(Path.GetTempPath(), "mp2-keys-" + Guid.NewGuid().ToString("N") + ".json");
      Environment.SetEnvironmentVariable("MP2_KEYBINDINGS_FILE", _file);
      KeyBindings.Override(null);
    }

    [TearDown]
    public void TearDown()
    {
      GamepadRemote.Detach();
      KeyBindings.Override(null);
      Environment.SetEnvironmentVariable("MP2_KEYBINDINGS_FILE", null);
      if (_file != null && File.Exists(_file))
        File.Delete(_file);
    }

    /// <summary>
    /// The chooser's core: a key event on the window names "Keyboard" and carries its key, and the capture is
    /// one shot - a second press after the first arrived says nothing.
    /// </summary>
    [AvaloniaTest]
    public void TheSnifferNamesTheKeyboardAndItsKeyOnce()
    {
      Window window = new Window();
      window.Show();

      InputSniffer sniffer = new InputSniffer(window);
      List<InputSniffer.Capture> got = new List<InputSniffer.Capture>();
      sniffer.ListenOnce(got.Add);

      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.F7,
          Source = window
      });

      Assert.That(got, Has.Count.EqualTo(1), "one armed listener wants exactly one press");
      Assert.That(got[0].DeviceName, Is.EqualTo("Keyboard"));
      Assert.That(got[0].KeyName, Is.EqualTo("F7"), "the name written must be the name the loader parses back");

      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.F8,
          Source = window
      });
      Assert.That(got, Has.Count.EqualTo(1), "and after the shot is taken the listener is gone");
      Assert.That(sniffer.IsListening, Is.False);
    }

    /// <summary>
    /// Even a press a screen already answered names itself to the sniffer - the settings page listens beside
    /// the screens, it does not compete with them.
    /// </summary>
    [AvaloniaTest]
    public void TheSnifferHearsAPressThatWasAlreadyAnswered()
    {
      Window window = new Window();
      window.AddHandler(InputElement.KeyDownEvent,
          delegate (object? sender, KeyEventArgs args) { args.Handled = true; },
          RoutingStrategies.Tunnel, handledEventsToo: true);
      window.Show();

      InputSniffer sniffer = new InputSniffer(window);
      List<InputSniffer.Capture> got = new List<InputSniffer.Capture>();
      sniffer.ListenOnce(got.Add);

      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.M,
          Source = window
      });

      Assert.That(got, Has.Count.EqualTo(1), "a answered press still names its device to a waiting viewer");
    }

    /// <summary>A cancelled sniffer reports nothing afterwards.</summary>
    [AvaloniaTest]
    public void ACancelledSnifferSaysNothing()
    {
      Window window = new Window();
      window.Show();

      InputSniffer sniffer = new InputSniffer(window);
      List<InputSniffer.Capture> got = new List<InputSniffer.Capture>();
      sniffer.ListenOnce(got.Add);
      sniffer.Cancel();

      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.F7,
          Source = window
      });
      Assert.That(got, Is.Empty);
    }

    /// <summary>
    /// The pad's half of the chooser: a button edge during a peek hands the pad's OWN name to the settings
    /// screen, which is what lets a per-device table open for the right controller.
    /// </summary>
    [AvaloniaTest]
    public void APeekedPadPressCarriesThePadsOwnName()
    {
      Window window = new Window();
      window.Show();

      FakeGamepadSource pad = new FakeGamepadSource("Wireless Pad");
      GamepadRemote.Attach(window, pad);

      List<(GamepadButton, string?)> peeks = new List<(GamepadButton, string?)>();
      GamepadRemote.PeekOnce(delegate (GamepadButton button, string? device) { peeks.Add((button, device)); });

      pad.Hold(new[] { GamepadButton.East });
      GamepadRemote.PollOnceForTests();

      Assert.That(peeks, Has.Count.EqualTo(1));
      Assert.That(peeks[0].Item1, Is.EqualTo(GamepadButton.East));
      Assert.That(peeks[0].Item2, Is.EqualTo("Wireless Pad"));

      // One shot: the next press finds no waiter unless one arms again.
      pad.Release();
      GamepadRemote.PollOnceForTests();
      pad.Hold(new[] { GamepadButton.South });
      GamepadRemote.PollOnceForTests();
      Assert.That(peeks, Has.Count.EqualTo(1), "a peek is taken once, like the reference's dialog closed");
    }

    /// <summary>
    /// Writing a re-bound key: the learning step's capture lands in the FILE (not the merged defaults), under
    /// the open device alone, and the running front end answers the new key immediately afterwards. The pad is
    /// named through a real peeked press first, exactly as the chooser does it on the screen.
    /// </summary>
    [AvaloniaTest]
    public void AReboundKeyLandsInTheFileAndAnswersAtOnce()
    {
      Window window = new Window();
      window.Show();

      FakeGamepadSource pad = new FakeGamepadSource("Fake Pad");
      GamepadRemote.Attach(window, pad);

      InputDevicesViewModel model = new InputDevicesViewModel();
      model.Reload(window);           // the chooser listens, like the reference's dialog from its first moment

      pad.Hold(new[] { GamepadButton.South });
      GamepadRemote.PollOnceForTests();   // ...so a pad press names its device, opening its table

      Assert.That(model.Choosing, Is.False, "the pressed pad's own page stood up without a pick");
      Assert.That(model.Device, Is.EqualTo("Fake Pad"));

      InputDevicesViewModel.ActionRow back =
          model.Rows.First(r => r.Action == KeyAction.Back.ToString());
      model.BeginMapping(back);       // Esc cancels; F9 does not, so this write goes through
      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.F9,
          Source = window
      });

      KeyBindings onDisk = KeyBindings.Load(null, out _);
      Assert.That(onDisk.Devices.ContainsKey("Fake Pad"), Is.True,
          "a named device earns its own entry in the file");
      Assert.That(onDisk.Devices["Fake Pad"].Bindings[KeyAction.Back.ToString()], Is.EqualTo(new[] { "F9" }));
      Assert.That(onDisk.Bindings.ContainsKey(KeyAction.Back.ToString()), Is.False,
          "and the global table stays as it was - an edit for one pad never bakes into everybody's file");

      KeyBindings live = KeyBindings.Current;
      Assert.That(live.ForDevice("Fake Pad").Is(KeyAction.Back, Key.F9), Is.True,
          "the running front end answers the new key for this pad right away");
      Assert.That(live.ForDevice(null).Is(KeyAction.Back, Key.Escape), Is.True,
          "while every other hand keeps the old answer");
    }

    /// <summary>
    /// Clearing an entry: the action falls back to the machine's table for that device, which is exactly what
    /// the reference's DeleteMapping did.
    /// </summary>
    [AvaloniaTest]
    public void ClearingAnEntryLetsTheActionFallBackToTheGlobalsTable()
    {
      File.WriteAllText(_file!, ("{ 'bindings': { 'Menu': ['F5'] },"
          + " 'devices': { 'Fake Pad': { 'bindings': { 'Menu': ['F6'] } } } }").Replace('\'', '"'));

      InputDevicesViewModel model = new InputDevicesViewModel();
      Window window = new Window();
      window.Show();
      model.Reload(window);
      model.Open("Fake Pad");
      Assert.That(model.Devices, Does.Contain("Fake Pad"),
          "a device the file knows is offered without pressing anything");

      InputDevicesViewModel.ActionRow menu =
          model.Rows.First(r => r.Action == KeyAction.Menu.ToString());
      Assert.That(menu.Overridden, Is.True, "the row says this device carries its own entry");

      model.ClearMapping(menu);

      KeyBindings onDisk = KeyBindings.Load(null, out _);
      Assert.That(onDisk.Devices.ContainsKey("Fake Pad"), Is.False,
          "the entry is gone from the file - a device with nothing of its own keeps no empty shell");
      Assert.That(KeyBindings.Current.ForDevice("Fake Pad").Is(KeyAction.Menu, Key.F5), Is.True,
          "and the pad now answers the machine's own menu key");
    }

    /// <summary>The global pseudo-device edits the file's top-level table, leaving no devices section behind.</summary>
    [AvaloniaTest]
    public void EditingTheGlobalEntryWritesTheFilesTopLevelTable()
    {
      Window window = new Window();
      window.Show();

      InputDevicesViewModel model = new InputDevicesViewModel();
      model.Reload(window);
      model.Open(InputDevicesViewModel.GLOBAL_DEVICE);

      InputDevicesViewModel.ActionRow find =
          model.Rows.First(r => r.Action == KeyAction.Find.ToString());
      model.BeginMapping(find);
      window.RaiseEvent(new KeyEventArgs
      {
          RoutedEvent = InputElement.KeyDownEvent,
          Key = Key.K,
          Source = window
      });

      KeyBindings onDisk = KeyBindings.Load(null, out _);
      Assert.That(onDisk.Bindings[KeyAction.Find.ToString()], Is.EqualTo(new[] { "K" }));
      Assert.That(onDisk.Devices, Is.Empty, "an edit for everybody needs no device section");
      Assert.That(KeyBindings.Current.Is(KeyAction.Find, Key.K), Is.True);
    }
  }
}
