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

namespace MediaPortal.Frontend.Gamepads
{
  /// <summary>
  /// The source for every machine that has no pad route yet - macOS, Android before its bring-up, and any test
  /// that does not want a pad in the room. It answers "no", says which kind of no it is, and reads empty.
  /// </summary>
  public sealed class NullGamepadSource : IGamepadSource
  {
    private readonly string _reason;

    public NullGamepadSource(string reason)
    {
      _reason = reason ?? "";
    }

    public bool IsAvailable => false;

    public string Reason => _reason;

    public GamepadState Read() => new GamepadState("", EMPTY, 0f, 0f);

    public void Dispose()
    {
    }

    internal static readonly IReadOnlySet<GamepadButton> EMPTY =
        new HashSet<GamepadButton>();
  }

  /// <summary>
  /// The source the tests steer with: the test writes what the pad holds, the reader reads it. No thread of its
  /// own - the headless tests drive the reader's timer themselves, so a button press is something a test states
  /// rather than something it waits for.
  /// </summary>
  public sealed class FakeGamepadSource : IGamepadSource
  {
    private GamepadState _held = new GamepadState("Fake Pad", NullGamepadSource.EMPTY, 0f, 0f);
    private readonly string _name;

    /// <summary>A pad that calls itself "Fake Pad" unless a test wants to be named, like a real driver does.</summary>
    public FakeGamepadSource(string? name = null)
    {
      _name = name ?? "Fake Pad";
      _held = new GamepadState(_name, NullGamepadSource.EMPTY, 0f, 0f);
    }

    public bool IsAvailable => true;

    public string Reason => "";

    public GamepadState Read() => _held;

    /// <summary>The pad now holds exactly these buttons and leans its stick there.</summary>
    public void Hold(IEnumerable<GamepadButton> buttons, float leftX = 0f, float leftY = 0f)
    {
      var held = new HashSet<GamepadButton>(buttons);
      _held = new GamepadState(_name, held, leftX, leftY);
    }

    /// <summary>The pad lies on the table, nothing held.</summary>
    public void Release() => _held = new GamepadState(_name, NullGamepadSource.EMPTY, 0f, 0f);

    public void Dispose()
    {
    }
  }
}
