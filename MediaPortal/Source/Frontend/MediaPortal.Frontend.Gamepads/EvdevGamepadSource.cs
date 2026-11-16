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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// The Linux pad route: the kernel's own event devices under <c>/dev/input/event*</c>, read with plain
  /// <c>open</c>/<c>read</c> - no library, no package, the same syscall surface the CEC plan's Stufe A trusts.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Every pad on Linux is an event device that announces what it can do through <c>EVIOCGBIT</c>; a gamepad is
  /// a device whose key bits include <c>BTN_SOUTH</c>. That one question is the whole detection, and it is why no
  /// vendor table is needed here the way the reference kept one for Windows: the kernel already knows the pads.
  /// Buttons and stick readings arrive as <c>input_event</c> records, which are accumulated into the held set
  /// between two reads; the device is opened non-blocking so a read never waits for input.
  /// </para>
  /// <para>
  /// A pad replugged after being pulled appears as a NEW event number, which is why the device list is re-walked
  /// whenever the watched device vanished - the pad comes back working without a restart, exactly the promise
  /// phase 1 makes. Android also speaks evdev, but its application sandbox does not let an ordinary app open
  /// <c>/dev/input</c>, so Android stays on the null source until its bring-up decides otherwise.
  /// </para>
  /// </remarks>
  public sealed class EvdevGamepadSource : IGamepadSource
  {
    // The evdev numbers this file needs, from the kernel's <linux/input.h>. They are ABI-stable; naming them
    // here is what keeps the assembly free of any binding package.
    private const int EV_SYN = 0x00;
    private const int EV_KEY = 0x01;
    private const int EV_ABS = 0x03;
    private const int KEY_DOWN = 1;   // struct input_event value: pressed or held
    private const int ABS_X = 0x00;
    private const int ABS_Y = 0x01;
    private const int ABS_HAT0X = 0x10;
    private const int ABS_HAT0Y = 0x11;
    private const int BTN_MISC = 0x100;
    private const int BTN_SOUTH = 0x130;
    private const int BTN_EAST = 0x131;
    private const int BTN_C = 0x132;
    private const int BTN_NORTH = 0x133;
    private const int BTN_WEST = 0x134;
    private const int BTN_Z = 0x135;
    private const int BTN_TL = 0x136;
    private const int BTN_TR = 0x137;
    private const int BTN_SELECT = 0x138;
    private const int BTN_START = 0x139;
    private const int BTN_MODE = 0x13a;
    private const int BTN_THUMBL = 0x13b;
    private const int BTN_DPAD_UP = 0x200;
    private const int BTN_DPAD_DOWN = 0x201;
    private const int BTN_DPAD_LEFT = 0x202;
    private const int BTN_DPAD_RIGHT = 0x203;

    // The joystick dead zone the kernel documents as the sensible default (JS_DEADZONE), stretched like
    // XInput's is so a lean past the zone still reaches full speed.
    private const float DEADZONE = 0.11f;
    private const float FULL_SCALE = 32767f;

    private bool _probed;
    private string _reason = "not asked yet";
    private SafeFileHandle? _handle;
    private string _name = "";
    private int _absMin = -32767;
    private int _absMax = 32767;

    // What the last events left held, between two reads. Written only on the reader's thread.
    private readonly HashSet<GamepadButton> _held = new HashSet<GamepadButton>();
    private int _axisX;
    private int _axisY;
    private int _hatX;
    private int _hatY;

    public bool IsAvailable
    {
      get
      {
        Probe();
        return _handle != null;
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

      if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
        _reason = "this is not Linux";
        return;
      }

      OpenFirstPad();
      if (_handle == null)
        _reason = "no event device under /dev/input answers to the gamepad button bits";
    }

    /// <summary>Walks /dev/input/event* and opens the first device whose key bits carry BTN_SOUTH - the one
    /// question that separates a pad from a keyboard, a mouse and a CEC remote.</summary>
    private void OpenFirstPad()
    {
      string[] devices;
      try
      {
        devices = Directory.GetFiles("/dev/input", "event*");
      }
      catch (IOException)
      {
        return;
      }
      Array.Sort(devices, StringComparer.Ordinal);

      foreach (string device in devices)
      {
        SafeFileHandle handle = Open(device);
        if (handle == null || handle.IsInvalid)
          continue;

        // A device that merely carries BTN_SOUTH among its keys is a pad (or a wheel with pad buttons, which
        // steers just as well). The name is read for the log and for future per-device bindings.
        byte[] keyBits = new byte[KEY_BITS_BYTES];
        if (!Ioctl(handle, Eviocgbit(EV_KEY), keyBits))
        {
          handle.Dispose();
          continue;
        }
        if (!BitSet(keyBits, BTN_SOUTH))
        {
          handle.Dispose();
          continue;
        }

        byte[] absBits = new byte[KEY_BITS_BYTES];
        Ioctl(handle, Eviocgbit(EV_ABS), absBits);

        _handle = handle;
        _name = ReadName(handle);
        if (BitSet(absBits, ABS_X) && BitSet(absBits, ABS_Y))
          ReadAbsRange(handle, ABS_X);
        return;
      }
    }

    public GamepadState Read()
    {
      if (_handle == null)
      {
        // Either there was never a pad, or the watched one went away - in which case re-walk the folder so a
        // replugged pad works again without a restart.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
          _probed = false;
          _held.Clear();
          _axisX = _axisY = _hatX = _hatY = 0;
          Probe();
        }
        return new GamepadState("", NullGamepadSource.EMPTY, 0f, 0f);
      }

      DrainEvents();

      // The D-pad arrives two ways depending on the driver: as BTN_DPAD_* keys, or as the HAT0 axes. Either
      // answer means the same direction.
      var snapshot = new HashSet<GamepadButton>(_held);
      if (_hatX < 0) snapshot.Add(GamepadButton.DPadLeft);
      if (_hatX > 0) snapshot.Add(GamepadButton.DPadRight);
      if (_hatY < 0) snapshot.Add(GamepadButton.DPadUp);   // the hat's up is negative, like a screen's y
      if (_hatY > 0) snapshot.Add(GamepadButton.DPadDown);

      return new GamepadState(_name, snapshot, Stick(_axisX), Stick(_axisY));
    }

    /// <summary>Reads everything the kernel has queued since the last read; never blocks (the handle is O_NONBLOCK).</summary>
    private void DrainEvents()
    {
      // One input_event on 64-bit Linux is 24 bytes (timeval64 + type/code/value); on 32-bit it is 16. Reading
      // in a multiple of both and slicing by stride would be clever; asking the kernel per record is cheap at
      // polling rates and honest about the layout instead.
      byte[] record = new byte[EVENT_STRIDE];
      while (true)
      {
        int got = ReadNext(record);
        if (got < EVENT_STRIDE)
          return;

        ushort type = BitConverter.ToUInt16(record, TYPE_OFFSET);
        ushort code = BitConverter.ToUInt16(record, CODE_OFFSET);
        int value = BitConverter.ToInt32(record, VALUE_OFFSET);

        if (type == EV_KEY)
        {
          GamepadButton? button = ButtonOf(code);
          if (button != null)
          {
            if (value == KEY_DOWN)
              _held.Add(button.Value);
            else if (value == 0)
              _held.Remove(button.Value);
          }
        }
        else if (type == EV_ABS)
        {
          switch (code)
          {
            case ABS_X: _axisX = value; break;
            case ABS_Y: _axisY = value; break;
            case ABS_HAT0X: _hatX = value; break;
            case ABS_HAT0Y: _hatY = value; break;
          }
        }
      }
    }

    private static GamepadButton? ButtonOf(ushort code)
    {
      switch (code)
      {
        case BTN_SOUTH: return GamepadButton.South;
        case BTN_EAST: return GamepadButton.East;
        case BTN_C: return GamepadButton.West;       // some drivers spell Xbox X as BTN_C
        case BTN_NORTH: return GamepadButton.North;
        case BTN_WEST: return GamepadButton.West;
        case BTN_Z: return GamepadButton.North;      // and Xbox Y as BTN_Z
        case BTN_TL: return GamepadButton.LeftShoulder;
        case BTN_TR: return GamepadButton.RightShoulder;
        case BTN_SELECT: return GamepadButton.Select;
        case BTN_START: return GamepadButton.Start;
        case BTN_MODE: return GamepadButton.Guide;
        case BTN_THUMBL: return GamepadButton.LeftStickPress;
        case BTN_DPAD_UP: return GamepadButton.DPadUp;
        case BTN_DPAD_DOWN: return GamepadButton.DPadDown;
        case BTN_DPAD_LEFT: return GamepadButton.DPadLeft;
        case BTN_DPAD_RIGHT: return GamepadButton.DPadRight;
        default: return null;
      }
    }

    private float Stick(int raw)
    {
      // Normalize against the range THIS device reported, then apply and stretch past the dead zone.
      float scale = Math.Max(Math.Abs(_absMin), Math.Abs(_absMax));
      if (scale <= 0f)
        return 0f;
      float lean = raw / scale;
      if (lean > -DEADZONE && lean < DEADZONE)
        return 0f;
      float stretched = (lean - Math.Sign(lean) * DEADZONE) / (1f - DEADZONE);
      return Math.Clamp(stretched, -1f, 1f);
    }

    // ---- the kernel seam: three syscalls and the input_event layout -------------------------------

    private const int EVENT_STRIDE = 24;   // sizeof(struct input_event) with timespec64, every 64-bit arch
    private const int TYPE_OFFSET = 16;
    private const int CODE_OFFSET = 18;
    private const int VALUE_OFFSET = 20;
    private const int KEY_BITS_BYTES = 768;  // KEY_MAX(0x2ff)/8 rounded up to longs, generous

    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 04000;   // octal 0o4000, as the kernel defines it

    private int ReadNext(byte[] buffer)
    {
      if (_handle == null)
        return -1;
      try
      {
        return read(_handle.DangerousGetHandle(), buffer, EVENT_STRIDE);
      }
      catch (Exception)
      {
        // The device disappeared under us (pad pulled). The next Read re-walks the folder.
        CloseDevice();
        return -1;
      }
    }

    private void CloseDevice()
    {
      _handle?.Dispose();
      _handle = null;
      _name = "";
    }

    private static SafeFileHandle Open(string path)
    {
      int fd = open(path, O_RDONLY | O_NONBLOCK);
      if (fd < 0)
        return null!;
      return new SafeFileHandle(fd, ownsHandle: true);
    }

    private static bool Ioctl(SafeFileHandle handle, nuint request, byte[] argument)
    {
      try
      {
        return ioctl(handle.DangerousGetHandle(), request, argument) == 0;
      }
      catch (Exception)
      {
        return false;
      }
    }

    /// <summary>EVIOCGBIT(ev, key): the request encodes the event type in its low byte.</summary>
    private static nuint Eviocgbit(int eventType)
    {
      // _IOR('E', 0x20 + ev, unsigned long[]) - shifted in ulong arithmetic because C# will not shift nuint by
      // nuint. The size word is what the kernel's compat path takes generously for the bit-array form.
      const ulong NRSHIFT = 0;
      const ulong TYPESHIFT = 8;
      const ulong SIZESHIFT = 16;
      const ulong READ = 2;
      ulong nr = (ulong)(0x20 + eventType);
      ulong type = 'E';
      ulong size = 8;
      return (nuint)(((ulong)READ << (int)(SIZESHIFT + 14)) | ((ulong)type << (int)TYPESHIFT) | ((ulong)size << (int)SIZESHIFT) | nr);
    }

    private static bool BitSet(byte[] bits, int index)
    {
      int byteIndex = index / 8;
      return byteIndex < bits.Length && (bits[byteIndex] & (1 << (index % 8))) != 0;
    }

    private void ReadAbsRange(SafeFileHandle handle, int axis)
    {
      // EVIOCGABS(axis): a 24-byte input_absinfo whose min/max decide the stick's normalization.
      byte[] info = new byte[24];
      nuint request = Eviocgabs(axis);
      if (Ioctl(handle, request, info))
      {
        _absMin = BitConverter.ToInt32(info, 4);
        _absMax = BitConverter.ToInt32(info, 8);
      }
    }

    private static nuint Eviocgabs(int axis)
    {
      const ulong TYPESHIFT = 8;
      const ulong SIZESHIFT = 16;
      const ulong READ = 2;
      ulong nr = (ulong)(0x40 + axis);
      ulong type = 'E';
      ulong size = 24;
      return (nuint)(((ulong)READ << (int)(SIZESHIFT + 14)) | ((ulong)type << (int)TYPESHIFT) | ((ulong)size << (int)SIZESHIFT) | nr);
    }

    private static string ReadName(SafeFileHandle handle)
    {
      // EVIOCGNAME(len): _IOWR('E', 0x02, char[len]) - the name says which pad the log and the settings screen
      // will show the viewer.
      byte[] name = new byte[128];
      nuint request = (nuint)((3UL << 30) | (128UL << 16) | ((ulong)'E' << 8) | 0x02);  // _IOWR: READ|WRITE
      if (!Ioctl(handle, request, name))
        return "";
      int end = Array.IndexOf(name, (byte)0);
      if (end < 0) end = name.Length;
      return System.Text.Encoding.UTF8.GetString(name, 0, end);
    }

    public void Dispose()
    {
      CloseDevice();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(IntPtr fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fd, nuint request, byte[] argp);
  }
}
