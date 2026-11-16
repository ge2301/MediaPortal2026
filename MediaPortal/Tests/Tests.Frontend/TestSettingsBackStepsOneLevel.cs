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
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using NUnit.Framework;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Views;

namespace Tests.Frontend
{
  /// <summary>
  /// The viewer's complaint on the real machine: settings -&gt; a folder ("Media library") -&gt; a page inside it
  /// ("FanArt cache"), then BACK - and instead of the folder's wall they stand on Home. Held here: one press of
  /// Back spends exactly one step of the hub's trail, whichever control inside the open page holds the keyboard.
  /// </summary>
  [TestFixture]
  public class TestSettingsBackStepsOneLevel
  {
    private static void Pump()
    {
      for (int i = 0; i < 10; i++)
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTest]
    public void BackFromAPageInsideAFolderLandsOnTheFoldersWallNotHome()
    {
      SettingsViewModel hub = new SettingsViewModel();
      hub.AddFolder("Media library", "Frontend", string.Empty, delegate
      {
        return SettingsViewModel.PagesHere(
            new SettingsSection("FanArt cache", "Frontend", string.Empty,
                delegate { return new TextBlock { Name = "fanartPage" }; }),
            new SettingsSection("Import settings", "Server", string.Empty,
                delegate { return new TextBlock { Name = "importPage" }; }));
      });
      ShellWindow window = new ShellWindow(new SettingsView(hub));
      window.Show();
      Pump();

      // Into the folder, onto its wall...
      _ = hub.OpenAsync(hub.Sections[0]);
      Pump();
      Assert.That(hub.IsWall, Is.True, "the folder opened as a wall of pages");
      Assert.That(hub.Location, Does.Contain("Media library"));

      // ...into a page beside it. The page is a plain control that claims no key, exactly like the FanArt page.
      SettingsSection page = hub.Sections.First(s => s.Name == "FanArt cache");
      _ = hub.OpenAsync(page);
      Pump();
      Assert.That(hub.ShowingContents, Is.True, "the page stands beside the lists");

      // The viewer presses Back. On the real machine the keyboard stands INSIDE the page (the hub hands it to
      // the open screen), so the key's road starts there - raised from the window only covers a listener hung
      // on the window itself, which is not what the hub has.
      Control pageView = hub.CurrentView!;
      pageView.RaiseEvent(new KeyEventArgs
      {
        Key = Key.Escape,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = pageView,
      });
      Pump();

      Assert.That(hub.ShowingContents, Is.False, "back left the page");
      Assert.That(hub.Location, Does.Contain("Media library"),
          "and landed on the folder's wall, not on the first level and not out of the settings");
      Assert.That(hub.Sections.Any(s => s.Name == "FanArt cache"), Is.True,
          "the folder's pages are still what the viewer sees");

      // One more Back steps out of the folder onto the first level...
      Control wallNow = hub.CurrentView ?? (Control)window;
      wallNow.RaiseEvent(new KeyEventArgs
      {
        Key = Key.Escape,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = wallNow,
      });
      Pump();
      Assert.That(hub.Location, Is.EqualTo("Settings"), "back from the folder reaches the first level");

      // ...and the third Back is the shell's: the hub has nowhere left, so the frame pops.
      Assert.That(hub.GoBack(), Is.False, "the hub says it is out of levels");

      window.Close();
    }

    [AvaloniaTest]
    public void BackFromAPageHeldByAFocusableControlStillStepsOneLevel()
    {
      // The real page's keyboard often stands ON a control inside it (a list, a button). The key's road then
      // starts deeper in the tree; the hub must still hear the unclaimed Back before the shell does.
      SettingsViewModel hub = new SettingsViewModel();
      Button inside = new Button { Content = "a page's own button" };
      // Two pages, so the folder is a real level and Back has a step to spend. (A door of ONE page is walked
      // through without entering its level - SkipForSingleItem - and its Back lands on the level the door
      // stood on, which is right but has no folder wall to name.)
      hub.AddFolder("Media library", "Frontend", string.Empty, delegate
      {
        return SettingsViewModel.PagesHere(
            new SettingsSection("FanArt cache", "Frontend", string.Empty, delegate { return inside; }),
            new SettingsSection("Import settings", "Server", string.Empty,
                delegate { return new TextBlock { Name = "importPage" }; }));
      });
      ShellWindow window = new ShellWindow(new SettingsView(hub));
      window.Show();
      Pump();

      _ = hub.OpenAsync(hub.Sections[0]);
      Pump();
      _ = hub.OpenAsync(hub.Sections.First(s => s.Name == "FanArt cache"));
      Pump();

      // The hub hands the keyboard to the opened screen when it can; a headless window may not carry it there
      // on its own, so the keyboard is stood where a real viewer's would stand - on the page's own control.
      inside.Focus();
      Pump();
      Assert.That(inside.IsFocused, Is.True, "the keyboard stands on the page's own control");

      inside.RaiseEvent(new KeyEventArgs
      {
        Key = Key.Escape,
        RoutedEvent = InputElement.KeyDownEvent,
        Source = inside,
      });
      Pump();

      Assert.That(hub.ShowingContents, Is.False, "back stepped out of the page");
      Assert.That(hub.Location, Does.Contain("Media library"), "onto the folder's wall");

      window.Close();
    }
  }
}
