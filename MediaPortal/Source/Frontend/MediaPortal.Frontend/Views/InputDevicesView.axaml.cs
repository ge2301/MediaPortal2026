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
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.ViewModels;
using ActionRow = MediaPortal.Frontend.ViewModels.InputDevicesViewModel.ActionRow;

namespace MediaPortal.Frontend.Views
{
  /// <summary>
  /// The mapping editor as the settings hub embeds it: one frame whose lists the arrows can walk through all
  /// three states - choosing a device, walking its table, and waiting for the re-binding press. Built the way
  /// the System page is built; the hub hands the view model over as DataContext.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The caret works the way every other list of this front end works, which is the way the reference's TV-list
  /// dialogs work: the row the viewer stands on is the SELECTED row (the skin paints its sheet only there),
  /// the arrow keys move that selection, and OK is the deliberate act on the stood-on row. Before this was so,
  /// the lists were kept selection-free and the keyboard merely focused a row - invisible in Nereus, which
  /// paints nothing on a focused-but-unselected row - and the platform's own arrow handling selected rows as
  /// a side effect of walking, so a viewer pressing Down saw "the first entry got chosen" and was thrown into
  /// the learning dialog by a navigation key.
  /// </para>
  /// </remarks>
  public partial class InputDevicesView : UserControl
  {
    private readonly List<Control> _ring = new List<Control>();
    /// <summary>While the page moves the caret itself, the list's own answer to the same key must stay out.</summary>
    private bool _walkingTheCaret;

    /// <summary>For the XAML loader, the designer and the hub, which sets the real view model as DataContext.</summary>
    public InputDevicesView()
    {
      InitializeComponent();

      _ring.Add(rowsList);
      DpadNavigation.Attach(this, _ring);

      // The table hears its arrow and OK keys on the TUNNEL road before the platform's list handling turns an
      // arrow into a selection and SelectionChanged into an action: here the arrows move the caret and OK alone
      // acts on the row it stands on.
      rowsList.AddHandler(KeyDownEvent, OnTableKeysTunnel, RoutingStrategies.Tunnel);

      // Opening the section opens the chooser while the page stands in a window: the hub builds it detached and
      // swaps it in later, and asking the hub for the keyboard from a control in no window asks nobody.
      AttachedToVisualTree += delegate
      {
        InputDevicesViewModel? model = DataContext as InputDevicesViewModel;
        if (model == null)
          return;
        // Subscribed once over -=/+= because every re-entry to the page would otherwise add the same listener
        // a second time.
        model.ShapeChanged -= AskHubForTheKeyboard;
        model.ShapeChanged += AskHubForTheKeyboard;

        // The window is one layout pass away even from here - the hub has just been swapped into the shell's
        // content host - so the asking repeats each pass until the page really stands in a window. Bounded
        // like everywhere else here.
        StandWhenInWindow(model, CARET_TRIES);
      };
      Unloaded += delegate
      {
        (DataContext as InputDevicesViewModel)?.CancelCapture();
      };
    }

    /// <summary>How often the asking repeats while the page is not in a window yet.</summary>
    private const int CARET_TRIES = 6;

    /// <summary>
    /// Opens the chooser once this page really stands inside a window - which arms its sniffer, because the
    /// reference's DialogSelectDevice listened from the first moment and had nothing else on screen. Until the
    /// page is in a window there is no key road to hang a listener on, so the asking repeats a bounded number
    /// of layout passes.
    /// </summary>
    private void StandWhenInWindow(InputDevicesViewModel model, int tries)
    {
      if (TopLevel.GetTopLevel(this) is Window window)
      {
        // The hub may have opened another screen in the meantime; arming an old page's model then would arm
        // a sniffer nobody can see and claim the keyboard from wherever the viewer actually stands.
        SettingsView? hub = this.GetVisualAncestors().OfType<SettingsView>().FirstOrDefault();
        if (hub == null || !ReferenceEquals(hub.OpenedScreen, this))
          return;
        model.Reload(window);
        AskHubForTheKeyboard();
        return;
      }

      if (tries <= 0)
        return;

      Dispatcher.UIThread.Post(
          delegate
          {
            if (DataContext is InputDevicesViewModel same && ReferenceEquals(same, model))
              StandWhenInWindow(model, tries - 1);
          },
          DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Tells the settings hub which control of this page wants the keyboard - the table, when one stands on
    /// screen - and stands the caret (the selection) on its first row, where the reference's dialogs stand too.
    /// The chooser asks for nothing: it has no list to stand on, only the picture and the listening press.
    /// </summary>
    private void AskHubForTheKeyboard()
    {
      InputDevicesViewModel? model = DataContext as InputDevicesViewModel;
      if (model == null || model.Choosing)
        return;

      SettingsView? hub = this.GetVisualAncestors().OfType<SettingsView>().FirstOrDefault();
      hub?.AskFocusFrom(rowsList);
      StandOnFirstRow(rowsList, 4);
    }

    /// <summary>
    /// Stands the caret on the first row: the list takes the keyboard, and the selection is laid on the row
    /// itself - the skin paints its sheet only on the selected row, so a mere focus is invisible on Nereus.
    /// The laying happens without the page's own handlers seeing it: standing on a row is not an act on it.
    /// Row containers are drawn only after a layout, so the asking repeats a bounded number of passes.
    /// </summary>
    private void StandOnFirstRow(ListBox list, int tries)
    {
      if (!list.IsEffectivelyVisible || list.Items.Count == 0)
        return;

      _walkingTheCaret = true;
      try
      {
        list.SelectedIndex = 0;
      }
      finally
      {
        _walkingTheCaret = false;
      }
      if (list.ContainerFromIndex(0) is Control row && row.IsEffectivelyVisible)
      {
        WalkSelection(list, 0);
        row.Focus();
        return;
      }

      if (tries <= 0)
        return;

      list.Focus();
      Dispatcher.UIThread.Post(delegate { StandOnFirstRow(list, tries - 1); }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Moves the caret of a list to a row: the selection travels with it (that is what the skin paints) and
    /// the keyboard stands on the row. The handler above guards the list's own SelectionChanged so that
    /// walking never reads as an act on the row.
    /// </summary>
    private void WalkSelection(ListBox list, int to)
    {
      _walkingTheCaret = true;
      try
      {
        list.SelectedIndex = to;
        if (list.ContainerFromIndex(to) is Control row && row.IsEffectivelyVisible)
          row.Focus();
      }
      finally
      {
        _walkingTheCaret = false;
      }
    }

    /// <summary>
    /// The table's keys: arrows walk the action list and OK starts the "press the key now" step for the row the
    /// caret stands on (the reference's SelectAction page). Back is left to the page's own handler below.
    /// </summary>
    private void OnTableKeysTunnel(object? sender, KeyEventArgs e)
    {
      InputDevicesViewModel? model = DataContext as InputDevicesViewModel;
      if (model == null || model.Choosing || model.Waiting != null || e.Handled)
        return;

      if (Settings.KeyBindings.Current.Is(Settings.KeyAction.Select, e))
      {
        if (rowsList.SelectedItem is ActionRow row)
        {
          e.Handled = true;
          model.BeginMapping(row);
        }
        return;
      }

      WalkArrows(rowsList, e);
    }

    /// <summary>Moving the caret of a vertical list with Up/Down; Left/Right stay the ring's (and the wall's).</summary>
    private void WalkArrows(ListBox list, KeyEventArgs e)
    {
      int stood = list.SelectedIndex;
      if (stood < 0)
        return;

      int to = e.Key switch
      {
        Key.Up => stood - 1,
        Key.Down => stood + 1,
        _ => -2
      };

      if (to == -2)
        return;
      if (to < 0 || to >= list.Items.Count)
      {
        e.Handled = true;   // the list ends that way; the key must not fall through as a selection elsewhere
        return;
      }

      e.Handled = true;
      if (list.ContainerFromIndex(to) is Control next && next.IsEffectivelyVisible)
      {
        WalkSelection(list, to);
        return;
      }

      // A row the virtualized list has not drawn yet is asked for by number: the scroll realizes it and the
      // caret steps onto it a layout later, with the key kept meanwhile.
      list.ScrollIntoView(to);
      RealizeAndStand(list, to, CARET_TRIES);
    }

    private void RealizeAndStand(ListBox list, int index, int tries)
    {
      if (list.ContainerFromIndex(index) is Control row && row.IsEffectivelyVisible)
      {
        WalkSelection(list, index);
        return;
      }

      if (tries <= 0)
        return;

      Dispatcher.UIThread.Post(
          delegate { RealizeAndStand(list, index, tries - 1); },
          DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The table's act on the stood-on row - the reference's BeginMapping. Guard: only a pointer's press
    /// of a row reaches it, never a caret step onto it.
    /// </summary>
    private void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
      if (_walkingTheCaret || !ReferenceEquals(e.Source, rowsList))
        return;

      InputDevicesViewModel? model = DataContext as InputDevicesViewModel;
      if (model == null || !model.Editing)
        return;
      if (rowsList.SelectedItem is ActionRow row)
        model.BeginMapping(row);
    }

    /// <summary>The reference's DeleteMapping button, beside an entry that can fall back to the global table.</summary>
    private void OnClear(object? sender, RoutedEventArgs e)
    {
      Button? button = sender as Button;
      ActionRow? row = button?.DataContext as ActionRow;
      (DataContext as InputDevicesViewModel)?.ClearMapping(row);
    }

    /// <summary>
    /// The page's answer to the keys no list holds the caret for, heard on the TUNNEL road before any control
    /// inside the page. While the chooser listens (which is its whole life, like the reference's dialog), every
    /// key is the listening press: it names its device and stops being anybody else's key - otherwise the same
    /// Enter would also be the shell's Back or a wall step behind the viewer's back. Back in a table steps to
    /// the chooser; Back on the chooser is left to the hub, which folds the screen back into its wall.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
      base.OnKeyDown(e);

      InputDevicesViewModel? model = DataContext as InputDevicesViewModel;
      if (model == null || e.Handled || e.Key == Key.None)
        return;

      if (!model.Choosing && model.Waiting == null
          && Settings.KeyBindings.Current.Is(Settings.KeyAction.Back, e))
      {
        e.Handled = true;
        model.BackToChooser();   // raises ShapeChanged, which re-asks for the keyboard on the chooser
        return;
      }

      // The listening press of the chooser's "press any button" step names its device and stops being
      // anybody else's key - with ONE exception: Back. The reference's dialog was a dialog, and a dialog's
      // cancel is never swallowed by its own listener; here Back closes the screen through the hub, which is
      // the viewer's way out. Swallowing it left the chooser a dead end with no door.
      if (model.Choosing && model.Listening)
      {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl || e.Key == Key.LeftAlt || e.Key == Key.RightAlt
            || e.Key == Key.LeftShift || e.Key == Key.RightShift)
          return;
        if (Settings.KeyBindings.Current.Is(Settings.KeyAction.Back, e))
          return;   // not ours at all: the hub takes it and folds the screen back into its wall
        e.Handled = true;
        model.OpenFromListeningPress(e.Key);
        return;
      }

      // The learning dialog's key: the sniffer took it off the wire already; here it merely loses the rest
      // of its road, so capturing a key never doubles as Back, a navigation step, or a channel change. Back
      // cancels the wait instead of being captured - the reference's DialogMapAction had a Cancel button for
      // exactly this way out.
      if (model.Waiting != null)
      {
        if (Settings.KeyBindings.Current.Is(Settings.KeyAction.Back, e))
        {
          e.Handled = true;
          model.CancelCapture();
        }
        else
        {
          e.Handled = true;   // the captured key travels no further
        }
      }
    }
  }
}
