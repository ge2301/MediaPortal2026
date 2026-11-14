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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaPortal.Frontend.Api;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Views;
using MediaPortal.Frontend.Workflow;
using NUnit.Framework;

namespace Tests.Frontend
{
  /// <summary>
  /// The countings that stand on a screen of their own: the years and the genres a shelf's titles carry, reached
  /// from that shelf's own shortcut tile on the home wall.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The counting itself is the server's and is proven over in its own tests. What is proven here is the front
  /// end's half: that a shortcut tile asking for a counting lands on the counting's own screen instead of on a
  /// browse column or on a shelf with a menu to work through, that the server's order and the server's numbers
  /// reach the tiles, that a pressed value opens the <em>ordinary</em> shelf cut to it rather than a second kind
  /// of list, and that a shelf nobody tagged or a server that refuses is said rather than shown as a blank wall.
  /// </para>
  /// <para>
  /// No test awaits, for the reason spelled out in <c>TestMediaBrowser</c>; the tests pump the queue until the
  /// screen says it is done.
  /// </para>
  /// </remarks>
  [TestFixture]
  public class TestFilterScreens
  {
    #region Fixtures

    private static readonly Uri SIGN_IN_URL = new Uri("http://127.0.0.1:1/MediaPortal/Server/");

    private static readonly HomeSection MOVIES = new HomeSection("Movies",
        new AspectViewModel(new MediaAspect(new Guid("11111111-1111-1111-1111-111111111111"), "MovieItem",
            false, false, null, Array.Empty<AspectAttribute>())));

    /// <summary>
    /// A year counting whose order is deliberately not sorted by the screen's hand: 2009 has the most titles, the
    /// single 1979 film stands last, and the empty value the database answers with for "no year written" is in
    /// there because the client is what takes it out.
    /// </summary>
    private const string YEARS = "{\"aspect\":\"MovieItem\",\"by\":\"year\",\"groups\":["
        + "{\"value\":\"2009\",\"count\":37},{\"value\":\"1984\",\"count\":12},"
        + "{\"value\":\"1979\",\"count\":1},{\"value\":\"\",\"count\":9}]}";

    private const string GENRES = "{\"aspect\":\"MovieItem\",\"by\":\"genre\",\"groups\":["
        + "{\"value\":\"Drama\",\"count\":4},{\"value\":\"Action\",\"count\":2}]}";

    private const string AGES = "{\"aspect\":\"MovieItem\",\"by\":\"age\",\"groups\":["
        + "{\"value\":\"16\",\"count\":40},{\"value\":\"12\",\"count\":7}]}";

    private const string TITLES = "{\"total\":1,\"offset\":0,\"limit\":60,\"returned\":1,\"sort\":\"title\","
        + "\"by\":\"year\",\"value\":\"2009\",\"titles\":["
        + "{\"id\":\"00000001-0000-0000-0000-000000000000\",\"title\":\"Alien\",\"year\":1979}]}";

    /// <summary>A server that answers the counting URL with whatever it was handed and every titles URL with a shelf.</summary>
    private class CountingServer : HttpMessageHandler
    {
      private readonly string _groupsBody;

      internal string? LastGroupsUrl { get; private set; }
      internal string? LastTitlesUrl { get; private set; }

      internal CountingServer(string groupsBody = YEARS)
      {
        _groupsBody = groupsBody;
      }

      internal ServerApiClient Client()
      {
        return new ServerApiClient(SIGN_IN_URL, new HttpClient(this));
      }

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
          CancellationToken cancellation)
      {
        string path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/thumbnail", StringComparison.Ordinal))
          return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

        string body;
        if (path.EndsWith("/groups", StringComparison.Ordinal))
        {
          LastGroupsUrl = request.RequestUri.PathAndQuery;
          body = _groupsBody;
        }
        else
        {
          if (!path.Contains("/items/", StringComparison.Ordinal))
            LastTitlesUrl = request.RequestUri.PathAndQuery;
          body = TITLES;
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          RequestMessage = request,
          Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
      }
    }

    private static void Pump()
    {
      Dispatcher.UIThread.RunJobs();
      Thread.Sleep(5);
    }

    private static void WaitUntil(string because, Func<bool> condition)
    {
      DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
      while (DateTimeOffset.UtcNow < deadline)
      {
        Pump();
        if (condition())
          return;
      }
      Assert.Fail("Timed out after 15 seconds waiting for " + because);
    }

    private static FilterViewModel ScreenOn(CountingServer server, string by = "year")
    {
      return new FilterViewModel(server.Client(), MOVIES, by);
    }

    private static void Press(Control control, Key key)
    {
      control.RaiseEvent(new KeyEventArgs
      {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key,
        Source = control
      });
      Pump();
    }

    #endregion

    #region The counting on the screen

    [AvaloniaTest]
    public void TheCountingArrivesInTheServersOrderWithTheServersNumbers()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      Assert.That(server.LastGroupsUrl, Does.Contain("aspect=MovieItem").And.Contain("by=year"));
      Assert.That(values.Values.Select(value => value.Value), Is.EqualTo(new[] { "2009", "1984", "1979" }),
          "biggest first as the database counted and not re-sorted here - the order is part of the answer, and "
          + "the empty group the database answers with for \"nobody wrote a year\" is not a tile a viewer can stand on");
      Assert.That(values.Values.Select(value => value.Count), Is.EqualTo(new[] { 37, 12, 1 }),
          "the number on a tile is the server's, not one arrived at by paging through the shelf here");
      Assert.That(values.Selected, Is.Not.Null, "the first value is already stood on, because the arrows arrive "
          + "before any decision does");
      Assert.That(values.Status, Does.Contain("3 years among Movies"),
          "plural by hand: \"3 year\" is not a sentence");
    }

    [AvaloniaTest]
    public void TheScreenSaysWhichShelfAndWhichCountingItStandsOn()
    {
      CountingServer server = new CountingServer();
      FilterViewModel years = ScreenOn(server, "year");
      Assert.That(years.Heading, Is.EqualTo("Movies by year"),
          "the viewer came out of a shelf and the counting is the new word, so the shelf is said first");

      FilterViewModel genres = ScreenOn(server, "genre");
      Assert.That(genres.Heading, Is.EqualTo("Movies by genre"));
      Assert.That(genres.PluralWord, Is.EqualTo("genres"), "\"2 genre\" would be the screen's own slip");

      Assert.That(years.Subtitle, Does.Contain("press OK"),
          "a screen that sends the viewer's hand to the arrows explains what the arrows land on before they are pressed");
    }

    [AvaloniaTest]
    public void APressedValueHandsOverTheShelfCutToThatValue()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      var opened = new List<HomeSection>();
      values.ValueChosen += delegate (object? sender, HomeSection shelf) { opened.Add(shelf); };

      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      values.Selected = values.Values.First(value => value.Value == "1984");
      Assert.That(values.OpenCommand.CanExecute(null), Is.True);
      values.OpenCommand.Execute(null);

      Assert.That(opened, Has.Count.EqualTo(1), "standing on a value and pressing OK is a request to see its titles");
      Assert.That(opened[0].Label, Is.EqualTo("Movies"),
          "the screen that opens is the shelf's own, so the viewer knows where these titles came from");
      Assert.That(opened[0].Aspect.Name, Is.EqualTo("MovieItem"));
      Assert.That(opened[0].By, Is.EqualTo("year"));
      Assert.That(opened[0].Value, Is.EqualTo("1984"), "both halves of the cut travel, or neither");
    }

    [AvaloniaTest]
    public void TheCutOpensInTheOrdinaryShelfAndAsksTheServerForTheValue()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      HomeSection? handed = null;
      values.ValueChosen += delegate (object? sender, HomeSection shelf) { handed = shelf; };

      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });
      values.OpenCommand.Execute(null);

      Assert.That(handed, Is.Not.Null);
      MediaBrowserViewModel shelf = new MediaBrowserViewModel(server.Client(), handed!);
      shelf.OpenCommand.Execute(null);
      WaitUntil("the cut titles to arrive", delegate { return shelf.Titles.Count == 1; });

      Assert.That(server.LastTitlesUrl, Does.Contain("by=year&value=2009"),
          "the value goes into the ordinary titles query - a counting screen is a way into the shelf and never a "
          + "second list with its own rules");
      Assert.That(shelf.HasShelfFilter, Is.True,
          "and the shelf knows it is cut, which is what heads it with the value rather than with the shelf alone");
    }

    [AvaloniaTest]
    public void AShelfNobodyTaggedSaysSoRatherThanShowingAnEmptyWall()
    {
      CountingServer server = new CountingServer("{\"aspect\":\"MovieItem\",\"by\":\"year\",\"groups\":[]}");
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the answer to be read", delegate { return values.Status.StartsWith("None of the Movies"); });

      Assert.That(values.Values, Is.Empty);
      Assert.That(values.Status, Does.Contain("carries a year the importer wrote"),
          "the two possible nothings - no titles, or titles nobody tagged - name which one it is");
    }

    [AvaloniaTest]
    public void ARefusalFromTheServerIsSaidAndNotSwallowed()
    {
      CountingServer server = new CountingServer(
          "{\"aspect\":\"MovieItem\",\"by\":\"year\",\"error\":\"this shelf has no year\"}");
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the refusal to reach the status line",
          delegate { return values.Status == "this shelf has no year"; });

      Assert.That(values.Values, Is.Empty, "a refusal is not an empty shelf, and the screen does not dress it as one");
    }

    #endregion

    #region Which tile opens which screen

    [Test]
    public void OnlyTheYearAndTheGenreStandOnAScreenOfTheirOwn()
    {
      Assert.That(FilterViewModel.OwnsItsOwnScreen("year"), Is.True);
      Assert.That(FilterViewModel.OwnsItsOwnScreen("genre"), Is.True);
      Assert.That(FilterViewModel.OwnsItsOwnScreen("GENRE"), Is.True, "the server's words are caseless");
      Assert.That(FilterViewModel.OwnsItsOwnScreen("age"), Is.False,
          "the age rating answers a whole household with half a dozen values, which is a row of the shelf's own "
          + "sidebar and not a screen");
      Assert.That(FilterViewModel.OwnsItsOwnScreen("actors"), Is.False,
          "the people are a wall of their own, not a counting");
      Assert.That(FilterViewModel.OwnsItsOwnScreen(null), Is.False);

      Assert.That(FilterViewModel.StandsInTheShelfsSidebar("age"), Is.True);
      Assert.That(FilterViewModel.StandsInTheShelfsSidebar("year"), Is.False);
    }

    /// <summary>
    /// What the age tile's press lands on: the shelf itself, with its own sidebar stood open on the counted ages.
    /// The counting is the same answer the screens of one's own show - only fewer values of it - so it stays where
    /// the shelf already keeps it, and the tile's job is to save the viewer the walk to it.
    /// </summary>
    [AvaloniaTest]
    public void TheAgeRatingOpensInsideTheShelfsOwnSidebar()
    {
      CountingServer server = new CountingServer(AGES);
      MediaBrowserViewModel shelf = new MediaBrowserViewModel(server.Client(), MOVIES);

      shelf.ShowMediaMenu();
      _ = shelf.ShowFilterValuesAsync("age");
      WaitUntil("the ages to stand on the menu",
          delegate { return shelf.MediaMenu.Any(row => row.Label.StartsWith("16", StringComparison.Ordinal)); });

      Assert.That(server.LastGroupsUrl, Does.Contain("by=age"));
      Assert.That(shelf.MediaMenuVisible, Is.True,
          "the tile promised a sidebar that was already open, so the sidebar is open and not merely built");
      Assert.That(shelf.MediaMenu.Select(row => row.Label), Contains.Item("16  (40)"),
          "the values carry the server's number, as they do on a screen of their own");
      Assert.That(shelf.HasShelfFilter, Is.False,
          "standing the sidebar open counts the shelf; it cuts nothing until the viewer presses a value");
    }

    /// <summary>
    /// Every shortcut tile that asks for one of the two countings is answered by the screen that counting stands
    /// on, on every shelf the skin carries the tiles for. The caught failure this stands against is a shelf whose
    /// tile was written with a category of its own spelling, so that one shelf kept opening the browse columns
    /// while the other three opened the screen.
    /// </summary>
    [Test]
    public void EveryCountingTileOfEveryShelfIsAnsweredByItsScreen()
    {
      var model = new MediaPortal.Frontend.Skins.Nereus.NereusHomeMenuModel();
      var checked_tiles = 0;
      foreach (string action in new[]
               {
                 "80d2e2cc-baaa-4750-807b-f37714153751",  // movies
                 "30f57cba-459c-4202-a587-09fff5098251",  // series
                 "30715d73-4205-417f-80aa-e82f0834171f",  // records
                 "55556593-9fe9-436c-a3b6-a971e10c9d44",  // videos
                 "a4df2df6-8d66-479a-9930-d7106525eb07"   // videos, second action
               })
      {
        MainMenuEntry entry = FindEntry(action);
        foreach (MediaPortal.Frontend.Skins.Nereus.NereusHomeShortcut shortcut in model.ShortcutsFor(entry))
        {
          if (shortcut.Label != "Year" && shortcut.Label != "Genre")
            continue;

          checked_tiles++;
          Assert.That(FilterViewModel.OwnsItsOwnScreen(shortcut.BrowseBy), Is.True,
              "the " + shortcut.Label + " tile of " + action + " asks for a counting no screen claims, so the "
              + "press lands on nothing");
        }
      }

      Assert.That(checked_tiles, Is.GreaterThanOrEqualTo(8),
          "the year and the genre stand on four shelves' tiles; a home menu that lost them all is not what this "
          + "test was written against");
    }

    private static MainMenuEntry FindEntry(string actionId)
    {
      foreach (MainMenuEntry entry in MainMenu.Build(Array.Empty<AspectViewModel>(), connected: false))
      {
        if (string.Equals(entry.HomeImageId, actionId, StringComparison.OrdinalIgnoreCase))
          return entry;
      }

      Assert.Fail("No home action has image id " + actionId);
      return null!;
    }

    #endregion

    #region The wall

    [AvaloniaTest]
    public void TheTilesAreWalkedByTheArrowsAndOpenedByEnter()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      var workflow = new WorkflowManager();
      var opened = new List<HomeSection>();

      ShellWindow window = new ShellWindow(workflow, delegate (WorkflowFrame frame)
      {
        if (frame.StateId == "filter")
        {
          return new FilterView((FilterViewModel) frame.ViewModel, workflow,
              delegate (HomeSection shelf)
              {
                opened.Add(shelf);
                return new WorkflowFrame("titles", shelf.Label,
                    new MediaBrowserViewModel(server.Client(), shelf));
              });
        }
        return new MediaBrowserView((MediaBrowserViewModel) frame.ViewModel);
      });
      window.Width = 1600;
      window.Height = 900;
      workflow.Push(new WorkflowFrame("filter", values.Heading, values));
      window.Show();
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      ListBox wall = window.GetVisualDescendants().OfType<ListBox>()
          .First(list => list.Name == "valuesList");
      Assert.That(values.Selected!.Value, Is.EqualTo("2009"),
          "the wall opens stood on its biggest counting");

      Press(wall, Key.Right);
      Assert.That(values.Selected!.Value, Is.EqualTo("1984"), "the arrow walks a tile along the wall");

      Press(wall, Key.Left);
      Assert.That(values.Selected!.Value, Is.EqualTo("2009"), "and back again");

      Press(wall, Key.Up);
      Assert.That(values.Selected!.Value, Is.EqualTo("2009"),
          "at the head of the wall the arrow is nobody's; the caret keeps the tile it stood on");

      Press(wall, Key.Enter);
      Assert.That(opened, Has.Count.EqualTo(1), "OK opens the titles that carry the stood tile");
      Assert.That(opened[0].By, Is.EqualTo("year"));
      Assert.That(opened[0].Value, Is.EqualTo("2009"));

      window.Close();
    }

    #endregion
  }
}
