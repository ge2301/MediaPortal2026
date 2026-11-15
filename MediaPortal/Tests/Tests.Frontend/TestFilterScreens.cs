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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
    /// A shelf of music, which is the one shelf counted by the music genres: they are numbered by a table of their
    /// own, so id 3 is a different genre of music than it is a genre of film.
    /// </summary>
    private static readonly HomeSection MUSIC = new HomeSection("Music",
        new AspectViewModel(new MediaAspect(new Guid("33333333-3333-3333-3333-333333333333"), "AudioItem",
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

    /// <summary>
    /// The same genre counting with the numbers the library holds the genres under, which is what a library of
    /// tagged titles answers and what the reference's skin files its genre art by.
    /// </summary>
    private const string GENRES_NUMBERED = "{\"aspect\":\"MovieItem\",\"by\":\"genre\",\"groups\":["
        + "{\"value\":\"Drama\",\"count\":4,\"id\":18},{\"value\":\"Action\",\"count\":2,\"id\":5}]}";

    /// <summary>One numbered genre and one the importer wrote without a number, as a mixed library answers.</summary>
    private const string GENRES_HALF_NUMBERED = "{\"aspect\":\"MovieItem\",\"by\":\"genre\",\"groups\":["
        + "{\"value\":\"Drama\",\"count\":4,\"id\":18},{\"value\":\"Adventure\",\"count\":2}]}";

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

    /// <summary>The same screen, stood on another shelf: the counting is one thing and the shelf it counts is another.</summary>
    private static FilterViewModel ScreenOn(CountingServer server, HomeSection shelf, string by)
    {
      return new FilterViewModel(server.Client(), shelf, by);
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
    public void AGenreCarriesThePictureFiledUnderItsNumber()
    {
      CountingServer server = new CountingServer(GENRES_NUMBERED);
      FilterViewModel values = ScreenOn(server, "genre");
      values.ReadCommand.Execute(null);
      WaitUntil("the genres to arrive", delegate { return values.Values.Count == 2; });

      Assert.That(values.Values[0].Picture, Is.EqualTo("GenreVideo/Square/18"),
          "a genre's art is asked for by the number the library holds it under, in the folder the reference's skin "
          + "files it in: the words of a genre are translated and renamed by importers while its number is what the "
          + "library has always called it by, which is the only thing a picture can be named after durably");
      Assert.That(values.Values[1].Picture, Is.EqualTo("GenreVideo/Square/5"));
      Assert.That(values.ShowsPictures, Is.True,
          "the wall asks whether its values carry art before it dresses its first tile, so that no square is seen "
          + "changing its clothes under the viewer");

      Assert.That(values.DetailsPicture, Is.EqualTo("GenreVideo/Square/18"),
          "and the column of words speaks of the stood-on genre's picture too, which is where the art is shown "
          + "large - the way a shelf shows an album cover rather than a stamp");
      Assert.That(values.ShowsDetailsPicture, Is.True);

      values.Selected = values.Values[1];
      Assert.That(values.DetailsPicture, Is.EqualTo("GenreVideo/Square/5"),
          "the large picture follows the caret the same way the value's name does");
    }

    [AvaloniaTest]
    public void AYearIsItsOwnNumberAndWearsNoPicture()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      Assert.That(values.Values, Is.All.Property(nameof(BrowseValue.Picture)).Null,
          "a year is its own key and has no art filed under it; borrowing another value's picture would be a lie "
          + "about the film, not a decoration");
      Assert.That(values.ShowsPictures, Is.False, "a wall of years is a wall of words, which is what it always was");
      Assert.That(values.DetailsPicture, Is.Null,
          "the column has no picture to show large on a year wall either - only a genre's number names art");
      Assert.That(values.ShowsDetailsPicture, Is.False);
    }

    [AvaloniaTest]
    public void AGenreTheLibraryNeverNumberedIsStillCountedAndStandsWithoutArt()
    {
      // A library imported from bare filenames carries the words of a genre and never looked its number up. Such a
      // genre is as browsable as a numbered one; it simply has nothing to ask a picture by.
      CountingServer server = new CountingServer(GENRES_HALF_NUMBERED);
      FilterViewModel values = ScreenOn(server, "genre");
      values.ReadCommand.Execute(null);
      WaitUntil("the genres to arrive", delegate { return values.Values.Count == 2; });

      Assert.That(values.Values.Select(value => value.Value), Is.EqualTo(new[] { "Drama", "Adventure" }),
          "the unnumbered genre keeps its tile: a number is a key to a picture, not a right to be listed");
      Assert.That(values.Values[0].Picture, Is.EqualTo("GenreVideo/Square/18"));
      Assert.That(values.Values[1].Picture, Is.Null);
      Assert.That(values.ShowsPictures, Is.True,
          "the wall is a wall of pictures, and the one value without art stands with the counting's mark on its "
          + "face - the answer the people wall gives a name whose portrait never arrived");
    }

    [AvaloniaTest]
    public void AGenreWallTheSkinHasNoArtForIsStillAWallOfWords()
    {
      CountingServer server = new CountingServer(GENRES);
      FilterViewModel values = ScreenOn(server, "genre");
      values.ReadCommand.Execute(null);
      WaitUntil("the genres to arrive", delegate { return values.Values.Count == 2; });

      Assert.That(values.ShowsPictures, Is.False,
          "with no number to ask by, the wall wears the words it wore before it ever heard of genre art");
    }

    [AvaloniaTest]
    public void AWallOfMusicAsksTheMusicGenresArt()
    {
      // The music genres are numbered by a table of their own, so the same number means a different genre on the
      // two shelves; a wall of records wearing the film genres' art would hang the wrong picture on the right word.
      CountingServer server = new CountingServer(GENRES_NUMBERED);
      FilterViewModel values = ScreenOn(server, MUSIC, "genre");
      values.ReadCommand.Execute(null);
      WaitUntil("the genres to arrive", delegate { return values.Values.Count == 2; });

      Assert.That(values.Values[0].Picture, Is.EqualTo("GenreAudio/Square/18"));
      Assert.That(server.LastGroupsUrl, Does.Contain("aspect=AudioItem"),
          "the shelf that is counted is the shelf the art is asked for, and not the films next door");
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

      Assert.That(years.CategoryIcon, Is.EqualTo("MediaMenu/year"),
          "the wall wears the mark the home tile of this counting wore, which is the one place the front end "
          + "spells out what kind of counting a screen of values is");
      Assert.That(genres.CategoryIcon, Is.EqualTo("MediaMenu/genre"));
    }

    [AvaloniaTest]
    public void TheWallWearsTheShelfsOwnRailAndTheRailSaysWhatThisScreenCanDo()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      values.ShowMediaMenu();

      Assert.That(values.MediaMenuVisible, Is.True);
      Assert.That(values.MediaMenuHeading, Is.EqualTo("Menu"),
          "the commands page is what every rail of this front end opens with, and the rail's walking is written "
          + "against that word");
      Assert.That(values.MediaMenu.Select(row => row.Label),
          Is.EqualTo(new[] { "All Movies", "Filter", "By count", "View" }),
          "the rail offers what this screen can really do and nothing else: the shelf the counting came out of, "
          + "the other counting, the order, the shape");

      MediaBrowserMenuEntry filter = values.MediaMenu.First(row => row.Label == "Filter");
      Assert.That(filter.Detail, Is.EqualTo("Year"), "the row says which counting the wall is wearing");

      // Climbing down over the shape row is what every other rail does: the commands page is left standing behind.
      filter.Command.Execute(null);
      Assert.That(values.MediaMenuHeading, Is.EqualTo("Filter"));
      Assert.That(values.MediaMenu.Select(row => row.Label), Is.EqualTo(new[] { "Genre", "Year" }),
          "the two countings stand in the order the shelf's own tiles stand them");
      Assert.That(values.MediaMenu.First(row => row.Label == "Year").IsCurrent, Is.True,
          "the standing counting is marked as the shelf marks its own current filter");
    }

    [AvaloniaTest]
    public void TheCommandsRowForTheWholeShelfHandsOverTheShelfWithoutACut()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      var opened = new List<HomeSection>();
      values.ValueChosen += delegate (object? sender, HomeSection shelf) { opened.Add(shelf); };

      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      values.ShowMediaMenu();
      values.MediaMenu.First(row => row.Label == "All Movies").Command.Execute(null);

      Assert.That(opened, Has.Count.EqualTo(1));
      Assert.That(opened[0].By, Is.Null, "the whole shelf carries no cut");
      Assert.That(opened[0].Label, Is.EqualTo("Movies"));
      Assert.That(values.MediaMenuVisible, Is.False, "a rail standing over a shelf the viewer just opened is a rail in the way");
    }

    [AvaloniaTest]
    public void TheViewPageTurnsTheWallIntoRowsAndBack()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      values.ShowMediaMenu();
      values.MediaMenu.First(row => row.Label == "View").Command.Execute(null);
      Assert.That(values.MediaMenu.Select(row => row.Label), Is.EqualTo(new[] { "List", "Grid", "Details on" }),
          "the same three answers the shelf's and the people wall's view page gives, in the same order, and the "
          + "column stands from the first value as it does there");
      Assert.That(values.MediaMenu.First(row => row.Label == "Grid").IsCurrent, Is.True);

      values.MediaMenu.First(row => row.Label == "List").Command.Execute(null);
      Assert.That(values.Layout, Is.EqualTo(FilterViewModel.LAYOUT_LIST));
      Assert.That(values.MediaMenuHeading, Is.EqualTo("View"), "the page stays where the viewer climbed down to");
      Assert.That(values.MediaMenu.First(row => row.Label == "List").IsCurrent, Is.True);

      values.MediaMenu.First(row => row.Label == "Details on").Command.Execute(null);
      Assert.That(values.ShowDetails, Is.False);
      Assert.That(values.MediaMenu.Select(row => row.Label), Does.Contain("Details off"),
          "a row that answers a switch says which way it stands, as every other row of this front end does");
    }

    [AvaloniaTest]
    public void TheSortPageTurnsTheWallAroundWithoutAskingTheServerAgain()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });
      string first_url = server.LastGroupsUrl!;

      values.ShowMediaMenu();
      values.MediaMenu.First(row => row.Label == "By count").Command.Execute(null);
      Assert.That(values.MediaMenuHeading, Is.EqualTo("Sort"));

      values.MediaMenu.First(row => row.Label == "By count").Command.Execute(null);
      Assert.That(values.SortDescending, Is.False, "pressing the standing order again turns it around");
      Assert.That(values.Values.Select(value => value.Count), Is.EqualTo(new[] { 1, 12, 37 }));

      values.MediaMenu.First(row => row.Label == "By year").Command.Execute(null);
      Assert.That(values.Values.Select(value => value.Value), Is.EqualTo(new[] { "2009", "1984", "1979" }),
          "a year reads newest first, which is the end a viewer already knows");
      Assert.That(server.LastGroupsUrl, Is.EqualTo(first_url),
          "one answer from the server holds every value of a shelf, so re-ordering it is a question the screen "
          + "answers for itself rather than one it asks twice");
    }

    [AvaloniaTest]
    public void TheFilterPageAsksForTheOtherWallOfTheSameShelf()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      var asked = new List<string>();
      values.CountingChosen += delegate (object? sender, string by) { asked.Add(by); };

      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      values.ShowMediaMenu();
      values.MediaMenu.First(row => row.Label == "Filter").Command.Execute(null);
      values.MediaMenu.First(row => row.Label == "Year").Command.Execute(null);
      Assert.That(asked, Is.Empty, "pressing the counting the wall already wears asks for nothing");

      values.MediaMenu.First(row => row.Label == "Genre").Command.Execute(null);
      Assert.That(asked, Is.EqualTo(new[] { "genre" }),
          "the other counting is a wall of its own, so the screen that can build a wall is asked to build it");
      Assert.That(values.MediaMenuVisible, Is.False);
    }

    [AvaloniaTest]
    public void TheColumnOfWordsSpeaksOfTheValueTheCaretStandsOn()
    {
      CountingServer server = new CountingServer();
      FilterViewModel values = ScreenOn(server);
      values.ReadCommand.Execute(null);
      WaitUntil("the years to arrive", delegate { return values.Values.Count == 3; });

      Assert.That(values.DetailsName, Is.EqualTo("2009"),
          "the column names the value the wall stands on, as the shelf's column names the title its caret is on");
      Assert.That(values.ShowsSideDetails && values.ShowsDetailsWords, Is.True,
          "the column is up from the first value, which is how a shelf and a wall of names open");
      Assert.That(values.DetailsFacts.Select(fact => fact.Label), Is.EqualTo(new[] { "Titles", "Shelf" }),
          "the answers the tile cannot hold stand in the shelf's own pairs");
      Assert.That(values.DetailsFacts[0].Value, Is.EqualTo("37"));
      Assert.That(values.ShowsContentInfo, Is.False,
          "the line at the foot only speaks when the column is turned away");

      values.ShowDetails = false;
      Assert.That(values.ShowsContentInfo, Is.True);
      Assert.That(values.ContentInfo, Does.Contain("2009").And.Contain("37 titles"),
          "the viewer turned the column off, not the knowledge");
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

    /// <summary>
    /// Every media shelf's Search tile is recognised as one, on every arm of the cross that carries it.  The
    /// caught failure this stands against is a search tile whose key no press answers - the tile lit under the
    /// hand, said its sentence about not being available, and opened nothing (viewer's word 2026-11-15: the
    /// shortcut entries have to be finalised).  The music arm is named here because it only gained its tile with
    /// this fix, and an arm that quietly lost it again would leave a record shelf with no way onto its field.
    /// </summary>
    [Test]
    public void EveryMediaShelfCarriesASearchTileAndThePressKnowsIt()
    {
      var model = new MediaPortal.Frontend.Skins.Nereus.NereusHomeMenuModel();
      var searched_shelves = new List<string>();
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
          if (shortcut.Label != "Search")
            continue;

          searched_shelves.Add(action);
          Assert.That(HomeView.IsSearchShortcut(shortcut.ScreenKey), Is.True,
              "the Search tile of " + action + " wears a key the press does not read as a search, so it opens "
              + "nothing but a sentence");
        }
      }

      Assert.That(searched_shelves, Has.Count.EqualTo(5),
          "every media arm of the skin's cross carries its search tile, the reference's arms included");
    }

    /// <summary>
    /// What the Search tile's press lands on: the shelf itself, standing with its own search field open, which is
    /// the same door the shelf's sidebar row and its Find key walk through.  A screen of its own for searching was
    /// never built and was never wanted - the shelf has the field already.
    /// </summary>
    [AvaloniaTest]
    public void TheSearchTileOpensItsShelfWithTheFieldStoodOpen()
    {
      CountingServer server = new CountingServer();
      var home = new HomeViewModel(server.Client());
      MainMenuEntry movies = FindEntry("80d2e2cc-baaa-4750-807b-f37714153751");
      movies.Section = MOVIES;
      home.SelectedEntry = movies;

      // The shortcut column the entry asks for, as the skin lays it out: without the worn templates no square
      // exists to be pressed, so the skin goes on the way the application puts it on.  A band is added only so
      // that the wall has ground under the squares.
      Application app = Application.Current!;
      string? previousFolder = MediaPortal.Frontend.Theming.SkinEngine.SkinsFolder;
      string previousTheme = MediaPortal.Frontend.Theming.FrontendTheme.CurrentName(app);
      MediaPortal.Frontend.Theming.SkinEngine.SkinsFolder = Path.Combine(
          Path.GetDirectoryName(typeof(HomeView).Assembly.Location)!, "Skins");
      var workflow = new WorkflowManager();
      MediaBrowserViewModel? shelf = null;
      try
      {
        MediaPortal.Frontend.Theming.FrontendTheme.Apply(app, "nereus", out string? _);
        MediaPortal.Frontend.Theming.SkinEngine.Apply(app, "nereus", out string? _);
        home.ShowContentAsync(movies).GetAwaiter().GetResult();
        home.ContentGroups.Add(new HomeContentGroup("Latest added", "square1", 760, 4));

        ShellWindow window = new ShellWindow(workflow, delegate (WorkflowFrame frame)
        {
          if (frame.ViewModel is MediaBrowserViewModel browser)
          {
            shelf = browser;
            return new MediaBrowserView(browser);
          }
          return new HomeView(home, workflow,
              sectionScreen: delegate (HomeSection section)
              {
                return new WorkflowFrame("titles", section.Label,
                    new MediaBrowserViewModel(server.Client(), section));
              });
        });
        workflow.Push(new WorkflowFrame("home", "Home", home));
        window.Width = 1920;
        window.Height = 1080;
        window.Show();
        WaitUntil("the shortcut squares to stand",
            delegate
            {
              return window.GetVisualDescendants().OfType<Button>()
                  .Any(button => button.Tag is HomeContentTile worn && worn.IsShortcut);
            });

        // The press the wall's shortcut square raises: the same routed Click a hand or Enter produces, raised on
        // the Search square of the movies arm itself, so that the handler the markup wires is the code tested.
        Button tile = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.Tag is HomeContentTile worn && worn.IsShortcut
                && worn.Shortcut != null && HomeView.IsSearchShortcut(worn.Shortcut.ScreenKey));

        // The shelf the arm stands on has to be there for the press to walk onto it, which is what connecting to
        // a server does; the entry that survives the rebuild is a fresh object though, so its section - which the
        // real reading fills from the server's aspects - is put on it by hand again.
        home.UseServer(server.Client());
        MainMenuEntry standing = home.SelectedEntry!;
        standing.Section = MOVIES;

        tile.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent, tile));
        Pump();

        Assert.That(shelf, Is.Not.Null, "the search tile walked onto a shelf");
        Assert.That(workflow.Current!.StateId, Is.EqualTo("titles"),
            "the shelf stands on the screen, not merely built behind the home");
        Assert.That(shelf!.SearchOpen, Is.True,
            "and it wears its search field open, as the sidebar's Search row puts it on - the tile saves the "
            + "viewer the walk to the row, nothing more");
        window.Close();
      }
      finally
      {
        MediaPortal.Frontend.Theming.SkinEngine.SkinsFolder = previousFolder;
        MediaPortal.Frontend.Theming.FrontendTheme.Apply(app, previousTheme, out string? _);
      }
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

    /// <summary>
    /// The square a value is drawn in is chosen by what the values carry: a genre wall whose genres the library
    /// numbered is a wall of pictures with a word under each, worn as the people wall wears its portraits, while a
    /// wall of years keeps the square of words it has always worn.  Both are asked of the same live wall, because
    /// the choice is made while the counting arrives and not by the markup.
    /// </summary>
    [AvaloniaTest]
    public void AWallWearsTheSquareItsValuesAreDrawnIn()
    {
      List<ShellWindow> shown = new List<ShellWindow>();
      ListBox WallOf(CountingServer server, string by, HomeSection shelf)
      {
        FilterViewModel values = ScreenOn(server, shelf, by);
        var workflow = new WorkflowManager();
        ShellWindow window = new ShellWindow(workflow, delegate (WorkflowFrame frame)
        {
          return new FilterView((FilterViewModel) frame.ViewModel, workflow);
        });
        window.Width = 1600;
        window.Height = 900;
        workflow.Push(new WorkflowFrame("filter", values.Heading, values));
        window.Show();
        WaitUntil("the counting to arrive", delegate { return values.Values.Count > 0; });
        shown.Add(window);
        return window.GetVisualDescendants().OfType<ListBox>().First(list => list.Name == "valuesList");
      }

      ListBox genres = WallOf(new CountingServer(GENRES_NUMBERED), "genre", MOVIES);
      Assert.That(genres.GetVisualDescendants().OfType<Image>(), Is.Not.Empty,
          "the genres of a tagged library are drawn as pictures, as the reference drew its genre tiles and as the "
          + "people wall draws a portrait - the picture is asked of the worn skin by name, so a skin without the "
          + "art simply leaves the square dark rather than breaking it");
      Assert.That(
          genres.GetVisualDescendants().OfType<TextBlock>().Any(word => word.Text == "Drama"), Is.True,
          "and the word stays under the picture: a wall of pictures whose viewer cannot tell which genre is which "
          + "is a decoration, not a menu");

      ListBox years = WallOf(new CountingServer(), "year", MOVIES);
      Assert.That(years.GetVisualDescendants().OfType<Image>(), Is.Empty,
          "a year has no art filed under it and its wall is the wall of words it always was");
      Assert.That(
          years.GetVisualDescendants().OfType<TextBlock>().Any(word => word.Text == "2009"), Is.True);

      foreach (ShellWindow window in shown)
        window.Close();
    }

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

      // Left off the leading edge of the wall is the road to the screen's own menu here too, and the leading edge
      // is the whole first column: the rail slides out of the two hundred points that stand off the room's left
      // hand and the room travels out of its way, which is the shelf's move and the people wall's.
      Press(wall, Key.Left);
      Assert.That(values.MediaMenuVisible, Is.True,
          "the wall's own menu comes out at the wall's leading edge, as the shelf's does");
      ListBox rail = window.GetVisualDescendants().OfType<ListBox>().First(list => list.Name == "mediaMenuList");
      WaitUntil("the rail to be dressed", delegate { return rail.Items.Count == 4; });

      Press(rail, Key.Right);
      Assert.That(values.MediaMenuVisible, Is.False, "and Right hands the caret back to the wall");

      Press(wall, Key.Enter);
      Assert.That(opened, Has.Count.EqualTo(1), "OK opens the titles that carry the stood tile");
      Assert.That(opened[0].By, Is.EqualTo("year"));
      Assert.That(opened[0].Value, Is.EqualTo("2009"));

      window.Close();
    }

    /// <summary>
    /// A value pointed at is opened by the pointer, not merely selected: the wall answered a remote and not a
    /// hand until the tap was heard where every other wall of this front end hears it - on the bubble, after the
    /// press has settled into a selection.  A tunnel tap fires before the ListBox has marked its item selected, so
    /// the first press of a session moved the caret and opened nothing (viewer's word 2026-11-14: the filter
    /// values "do not work" when pressed).
    /// </summary>
    [AvaloniaTest]
    public void TheTilesAreOpenedByThePointerAsWellAsByEnter()
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

      // The second row's tile, asked for out of the realized containers rather than by index arithmetic, because
      // which container holds which value is the panel's business, not the test's.
      ListBoxItem row = wall.GetVisualDescendants().OfType<ListBoxItem>()
          .First(item => item.DataContext is BrowseValue stood && stood.Value == "1984");

      // A pointer never presses a whole tile: it presses the deepest thing under it, here the value's own writing.
      // The tap is handed over the way the people wall's test hands one over (TestTheWallOfPeople) - a headless
      // frame drives no mouse - with the source deep inside the tile, as a real tap arrives.
      TextBlock word = row.GetVisualDescendants().OfType<TextBlock>()
          .First(t => t.IsVisible && t.Bounds.Height > 4);
      var pressing = new PointerPressedEventArgs(word, null!, word,
          new Point(word.Bounds.Width / 2, word.Bounds.Height / 2), 1,
          new PointerPointProperties(), KeyModifiers.None)
      {
        RoutedEvent = InputElement.PointerPressedEvent
      };
      word.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, pressing) { Source = word });
      Pump();

      Assert.That(values.Selected!.Value, Is.EqualTo("1984"),
          "the press puts the caret on the value the pointer hit");
      Assert.That(opened, Has.Count.EqualTo(1),
          "and the same press opens the titles that carry it - a wall that only selects under a hand and never "
          + "opens is the bug this test was written for");
      Assert.That(opened[0].By, Is.EqualTo("year"));
      Assert.That(opened[0].Value, Is.EqualTo("1984"));

      window.Close();
    }

    #endregion
  }
}
