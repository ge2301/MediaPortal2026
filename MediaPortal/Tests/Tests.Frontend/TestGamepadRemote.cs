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
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NUnit.Framework;
using MediaPortal.Frontend.Gamepads;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.Settings;

namespace Tests.Frontend
{
  /// <summary>
  /// The gamepad path through the one action router: every button the pad can press must mean a key the binding
  /// table already answers (so the pad asks the screens nothing they do not already answer for a remote), the
  /// deliberately silent buttons must stay silent, and one press must reach the window exactly once - which is
  /// the single-router promise the input devices plan opens with.
  /// </summary>
  [TestFixture]
  public class TestGamepadRemote
  {
    [TearDown]
    public void TearDown()
    {
      GamepadRemote.Detach();
      GamepadRemote.OverrideClockForTests(null);
      KeyBindings.Override(null);
    }

    private static void RunDispatcher()
    {
      for (int i = 0; i < 10; i++)
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Every mappable button stands for a key that the shipped defaults turn back into an action. A button whose
    /// key no default answers would be a button that steers nothing, and that is what this test keeps out.
    /// </summary>
    [Test]
    public void EveryMappableButtonMeansAKeyTheBindingTableAnswers()
    {
      KeyBindings defaults = KeyBindings.TheDefaults();

      Assert.That(defaults.Is(KeyAction.Select, GamepadRemote.KeyOf(GamepadButton.South)), Is.True,
          "the pad's A means whatever the remote's OK means");
      Assert.That(defaults.Is(KeyAction.Back, GamepadRemote.KeyOf(GamepadButton.East)), Is.True,
          "and its B means away again");
      Assert.That(defaults.Is(KeyAction.Menu, GamepadRemote.KeyOf(GamepadButton.West)), Is.True);
      Assert.That(defaults.Is(KeyAction.MediaMenu, GamepadRemote.KeyOf(GamepadButton.North)), Is.True);
      Assert.That(defaults.Is(KeyAction.Media, GamepadRemote.KeyOf(GamepadButton.Start)), Is.True);
      Assert.That(defaults.Is(KeyAction.VolumeUp, GamepadRemote.KeyOf(GamepadButton.RightShoulder)), Is.True);
      Assert.That(defaults.Is(KeyAction.VolumeDown, GamepadRemote.KeyOf(GamepadButton.LeftShoulder)), Is.True);
    }

    /// <summary>
    /// The buttons the plan leaves unanswered: the Guide belongs to CEC Stufe B's ITvControl, never to a key,
    /// and the small left Select collides with what B already says.
    /// </summary>
    [Test]
    public void TheButtonsThePlanLeftOutStaySilent()
    {
      Assert.That(GamepadRemote.KeyOf(GamepadButton.Guide), Is.EqualTo(Key.None));
      Assert.That(GamepadRemote.KeyOf(GamepadButton.Select), Is.EqualTo(Key.None));
      Assert.That(GamepadRemote.KeyOf(GamepadButton.LeftStickPress), Is.EqualTo(Key.None));
    }

    /// <summary>
    /// The D-pad and the stick become the arrow keys, so the ring navigation every screen already has
    /// (DpadNavigation) steers from a pad without knowing pads exist.
    /// </summary>
    [Test]
    public void TheDirectionsMeanTheArrowKeysTheRingAlreadyWalks()
    {
      Assert.That(GamepadRemote.KeyOf(GamepadButton.DPadUp), Is.EqualTo(Key.Up));
      Assert.That(GamepadRemote.KeyOf(GamepadButton.DPadDown), Is.EqualTo(Key.Down));
      Assert.That(GamepadRemote.KeyOf(GamepadButton.DPadLeft), Is.EqualTo(Key.Left));
      Assert.That(GamepadRemote.KeyOf(GamepadButton.DPadRight), Is.EqualTo(Key.Right));
    }

    /// <summary>
    /// One press, one answer: holding A across several polls raises Enter once, releasing and pressing it again
    /// raises it a second time. This is the promise the reference guarded with its Handled flag - here it holds
    /// structurally, because the pad shares the window's single key road with every other source.
    /// </summary>
    [AvaloniaTest]
    public void AButtonHeldAcrossPollsIsAnsweredOnceAndAPressAgainIsAnsweredAgain()
    {
      Window window = new Window();
      var presses = new List<Key>();
      window.AddHandler(InputElement.KeyDownEvent,
          delegate (object? sender, KeyEventArgs args)
          {
            if (!args.Handled)
              presses.Add(args.Key);
          },
          RoutingStrategies.Bubble, handledEventsToo: true);
      window.Show();

      FakeGamepadSource pad = new FakeGamepadSource();
      StartTestClock();
      GamepadRemote.Attach(window, pad);

      // The attach read saw an empty pad; now A goes down and stays down over three polls.
      pad.Hold(new[] { GamepadButton.South });
      Tick(); Tick(); Tick();
      Assert.That(presses, Is.EqualTo(new[] { Key.Enter }),
          "a held button is a press, not a repeat - the stick owns held directions");

      // Released and pressed again: the edge answers again.
      pad.Release();
      Tick();
      pad.Hold(new[] { GamepadButton.South });
      Tick();
      Assert.That(presses, Is.EqualTo(new[] { Key.Enter, Key.Enter }));

      window.Close();
    }

    /// <summary>
    /// The stick's lean walks the arrows like a held key does: the first lean moves once at once, and holding it
    /// repeats. Direction changes move immediately, the way a hand crossing from row to row expects.
    /// </summary>
    [AvaloniaTest]
    public void AStickLeanMovesTheRingOnceAndThenRepeatsLikeAHeldKey()
    {
      Window window = new Window();
      var presses = new List<Key>();
      window.AddHandler(InputElement.KeyDownEvent,
          delegate (object? sender, KeyEventArgs args)
          {
            if (!args.Handled)
              presses.Add(args.Key);
          },
          RoutingStrategies.Bubble, handledEventsToo: true);
      window.Show();

      FakeGamepadSource pad = new FakeGamepadSource();
      StartTestClock();
      GamepadRemote.Attach(window, pad);

      // A lean down past the hold threshold: one row move arrives at once. The stick's own y grows upward while
      // a screen's rows grow downward, so a positive lean means the next row - Key.Down.
      pad.Hold(System.Array.Empty<GamepadButton>(), leftY: 1f);
      Tick();
      Assert.That(presses.Count, Is.EqualTo(1));
      Assert.That(presses[0], Is.EqualTo(Key.Down));

      // Leaning further within the same direction does not add presses; changing direction does.
      pad.Hold(System.Array.Empty<GamepadButton>(), leftX: 1f, leftY: 1f);
      Tick();
      Assert.That(presses.Count, Is.EqualTo(1), "a diagonal still means the row move it started with");

      pad.Hold(System.Array.Empty<GamepadButton>(), leftX: 1f);
      Tick();
      Assert.That(presses.Count, Is.EqualTo(2));
      Assert.That(presses[1], Is.EqualTo(Key.Right));

      // A lean inside the noise band moves nothing at all.
      pad.Hold(System.Array.Empty<GamepadButton>(), leftX: 0.2f);
      Tick();
      Assert.That(presses.Count, Is.EqualTo(2), "the dead zone is not a direction");

      // And a held lean repeats like a held key: hand it the first-delay time and one more arrives.
      pad.Hold(System.Array.Empty<GamepadButton>(), leftY: 1f);
      Tick();                                    // the row move itself
      HandTime(TimeSpan.FromMilliseconds(500));   // past the first delay
      Tick();
      Assert.That(presses.Count, Is.EqualTo(4), "a held lean keeps walking, once per repeat step");
      HandTime(TimeSpan.FromMilliseconds(100));
      Tick();
      Assert.That(presses.Count, Is.EqualTo(5));

      window.Close();
    }

    /// <summary>
    /// A machine with no pad route says so once and changes nothing: attaching a null source raises no key and
    /// starts no timer, which is the front end behaving exactly as it did before this assembly existed.
    /// </summary>
    [AvaloniaTest]
    public void AMachineWithNoPadRouteHearsNothingAndSaysWhy()
    {
      Window window = new Window();
      int presses = 0;
      window.AddHandler(InputElement.KeyDownEvent,
          delegate (object? sender, KeyEventArgs args) { presses++; },
          RoutingStrategies.Bubble, handledEventsToo: true);
      window.Show();

      NullGamepadSource none = new NullGamepadSource("no pad on any XInput slot");
      Assert.That(none.IsAvailable, Is.False);
      GamepadRemote.Attach(window, none);
      Tick();
      Assert.That(presses, Is.Zero);

      window.Close();
    }

    /// <summary>Runs one poll of the reader and lets the posted key events reach the window. The clock is a test
    /// clock, so the held-stick repeat only fires when a test hands time to it on purpose.</summary>
    private static void Tick()
    {
      GamepadRemote.PollOnceForTests();
      RunDispatcher();
    }

    /// <summary>Hands the reader's repeat clock forward without sleeping.</summary>
    private static void HandTime(TimeSpan span)
    {
      _handed += span;
    }

    private static TimeSpan _handed;

    private static void StartTestClock()
    {
      _handed = TimeSpan.Zero;
      DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      GamepadRemote.OverrideClockForTests(() => start + _handed);
    }
  }
}
