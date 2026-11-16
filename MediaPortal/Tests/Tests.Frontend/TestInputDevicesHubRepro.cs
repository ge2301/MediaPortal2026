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
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using NUnit.Framework;
using MediaPortal.Frontend.Theming;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Views;

namespace Tests.Frontend
{
  /// <summary>
  /// Reproduces what the viewer sees on the real machine: the input devices page opened INSIDE the settings
  /// hub inside the shell window - a press must open the device's table, and Back must step one level at a
  /// time (table to chooser, chooser to wall), never pop the whole Settings frame.
  /// </summary>
  [TestFixture]
  public class TestInputDevicesHubRepro
  {
    private string _file = null!;

    [SetUp]
    public void SetUp()
    {
      _file = Path.Combine(Path.GetTempPath(), "mp2-repro-" + Guid.NewGuid().ToString("N") + ".json");
      Environment.SetEnvironmentVariable("MP2_KEYBINDINGS_FILE", _file);
      MediaPortal.Frontend.Settings.KeyBindings.Override(null);
    }

    [TearDown]
    public void TearDown()
    {
      MediaPortal.Frontend.Settings.KeyBindings.Override(null);
      Environment.SetEnvironmentVariable("MP2_KEYBINDINGS_FILE", null);
      if (File.Exists(_file))
        File.Delete(_file);
    }

    private static void Pump()
    {
      for (int i = 0; i < 10; i++)
        Dispatcher.UIThread.RunJobs();
    }

    private static ShellWindow HubWithEditor(out SettingsViewModel hub)
    {
      hub = new SettingsViewModel();
      hub.Add("Input devices", "Frontend", "editor", delegate
      {
        InputDevicesViewModel inputs = new InputDevicesViewModel();
        return new InputDevicesView { DataContext = inputs };
      });
      ShellWindow window = new ShellWindow(new SettingsView(hub));
      window.Show();
      Pump();
      return window;
    }

    [AvaloniaTest]
    public void PressOnTheChooserInsideTheHubOpensADeviceTable()
    {
      ShellWindow window = HubWithEditor(out SettingsViewModel hub);

      hub.Selected = hub.Sections[0];
      Pump();

      Assert.That(hub.CurrentView, Is.Not.Null, "the section opened");
      InputDevicesView? page = window.GetVisualDescendants().OfType<InputDevicesView>().FirstOrDefault();
      Assert.That(page, Is.Not.Null, "and the editor page stands in the window");

      // The chooser is the reference's DialogSelectDevice: it listens from the moment it stands up, and its
      // ONLY way is a press. Inside the hub a key must name "Keyboard" and open its table - not fall through
      // to the shell, which would leave the settings screen. Raised from the page itself, the way a focused
      // control inside it would carry the event up the window's road.
      page.Focus();
      Pump();
      InputDevicesViewModel model = (InputDevicesViewModel)page!.DataContext!;
      page.RaiseEvent(new KeyEventArgs
      {
        Key = Key.K,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = page,
      });
      Pump();

      Assert.That(model.Choosing, Is.False,
          "the press named its device and opened the table instead of falling to the shell");
      Assert.That(model.Device, Is.EqualTo("Keyboard"));

      window.Close();
    }

    [AvaloniaTest]
    public void TheTableOpenedByAPressStandsItsCaretOnTheFirstRow()
    {
      // Worn like the real machine, not bare: the shipped skin dresses every list with its own row template,
      // and a caret that only stands while no skin is on has never stood anywhere a viewer could see it.
      ShippedFaces.AnswerFromTheAssembly();
      Assert.That(SkinEngine.Apply(Avalonia.Application.Current!, "nereus", out string reason), Is.True, reason);

      ShellWindow window = HubWithEditor(out SettingsViewModel hub);

      hub.Selected = hub.Sections[0];
      Pump();
      InputDevicesView page = window.GetVisualDescendants().OfType<InputDevicesView>().First();
      InputDevicesViewModel model = (InputDevicesViewModel)page.DataContext!;

      // The chooser listens from the moment it stands up, like the reference's DialogSelectDevice. The
      // sniffer listens on the WINDOW now - the same place a real key's road always passes.
      window.RaiseEvent(new KeyEventArgs
      {
        Key = Key.K,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = window,
      });
      Pump();
      Assert.That(model.Choosing, Is.False, "the window's road carried the listening press");

      // Where does the keyboard stand once the table is on screen? The viewer must SEE it on the first row.
      Avalonia.Input.IInputElement? focused = window.FocusManager.GetFocusedElement();
      ListBox rows = page.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "rowsList");

      // A table whose rows have not been drawn cannot wear a caret; the real machine walks on while this is
      // still true, which is exactly the complaint. Say so plainly rather than pass on an empty container.
      Assert.That(rows.ContainerFromIndex(0), Is.Not.Null, "the first row of the table has been drawn");

      Assert.That(rows.Items.Count, Is.GreaterThan(0), "the table has rows");
      object? firstRow = rows.ContainerFromIndex(0);
      Assert.That(focused, Is.SameAs(firstRow),
          "the caret stands on the first row of the freshly opened table, not nowhere");

      window.Close();
    }

    [AvaloniaTest]
    public void AnArrowInATableMovesTheCaretWithoutArmingTheLearningDialog()
    {
      // The complaint this holds shut: pressing Down to navigate a table used to SELECT the row under the
      // caret, and selecting a row was the reference's BeginMapping - so navigation threw the viewer into
      // "press the key now". Arrows must walk the caret only; OK alone acts.
      ShellWindow window = HubWithEditor(out SettingsViewModel hub);

      hub.Selected = hub.Sections[0];
      Pump();
      InputDevicesView page = window.GetVisualDescendants().OfType<InputDevicesView>().First();
      InputDevicesViewModel model = (InputDevicesViewModel)page.DataContext!;
      model.Open(InputDevicesViewModel.GLOBAL_DEVICE);
      Pump();

      ListBox rows = page.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "rowsList");
      Assert.That(rows.SelectedIndex, Is.EqualTo(0), "the caret stands on the first row");

      rows.RaiseEvent(new KeyEventArgs
      {
        Key = Key.Down,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = rows,
      });
      Pump();

      Assert.That(rows.SelectedIndex, Is.EqualTo(1), "the arrow walked the caret one row down");
      Assert.That(model.Waiting, Is.Null, "and no learning dialog was armed by a navigation key");

      window.Close();
    }

    [AvaloniaTest]
    public void BackInATableStepsToTheChooserNotOutOfTheSettings()
    {
      ShellWindow window = HubWithEditor(out SettingsViewModel hub);

      hub.Selected = hub.Sections[0];
      Pump();
      InputDevicesView page = window.GetVisualDescendants().OfType<InputDevicesView>().First();
      InputDevicesViewModel model = (InputDevicesViewModel)page.DataContext!;

      model.Open(InputDevicesViewModel.GLOBAL_DEVICE);
      Pump();
      Assert.That(model.Choosing, Is.False);

      page.RaiseEvent(new KeyEventArgs
      {
        Key = Key.Escape,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = page,
      });
      Pump();

      Assert.That(model.Choosing, Is.True, "back stepped from the table to the chooser");
      Assert.That(hub.ShowingContents, Is.True, "and the hub still shows the editor, not its wall");

      window.Close();
    }
  }
}



