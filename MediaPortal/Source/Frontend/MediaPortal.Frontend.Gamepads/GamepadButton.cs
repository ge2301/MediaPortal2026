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

using System.Collections.Generic;

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// The buttons a gamepad can report, spelled the same on every platform.
  /// </summary>
  /// <remarks>
  /// The reference's HidInput decoded raw HID reports into its own input names per vendor; this enum is the
  /// same idea shrunk to what a media centre steers with. A backend's job is only to notice that ITS physical
  /// button - evdev's <c>BTN_SOUTH</c>, XInput's <c>XINPUT_GAMEPAD_A</c> - is one of these words. Nothing above
  /// this assembly knows which pad was bought.
  /// </remarks>
  public enum GamepadButton
  {
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,

    /// <summary>The bottom face button: Xbox A, PlayStation cross.</summary>
    South,

    /// <summary>The right face button: Xbox B, PlayStation circle.</summary>
    East,

    /// <summary>The left face button: Xbox X, PlayStation square.</summary>
    West,

    /// <summary>The top face button: Xbox Y, PlayStation triangle.</summary>
    North,

    LeftShoulder,
    RightShoulder,

    /// <summary>The left small button: Select, Back, Create.</summary>
    Select,

    /// <summary>The right small button: Start, Options.</summary>
    Start,

    /// <summary>The home button. Deliberately unmapped by default - see docs/multiplatform/Input-Devices-Plan.md.</summary>
    Guide,

    /// <summary>The stick itself pressed down.</summary>
    LeftStickPress
  }

  /// <summary>
  /// One read of one pad: which normalized buttons are held, which axis leans, and who the pad calls itself.
  /// </summary>
  /// <param name="DeviceName">What the pad is called, for the day bindings can be per device; empty when the
  /// backend cannot ask.</param>
  /// <param name="Buttons">The buttons held at this instant. A backend that does not know a button simply leaves
  /// it out rather than guessing.</param>
  /// <param name="LeftX">The left stick's horizontal lean, -1 to 1, already dead-zoned by the backend.</param>
  /// <param name="LeftY">The left stick's vertical lean, -1 to 1, already dead-zoned by the backend.</param>
  public readonly record struct GamepadState(
      string DeviceName,
      IReadOnlySet<GamepadButton> Buttons,
      float LeftX,
      float LeftY);
}
