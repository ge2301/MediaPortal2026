using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;
using MediaPortal.Frontend.Theming;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Views;

namespace Tests.Frontend
{
  /// <summary>
  /// The caret of the keyboard stands on a plate of the settings wall the moment the viewer arrives -
  /// without pressing anything. The viewer reads "the front end is dead" from a wall nobody stands on,
  /// and every earlier regression of this promise landed on exactly that complaint.
  /// </summary>
  public class TestSettingsCaretStandsOnArrival
  {
    private     static void Pump(int rounds = 30)
    {
      for (int i = 0; i < rounds; i++)
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTest]
    public void ArrivingAtTheHubStandsTheCaretOnTheFirstPlate()
    {
      // The television wears the Nereus skin; its templates draw the plate containers, and whether a plate
      // can be REALIZED (drawn at all) is exactly what decides if the caret has anywhere to stand. Testing
      // without the skin would test a wall that never exists on the machine.
      Assert.That(SkinEngine.Apply(Avalonia.Application.Current!, "nereus", out string reason), Is.True, reason);

      SettingsViewModel hub = new SettingsViewModel();
      hub.Add("General", "test", "", () => new TextBlock { Text = "general" });
      hub.Add("Media library", "test", "", () => new TextBlock { Text = "library" });

      SettingsView view = new SettingsView(hub);
      ShellWindow window = new ShellWindow(view);
      window.Show();
      Pump(30);

      ListBox wall = view.GetVisualDescendants().OfType<ListBox>().First();
      int realized = 0;
      for (int i = 0; i < wall.Items.Count; i++)
      {
        if (wall.ContainerFromIndex(i) is ListBoxItem)
          realized++;
      }

      bool anyPlateHolds = wall.GetVisualDescendants().OfType<ListBoxItem>().Any(item => item.IsKeyboardFocusWithin);

      Assert.That(realized, Is.GreaterThan(0), "the wall's plates got drawn");
      Assert.That(anyPlateHolds, Is.True,
          "arriving at the settings hands the keyboard to a PLATE of the wall, not merely the list");

      window.Close();
    }
  }
}
