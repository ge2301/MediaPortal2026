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
using System.Runtime.InteropServices;

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// The Windows pad route: XInput, the one function this assembly P/Invokes and no more.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The reference read pads through raw HID and its own report decoders, which is why it was Windows-only by
  /// construction and knew vendor collections by heart. XInput is the opposite bargain: one small exported API
  /// (<c>xinput1_4.dll</c>, present on every Windows since Vista) that already speaks "an Xbox-style pad is
  /// plugged into slot N" - and an Xbox-style pad is what nearly every pad on a shop shelf reports as, either
  /// natively or through its own driver. A pad XInput does not know (an old DirectInput joystick) is a pad this
  /// front end does not steer, which is written down rather than papered over.
  /// </para>
  /// <para>
  /// The DLL loads lazily with the first read, so an assembly that merely references this class still loads
  /// everywhere. <c>XInputGetState</c> on a slot with no pad answers an error code rather than throwing, which
  /// is what makes polling four slots cheap and safe. Pulling the pad between two reads is answered the same
  /// way, so unplugging mid-browsing does nothing worse than the pad stopping working.
  /// </para>
  /// </remarks>
  public sealed class XInputGamepadSource : IGamepadSource
  {
    private const int XUSER_MAX_COUNT = 4;
    private const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    // XINPUT_GAMEPAD's wButtons bits, from xinput.h. Only the ones GamepadButton has a word for are named.
    private const ushort DPAD_UP = 0x0001;
    private const ushort DPAD_DOWN = 0x0002;
    private const ushort DPAD_LEFT = 0x0004;
    private const ushort DPAD_RIGHT = 0x0008;
    private const ushort START = 0x0010;
    private const ushort BACK = 0x0020;
    private const ushort LEFT_STICK = 0x0040;
    private const ushort LEFT_SHOULDER = 0x0100;
    private const ushort RIGHT_SHOULDER = 0x0200;
    private const ushort GUIDE = 0x0400;
    private const ushort A = 0x1000;
    private const ushort B = 0x2000;
    private const ushort X = 0x4000;
    private const ushort Y = 0x8000;

    // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE from xinput.h - the factory dead zone, kept rather than reinvented.
    private const short DEADZONE = 7849;
    private const float FULL_SCALE = 32767f;

    private bool _probed;
    private bool _available;
    private string _reason = "not asked yet";
    private int _slot = -1;

    public bool IsAvailable
    {
      get
      {
        Probe();
        return _available;
      }
    }

    public string Reason
    {
      get
      {
        Probe();
        return _reason;
      }
    }

    private void Probe()
    {
      if (_probed)
        return;
      _probed = true;

      try
      {
        // Ask each slot once. Any answer other than ERROR_DEVICE_NOT_CONNECTED means either a pad is there or
        // something is wrong with the DLL; both say something worth logging.
        for (int slot = 0; slot < XUSER_MAX_COUNT; slot++)
        {
          int code = XInputGetState(slot, out XINPUT_STATE state);
          if (code == 0)
          {
            _slot = slot;
            _available = true;
            _reason = "";
            return;
          }
          if (code != ERROR_DEVICE_NOT_CONNECTED)
          {
            _reason = "XInput answered " + code + " instead of 'no pad'";
            return;
          }
        }
        _reason = "no pad on any XInput slot";
      }
      catch (DllNotFoundException)
      {
        _reason = "xinput1_4.dll is not on this machine";
      }
      catch (EntryPointNotFoundException)
      {
        _reason = "xinput1_4.dll has no XInputGetState";
      }
    }

    public GamepadState Read()
    {
      if (!IsAvailable || _slot < 0)
        return new GamepadState("", NullGamepadSource.EMPTY, 0f, 0f);

      XINPUT_STATE state;
      int code;
      try
      {
        code = XInputGetState(_slot, out state);
      }
      catch (DllNotFoundException)
      {
        _available = false;
        _reason = "xinput1_4.dll went away";
        return new GamepadState("", NullGamepadSource.EMPTY, 0f, 0f);
      }

      if (code != 0)
      {
        // The pad was pulled between two reads. Stay available - the same slot may hold it again when it is
        // replugged - and read empty meanwhile.
        return new GamepadState("", NullGamepadSource.EMPTY, 0f, 0f);
      }

      ushort buttons = state.Gamepad.wButtons;
      var held = new HashSet<GamepadButton>();
      if ((buttons & DPAD_UP) != 0) held.Add(GamepadButton.DPadUp);
      if ((buttons & DPAD_DOWN) != 0) held.Add(GamepadButton.DPadDown);
      if ((buttons & DPAD_LEFT) != 0) held.Add(GamepadButton.DPadLeft);
      if ((buttons & DPAD_RIGHT) != 0) held.Add(GamepadButton.DPadRight);
      if ((buttons & A) != 0) held.Add(GamepadButton.South);
      if ((buttons & B) != 0) held.Add(GamepadButton.East);
      if ((buttons & X) != 0) held.Add(GamepadButton.West);
      if ((buttons & Y) != 0) held.Add(GamepadButton.North);
      if ((buttons & LEFT_SHOULDER) != 0) held.Add(GamepadButton.LeftShoulder);
      if ((buttons & RIGHT_SHOULDER) != 0) held.Add(GamepadButton.RightShoulder);
      if ((buttons & BACK) != 0) held.Add(GamepadButton.Select);
      if ((buttons & START) != 0) held.Add(GamepadButton.Start);
      if ((buttons & GUIDE) != 0) held.Add(GamepadButton.Guide);
      if ((buttons & LEFT_STICK) != 0) held.Add(GamepadButton.LeftStickPress);

      return new GamepadState("XInput slot " + _slot, held,
          DeadZoned(state.Gamepad.sThumbLX), DeadZoned(state.Gamepad.sThumbLY));
    }

    /// <summary>The factory dead zone applied, then the remainder stretched to the full range so a lean past the
    /// zone reaches full speed rather than stopping at its edge.</summary>
    private static float DeadZoned(short raw)
    {
      if (raw > -DEADZONE && raw < DEADZONE)
        return 0f;
      float stretched = (raw - Math.Sign(raw) * DEADZONE) / (FULL_SCALE - DEADZONE);
      return Math.Clamp(stretched, -1f, 1f);
    }

    public void Dispose()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
      public ushort wButtons;
      public byte bLeftTrigger;
      public byte bRightTrigger;
      public short sThumbLX;
      public short sThumbLY;
      public short sThumbRX;
      public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
      public uint dwPacketNumber;
      public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int userIndex, out XINPUT_STATE state);
  }
}
