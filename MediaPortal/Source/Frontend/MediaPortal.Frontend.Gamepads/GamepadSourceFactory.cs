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

using System.Runtime.InteropServices;

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// The one place that decides which pad route this machine takes - probed rather than assumed, the way the
  /// CEC plan wants its backends chosen.
  /// </summary>
  public static class GamepadSource
  {
    /// <summary>
    /// The route for the machine the front end is running on: XInput on Windows, evdev on Linux, nothing
    /// anywhere else yet. The returned source answers availability and reason itself, so the caller logs the
    /// same sentence whether the answer is "no such API here" or "no pad plugged".
    /// </summary>
    public static IGamepadSource ForThisMachine()
    {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return new XInputGamepadSource();
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        return new EvdevGamepadSource();
      return new NullGamepadSource("this platform has no pad route yet");
    }
  }
}
