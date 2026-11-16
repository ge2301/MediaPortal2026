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

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// Where gamepad states come from - the seam the platform decisions hide behind, the way
  /// <c>MediaPortal.Frontend.Player</c> hides libVLC's.
  /// </summary>
  /// <remarks>
  /// A source reads its platform's pad language (evdev events on Linux, XInput on Windows, nothing on a machine
  /// that has no route yet) and hands up <see cref="GamepadState"/> readings in the one spelling everything above
  /// this assembly speaks. The reader that owns the source decides timing; a source only answers when asked.
  /// </remarks>
  public interface IGamepadSource : IDisposable
  {
    /// <summary>Whether this machine has a route to a pad at all. Asked once at start-up; false means the front
    /// end behaves exactly as it did before this assembly existed.</summary>
    bool IsAvailable { get; }

    /// <summary>Said out loud when <see cref="IsAvailable"/> is false, so the start-up log can tell a viewer
    /// WHICH of "no such API here", "no pad plugged" and "the library is missing" they are looking at.</summary>
    string Reason { get; }

    /// <summary>One read of the pad right now: buttons held, stick leaned. Called on the reader's timer; it must
    /// never block waiting for input - a pad that is not there simply reads empty.</summary>
    GamepadState Read();
  }
}
