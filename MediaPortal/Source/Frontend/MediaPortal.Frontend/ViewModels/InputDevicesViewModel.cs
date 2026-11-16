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
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.Settings;

namespace MediaPortal.Frontend.ViewModels
{
  /// <summary>
  /// The input devices as the reference ships them: a mapping EDITOR, not a listing. The page opens with the
  /// reference's "press any button on the device you want to configure" step, walks into that device's table
  /// of actions - each row showing what answers today, with Change and Clear beside it - and re-binds through
  /// the "press the key now" learning dialog. Everything is written straight into keybindings.json, the one
  /// store the front end already reads.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Three states, mirroring the reference's three screens: <see cref="Choosing"/> (its DialogSelectDevice),
  /// the per-device table (its SelectAction page) and <see cref="Waiting"/> (its DialogMapAction). The
  /// sniffer only observes the window's key road plus the pad's poll, so listening never answers a press;
  /// writing goes through the file, never through a second in-memory truth.
  /// </para>
  /// <para>
  /// What a row shows is the answer that WOULD hold: for a device its own entry where it has bound the
  /// action, otherwise the merged global answer - exactly how <see cref="KeyBindings.ForDevice"/> resolves a
  /// live press.
  /// </para>
  /// </remarks>
  public sealed class InputDevicesViewModel : ObservableObject
  {
    /// <summary>The name a keyboard-shaped press wears, which is the name the sniffer gives it too. It doubles
    /// as the machine's own table: a live key answers from the file's top level unless a device has entries of
    /// its own (KeyBindings.ForDevice), so editing "Keyboard" edits what every unnamed device obeys.</summary>
    public const string GLOBAL_DEVICE = "Keyboard";

    private readonly KeyBindings _defaults = KeyBindings.TheDefaults();
    private InputSniffer? _sniffer;
    private Control? _listenOn;

    public ObservableCollection<string> Devices { get; } = new ObservableCollection<string>();

    /// <summary>One row per action while a device is open.</summary>
    public ObservableCollection<ActionRow> Rows { get; } = new ObservableCollection<ActionRow>();

    public string Heading { get; } = "Input devices";

    /// <summary>What the page asks right now (the chooser's line, or the table's header).</summary>
    private string _subtitle = string.Empty;
    public string Subtitle
    {
      get { return _subtitle; }
      private set { SetProperty(ref _subtitle, value); }
    }

    private bool _choosing = true;
    /// <summary>Whether the press-any-button step is shown instead of the table.</summary>
    public bool Choosing
    {
      get { return _choosing; }
      private set
      {
        if (SetProperty(ref _choosing, value))
        {
          OnPropertyChanged(nameof(Editing));
        }
      }
    }

    /// <summary>Whether the device's action table is shown.</summary>
    public bool Editing
    {
      get { return !_choosing && _waiting == null; }
    }

    private string? _waiting;
    /// <summary>Set while the learning dialog waits for one press; its line is what the dialog prints.</summary>
    public string? Waiting
    {
      get { return _waiting; }
      private set
      {
        if (SetProperty(ref _waiting, value))
          OnPropertyChanged(nameof(Editing));
      }
    }

    /// <summary>The device whose table is open.</summary>
    public string Device { get; private set; } = GLOBAL_DEVICE;

    private ActionRow? _capturing;

    /// <summary>For the XAML loader and the hub, which hands a real model over as DataContext.</summary>
    public InputDevicesViewModel()
    {
    }

    /// <summary>
    /// The chooser's line - the reference's DialogSelectDevice help word for word, because that dialog had one
    /// way only: press something on the device you mean. There is no list beside it to contradict it.
    /// </summary>
    private const string CHOOSE_LINE = "Press a key/button on the input device you would like to configure.";

    /// <summary>
    /// Raised whenever the page swaps the list it shows - chooser to table and back - so the view can re-ask
    /// the hub for the keyboard on the list that is now visible.
    /// </summary>
    public event Action? ShapeChanged;

    /// <summary>
    /// Whether the chooser listens for a press naming its device. The reference's DialogSelectDevice listened
    /// from the moment it stood up and had nothing else on it, so this does too: while the chooser is on
    /// screen the next press names its device. There is no list to walk here whose arrows a listener could eat.
    /// </summary>
    private bool _listening = true;
    public bool Listening
    {
      get { return _listening; }
      private set { SetProperty(ref _listening, value); }
    }

    /// <summary>
    /// Opens the chooser: exactly the reference's DialogSelectDevice - one help line, the "press" picture, and
    /// a sniffer armed from the first moment. When <paramref name="listenOn"/> is given the next press names
    /// its device and opens its table; coming back from a table re-arms the step, because on the reference the
    /// dialog simply stood there again. Without a control the page still works headless because a caller
    /// without a window has nothing to hear a press on.
    /// </summary>
    public void Reload(Control? listenOn)
    {
      StopListening();
      CapturingRow(null);
      _listenOn = listenOn;
      Choosing = true;
      Device = GLOBAL_DEVICE;
      Rows.Clear();
      RebuildDeviceList();
      Subtitle = CHOOSE_LINE;
      ShapeChanged?.Invoke();

      if (_listenOn != null)
      {
        _sniffer = new InputSniffer(_listenOn);
        Listening = true;
        ListenForDevice();
      }
    }

    /// <summary>
    /// Opens one device's table. "Change" and "Clear" work without pressing anything else. The reference names
    /// a keyboard-shaped press "Keyboard", so the table that opens for one says "Keyboard" too - and since a
    /// live key answers from the file's top-level table unless a device has its own entry (see
    /// KeyBindings.ForDevice), that table is the machine's defaults: what a viewer who pressed an unknown
    /// remote or a plain keyboard lands in, exactly as agreed.
    /// </summary>
    public void Open(string device)
    {
      if (string.IsNullOrEmpty(device))
      {
        OpenGlobal();
        return;
      }

      StopListening();
      CapturingRow(null);
      Device = device;
      Choosing = false;
      Subtitle = string.Equals(device, GLOBAL_DEVICE, StringComparison.OrdinalIgnoreCase)
          ? "Every device answers this table unless it has entries of its own. Changes are saved to "
              + "keybindings.json beside the server settings file."
          : device + " answers its own entries first and this machine's table for the rest. Changes are "
              + "saved to keybindings.json beside the server settings file.";
      RebuildRows();
      ShapeChanged?.Invoke();
    }

    /// <summary>
    /// The machine's own table - the file's top level, which every device falls back to. Reached by a press no
    /// sniffer could name a device for, never offered as a choice of its own.
    /// </summary>
    private void OpenGlobal()
    {
      StopListening();
      CapturingRow(null);
      Device = GLOBAL_DEVICE;
      Choosing = false;
      Subtitle = "Every device answers this table unless it has an entry of its own. Changes are saved to "
          + "keybindings.json beside the server settings file.";
      RebuildRows();
      ShapeChanged?.Invoke();
    }

    /// <summary>Back to the chooser from the table or the dialog, listening for the next device again.</summary>
    public void BackToChooser()
    {
      Reload(_listenOn);
    }

    /// <summary>
    /// The reference's BeginMapping: the next press becomes this action's key for this device.
    /// </summary>
    public void BeginMapping(ActionRow row)
    {
      if (row == null || _sniffer == null)
        return;
      CapturingRow(row);
      Waiting = "Press the button that should do " + row.Action + ". Esc cancels.";
      _sniffer.ListenOnce(delegate (InputSniffer.Capture capture)
      {
        // Back keeps its right to cancel the wait rather than becoming a binding behind the viewer's back -
        // on any device, and for every action but Back itself, which may legitimately be re-bound. The key
        // is read off the wire here, so this answers whichever spelling the file gives Back today.
        Key pressed;
        if (!Enum.TryParse(capture.KeyName, true, out pressed))
          pressed = Key.None;
        if (!string.Equals(row.Action, "Back", StringComparison.OrdinalIgnoreCase)
            && KeyBindings.Current.ForDevice(Device).Is(KeyAction.Back, pressed))
        {
          CapturingRow(null);
          return;
        }
        WriteBinding(row.Action, capture.KeyName);
        CapturingRow(null);
        RebuildRows();
        ShapeChanged?.Invoke();
      });
    }

    /// <summary>The reference's DeleteMapping: this action falls back to the global answer for this device.</summary>
    public void ClearMapping(ActionRow row)
    {
      if (row == null)
        return;
      KeyBindings file = KeyBindings.Load(null, out _);
      if (Device == GLOBAL_DEVICE)
      {
        // The global table always has an answer - its own or the shipped default's - and clearing one is
        // writing none: an empty list says "nobody answers this", which is what a viewer means by clearing.
        file.Bindings[row.Action] = new List<string>();
        SaveAndReread(file);
      }
      else
      {
        KeyBindings target = TableOfFile(file);
        target.Bindings.Remove(row.Action);
        if (target.Bindings.Count == 0)
          file.Devices.Remove(Device);    // a device entry with nothing of its own is no entry at all
        SaveAndReread(file);
      }
      RebuildRows();
    }

    /// <summary>Cancels a waiting learning dialog.</summary>
    public void CancelCapture()
    {
      _sniffer?.Cancel();
      CapturingRow(null);
    }

    private ICommand? _cancelMappingCommand;
    /// <summary>The reference's Cancel button of the learning dialog, wired to the page for the viewer who
    /// would rather tap than press Escape.</summary>
    public ICommand CancelMappingCommand
    {
      get { return _cancelMappingCommand ??= new RelayCommand(CancelCapture); }
    }

    private void ListenForDevice()
    {
      if (_sniffer == null)
        return;
      _sniffer.ListenOnce(delegate (InputSniffer.Capture capture)
      {
        // The press that named the device also travels the ordinary road and is answered there once; the
        // sniffer merely read it off the wire. Whether or not it steered something, its device is now open -
        // and the chooser never listens again this visit, so the arrows of a viewer who comes back can walk
        // the list instead of naming devices.
        Listening = false;
        Open(capture.DeviceName);
      });
    }

    /// <summary>
    /// The view's own hearing of the listening press: a key that reached this page through the tunnel road
    /// names "Keyboard" here and stops being anybody else's key (see InputDevicesView.OnKeyDown). The pad's
    /// presses keep coming through the sniffer's peek instead; whichever route reports first wins, because
    /// the step waits for exactly one press.
    /// </summary>
    public void OpenFromListeningPress(Key key)
    {
      if (!Choosing || !Listening)
        return;
      StopListening();
      Listening = false;
      Open("Keyboard");
    }

    /// <summary>
    /// The devices the file knows, kept for callers that want to know what exists (nothing on the chooser
    /// itself shows them - the reference's DialogSelectDevice had no list; a device opens only by being
    /// pressed). The global pseudo-device is not in here: it edits the file's top-level table.
    /// </summary>
    private void RebuildDeviceList()
    {
      Devices.Clear();
      KeyBindings current = KeyBindings.Current;
      foreach (KeyValuePair<string, KeyBindings> entry in current.Devices)
      {
        if (!string.Equals(entry.Key, GLOBAL_DEVICE, StringComparison.OrdinalIgnoreCase))
          Devices.Add(entry.Key);
      }
    }

    private void RebuildRows()
    {
      Rows.Clear();
      KeyBindings merged = KeyBindings.Merged(KeyBindings.Load(null, out _), _defaults, out _);
      // "Keyboard" is the file's top level (see GLOBAL_DEVICE): its rows are that table resolved over the
      // shipped defaults, not a device section.
      bool perDevice = Device != GLOBAL_DEVICE;
      KeyBindings resolved = perDevice ? EffectiveTable(merged, Device) : merged;
      KeyBindings? own = perDevice ? TableOf(merged, Device) : null;
      foreach (KeyAction action in Enum.GetValues<KeyAction>())
      {
        IReadOnlyList<Key> keys = resolved.KeysFor(action);
        bool overridden = own != null && own.Bindings.ContainsKey(action.ToString());
        Rows.Add(new ActionRow(this, action.ToString(),
            keys.Count > 0 ? string.Join(", ", keys) : "nobody mapped this one", overridden));
      }
    }

    private void WriteBinding(string action, string keyName)
    {
      KeyBindings file = KeyBindings.Load(null, out _);
      KeyBindings target = TableOfFile(file);
      target.Bindings[action] = new List<string> { keyName };
      SaveAndReread(file);
    }

    /// <summary>The table an edit for the open device belongs in: the device's own inside the file, or the
    /// file's global table itself. Edits lay over the file as it stands, never over the merged defaults, so
    /// saving never bakes the defaults into the viewer's file.</summary>
    private KeyBindings TableOfFile(KeyBindings file)
    {
      if (Device == GLOBAL_DEVICE)
        return file;
      // The name goes in exactly as the chooser heard it from the device, because that is the name a live
      // press will be looked up with; a hand-written entry under another spelling stays what it always was.
      KeyBindings? own;
      if (!file.Devices.TryGetValue(Device, out own) || own == null)
      {
        own = new KeyBindings();
        file.Devices[Device] = own;
      }
      return own;
    }

    /// <summary>The device's own table within <paramref name="table"/>, or null when it has none.</summary>
    private static KeyBindings? TableOf(KeyBindings table, string device)
    {
      KeyBindings? own;
      return table.Devices.TryGetValue(device, out own) ? own : null;
    }

    /// <summary>
    /// The device's effective table for display: its own entries laid over the machine's merged table, which is
    /// how ForDevice resolves a live press - so Clear on a row visibly falls back to the global answer instead
    /// of vanishing into nothing. A device with no entry of its own answers the merged table outright.
    /// </summary>
    private static KeyBindings EffectiveTable(KeyBindings merged, string device)
    {
      KeyBindings? own = TableOf(merged, device);
      if (own == null)
        return merged;

      // Copy the merged answers, then lay the device's names over them verbatim - including an empty list,
      // which means "this device answers nothing here" and must survive to KeysFor unchanged.
      KeyBindings effective = new KeyBindings();
      foreach (KeyValuePair<string, List<string>> entry in merged.Bindings)
        effective.Bindings[entry.Key] = new List<string>(entry.Value);
      foreach (KeyValuePair<string, List<string>> entry in own.Bindings)
        effective.Bindings[entry.Key] = entry.Value ?? new List<string>();
      return effective;
    }

    private void SaveAndReread(KeyBindings edited)
    {
      bool saved;
      string said = edited.Save(null, out saved);
      Console.WriteLine("bindings: " + said);
      if (saved)
        KeyBindings.Override(null);    // the running front end picks the file up fresh on next ask
      RebuildDeviceList();
    }

    private void CapturingRow(ActionRow? row)
    {
      if (_capturing != null)
        _capturing.Capturing = false;
      _capturing = row;
      if (row != null)
        row.Capturing = true;
      if (row == null)
        Waiting = null;
    }

    private void StopListening()
    {
      _sniffer?.Cancel();
    }

    /// <summary>One action's row: its name, what answers it today, and the two commands the reference gave a
    /// row (BeginMapping, started here by the row's OK, and the Clear button's DeleteMapping).</summary>
    public sealed class ActionRow : ObservableObject
    {
      private readonly InputDevicesViewModel _owner;

      internal ActionRow(InputDevicesViewModel owner, string action, string keys, bool overridden)
      {
        _owner = owner;
        Action = action;
        Keys = keys;
        Overridden = overridden;
      }

      public string Action { get; }
      public string Keys { get; }

      /// <summary>Whether this device carries its own entry for the action rather than answering the global table.</summary>
      public bool Overridden { get; }

      private bool _capturing;
      public bool Capturing
      {
        get { return _capturing; }
        internal set { SetProperty(ref _capturing, value); }
      }

      public void BeginMapping()
      {
        _owner.BeginMapping(this);
      }

      private ICommand? _clearMappingCommand;

      /// <summary>The Clear button's command, so any skin can wire the row without code of its own.</summary>
      public ICommand ClearMappingCommand
      {
        get { return _clearMappingCommand ??= new RelayCommand(() => _owner.ClearMapping(this)); }
      }

      public void ClearMapping()
      {
        _owner.ClearMapping(this);
      }
    }
  }
}
