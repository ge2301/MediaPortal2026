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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaPortal.Frontend.Api;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaPortal.Frontend.ViewModels
{
  /// <summary>
  /// One counting of one shelf, stood on a screen of its own: the years its titles carry, or the genres.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>Why a screen and not the shelf's sidebar.</b> A shortcut tile on the home wall is already a decision -
  /// the viewer standing in front of their films who presses "Year" has said which shelf and which counting, and
  /// has said it before the shelf was even read. Answering that with the whole shelf plus a menu makes them say
  /// it a second time, in a smaller voice, one screen deeper. The walls of people were given a screen of their
  /// own for exactly this reason, and a counting of a shelf is the same kind of question: a list of what there
  /// is, each row standing for the titles underneath it.
  /// </para>
  /// <para>
  /// <b>The counts are the server's.</b> One query counts a shelf by a category (<c>GET api/media/groups</c>), and
  /// the number in the answer is the database's, not one arrived at by fetching the shelf sixty pages deep here.
  /// The answer also arrives in the server's order - biggest group first - and is kept in it, because that order
  /// is a statement about this library and a screen that re-sorted it alphabetically would quietly unpick the one
  /// piece of judgement the counting involved.
  /// </para>
  /// <para>
  /// <b>What pressing a value does.</b> It opens the ordinary shelf, cut to that value: the same
  /// <see cref="MediaBrowserViewModel"/>, the same paging, the same remembered order and shape, and the walk back
  /// leads here rather than out of the library. This screen is a way into a list and never a second list with its
  /// own rules, which is also why it holds no selection of its own once the value was pressed.
  /// </para>
  /// <para>
  /// <b>How the screen looks.</b> Like every other media screen of this front end, because it is one: the ground
  /// and the two bands, the rail of its own menu down the left edge, the wall of tiles in the middle and the
  /// column of words at its right hand end are the shelf's furniture, worn from the shelf's own files exactly as
  /// the walls of people wear them (Views/FilterView.axaml).  Nothing here invents a look, a heading over the
  /// wall, or a sentence explaining the screen: the head above the wall stands empty, and what a value is worth
  /// is said in the column beside it.
  /// </para>
  /// </remarks>
  public sealed partial class FilterViewModel : ObservableObject, Views.IMenuRailOwner
  {
    /// <summary>
    /// The two countings a shelf's shortcut tile answers on a screen of its own.
    /// </summary>
    /// <remarks>
    /// The rest of the categories the server counts stay in the shelf's own sidebar. The people are not in this
    /// list because they are not a counting - they have their walls - and the age rating stays in the sidebar
    /// because a household's films answer it with half a dozen values, which is a row of a menu and not a screen.
    /// </remarks>
    private static readonly string[] OWN_SCREEN_CATEGORIES = { "genre", "year" };

    /// <summary>
    /// The counting whose values stay in the shelf's sidebar, and whose shortcut therefore opens the shelf with
    /// that sidebar stood open on them.
    /// </summary>
    public const string SIDEBAR_CATEGORY = "age";

    private readonly ServerApiClient? _client;
    private CancellationTokenSource? _running;

    /// <param name="client">The server the counting is asked of, or null for a screen with nothing to read.</param>
    /// <param name="shelf">The shelf that is being counted, as the home tile named it.</param>
    /// <param name="by">The category to count it by, in the server's word: <c>year</c> or <c>genre</c>.</param>
    public FilterViewModel(ServerApiClient? client, HomeSection shelf, string by)
    {
      _client = client;
      Shelf = shelf;
      Category = (by ?? "").Trim();
      if (_client == null)
        Status = "There is no server to count " + Shelf.Label + " by " + Word + ".";
    }

    /// <summary>
    /// Whether this counting is one of the two that stand on a screen of their own, which is the question the
    /// home screen asks a shortcut tile's category before it decides which way to send the viewer.
    /// </summary>
    public static bool OwnsItsOwnScreen(string? by)
    {
      foreach (string owned in OWN_SCREEN_CATEGORIES)
      {
        if (string.Equals(owned, by?.Trim(), StringComparison.OrdinalIgnoreCase))
          return true;
      }
      return false;
    }

    /// <summary>
    /// Whether this counting is the one whose values are few enough to stay in the shelf's sidebar, which the home
    /// screen answers by opening the shelf with its menu stood open on them.
    /// </summary>
    public static bool StandsInTheShelfsSidebar(string? by)
    {
      return string.Equals(SIDEBAR_CATEGORY, by?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The shelf being counted, which is also the shelf a pressed value opens.</summary>
    public HomeSection Shelf { get; }

    /// <summary>The category being counted, in the server's word.</summary>
    public string Category { get; }

    /// <summary>What the screen calls the category: "year", "genre".</summary>
    public string Word
    {
      get
      {
        if (string.Equals(Category, "genre", StringComparison.OrdinalIgnoreCase))
          return "genre";
        if (string.Equals(Category, "year", StringComparison.OrdinalIgnoreCase))
          return "year";
        return Category;
      }
    }

    /// <summary>
    /// The mark this counting wears, which is the one the home tile of the counting wore.  A genre wears a picture
    /// of its own whenever the library numbered it and the worn skin carries art for that number; everything else
    /// - every year, and every genre whose importer wrote the words without the number - wears this mark, which is
    /// what stands in the square a shelf's row would have stood its cover in.
    /// </summary>
    public string CategoryIcon { get { return "MediaMenu/" + Word; } }

    /// <summary>
    /// Where the art for one genre of a shelf of films, series or videos stands: the folder the reference's skin
    /// files it in, with the genre's number as its file name.
    /// </summary>
    public const string PICTURE_FOLDER_VIDEO = "GenreVideo/Square";

    /// <summary>Where the art for one genre of a shelf of music stands, numbered by the music genres' own table.</summary>
    public const string PICTURE_FOLDER_AUDIO = "GenreAudio/Square";

    /// <summary>
    /// The folder this shelf's genres are filed in, which is the music one for a shelf of albums or tracks and the
    /// video one for everything else: the two kinds of genre are numbered by two different tables, so id 3 is a
    /// different genre of music than it is a genre of film, and a wall that read the video art for its records
    /// would hang the wrong picture on the right word.
    /// </summary>
    public string PictureFolder
    {
      get { return MediaCatalog.IsMusic(Shelf.Aspect.Name) ? PICTURE_FOLDER_AUDIO : PICTURE_FOLDER_VIDEO; }
    }

    /// <summary>
    /// The name of the picture one counted value is filed under, or null when nothing could be asked a picture by.
    /// </summary>
    /// <remarks>
    /// The number, not the words, because that is the reference's arrangement and the only durable one: "Adventure"
    /// and "Abenteuer" are one genre to a viewer and two rows to a database, and a picture named after the words
    /// would have to be kept twice and renamed every time a scraper changed them.  Years are their own number and
    /// carry none of this, so a year wall is a wall of words, as it should be.
    /// </remarks>
    private string? PictureOf(MediaGroup group)
    {
      // Only the genre counting is answered with numbers, and only a genre wall wears art; the check is here rather
      // than left to the server because a category that invents a number tomorrow should not grow pictures on a
      // wall of years by accident.
      if (!group.Id.HasValue || !string.Equals(Word, "genre", StringComparison.OrdinalIgnoreCase))
        return null;
      return PictureFolder + "/" + group.Id.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The category's word in the plural the word actually takes, because "38 genres" is the sort of slip a screen
    /// should not make in its first sentence and the plurals here are not one rule.
    /// </summary>
    public string PluralWord
    {
      get
      {
        string word = Word;
        return word.Equals("genre", StringComparison.OrdinalIgnoreCase) ? "genres" : word + "s";
      }
    }

    /// <summary>
    /// What the screen is called: the shelf and the counting, in that order, because the viewer came out of that
    /// shelf and the counting is the new word. "Movies by year", the same words the shelf's own menu rows wear.
    /// </summary>
    /// <remarks>
    /// The words are the frame's title and the traces' headline; they stand above the wall nowhere, because no
    /// shelf of titles and no wall of names stands under a caption (PeopleView's head is empty for this reason).
    /// </remarks>
    public string Heading { get { return Shelf.Label + " by " + Word; } }

    /// <summary>The values this shelf's titles carry, biggest first, with the number that justifies each row.</summary>
    public ObservableCollection<BrowseValue> Values { get; } = new ObservableCollection<BrowseValue>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private BrowseValue? _selected;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Whether any value of this wall came with a picture, which is the question the screen asks itself before it
    /// chooses the square it draws its values in: a wall of genres with art is a wall of pictures with a word under
    /// each, while a wall of years - or a genre wall on a skin that carries no art - is a wall of words.
    /// </summary>
    /// <remarks>
    /// Asked of the whole wall rather than of one value, because a wall is read as one thing: the squares of one
    /// wall all stand the same way, and a screen that grew a picture into its third tile while the first two stayed
    /// words would read as a mistake in the wall rather than as a gap in the art.
    /// </remarks>
    [ObservableProperty]
    private bool _showsPictures;

    [ObservableProperty]
    private string _status = "Not read yet.";

    /// <summary>
    /// Raised when a value was pressed: the application owns the windows, so it decides that this means the
    /// ordinary shelf cut to that value. What is handed over is a shelf of the same label and the same aspect
    /// wearing both halves of the cut, which is the one shape the titles screen and the API agree on.
    /// </summary>
    public event EventHandler<HomeSection>? ValueChosen;

    /// <summary>Counts the shelf. The screen asks for this when it opens; nothing else changes what is counted.</summary>
    [RelayCommand]
    private async Task ReadAsync()
    {
      if (_client == null)
        return;

      // One count at a time, for the same reason the shelf of titles cancels its own questions: were this screen
      // ever to count two categories in a row, answers arriving in any order would leave the last-written one
      // standing under a heading that named the other.
      CancellationTokenSource? previous = _running;
      CancellationTokenSource cancellation = new CancellationTokenSource();
      _running = cancellation;
      previous?.Cancel();

      IsBusy = true;
      try
      {
        MediaGroups answer = await _client.GetGroupsAsync(Shelf.Aspect.Name, Category, cancellation.Token);

        Values.Clear();
        foreach (MediaGroup group in answer.Values)
          Values.Add(new BrowseValue(group.Value!, group.Count, PictureOf(group)));

        // Stood before the first value is selected, because it decides the square every value is drawn in, and a
        // wall that dressed its tiles after the caret had already stood on one would be seen doing it.
        ShowsPictures = Values.Any(value => value.ShowsPicture);
        Selected = Values.Count > 0 ? Values[0] : null;

        if (!string.IsNullOrEmpty(answer.Error))
          Status = answer.Error;
        else if (Values.Count == 0)
        {
          // The honest nothing: the shelf exists and the category exists, but no title in it carries a value.
          // Naming both halves says which of the two the viewer is short of.
          Status = "None of the " + Shelf.Label + " carries a " + Word + " the importer wrote.";
        }
        else
          Status = Values.Count + " " + PluralWord + " among " + Shelf.Label + ".";
      }
      catch (ServerApiException ex)
      {
        if (ex.StatusCode == 403)
          Status = Shelf.Label + " is only for signed-in users on this server. Sign in on the server screen.";
        else if (ex.NotReady)
          Status = "The server is not ready yet: " + ex.Message;
        else
          Status = ex.Message;
      }
      catch (OperationCanceledException)
      {
        // A newer count was asked for; it reports its own status.
      }
      catch (Exception ex)
      {
        Status = "Counting the " + PluralWord + " failed: " + ex.Message;
      }
      finally
      {
        if (ReferenceEquals(_running, cancellation))
        {
          _running = null;
          IsBusy = false;
        }
        cancellation.Dispose();
      }
    }

    private bool CanOpen { get { return Selected != null; } }

    /// <summary>Opens the titles that carry the stood value, in the shelf they were counted out of.</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
      BrowseValue? value = Selected;
      if (value == null)
        return;

      // Both halves travel together or not at all - a category whose value went missing reads the whole shelf
      // under a heading naming one value, and the server refuses that, so the screen refuses it first.
      ValueChosen?.Invoke(this, new HomeSection(Shelf.Label, Shelf.Aspect, Category, value.Value));
    }

    // ─────────────────────────────  THE RAIL  ─────────────────────────────
    //
    // Every media screen of this front end wears the same rail down its left edge, and this screen is a media
    // screen: the plates are MediaBrowserMenuEntry rows drawn by the shelf's own MediaMenuTile, and the three
    // questions a rail asks of the screen it hangs in are answered here exactly as the shelf and the walls of
    // people answer them.  What the rail has to offer is whatever this screen can really do, which is four things:
    // put the wall into the other shape, read it in the other order, wear the other counting, and open the whole
    // shelf the counting was taken out of.

    /// <summary>Which page of the rail the plates are standing for.</summary>
    private enum MenuPage
    {
      Commands,
      View,
      Sort,
      Counting
    }

    private MenuPage _menuPage;
    private string _menuTileLabel = "";
    private string _menuTileDetail = "";

    /// <summary>The wall's shape: a wrap of squares.</summary>
    public const string LAYOUT_TILES = "tiles";

    /// <summary>The wall's other shape: a strip of rows, as a shelf of titles lays its list.</summary>
    public const string LAYOUT_LIST = "list";

    /// <summary>The order the server counted in: the biggest value first.</summary>
    public const string SORT_COUNT = "count";

    /// <summary>The other order this wall can be read in: by the value itself.</summary>
    public const string SORT_VALUE = "value";

    /// <summary>
    /// The countings a shelf's shortcut tile can bring the viewer to, which are the countings the rail's filter
    /// page offers as the two walls this screen can wear.
    /// </summary>
    public static IReadOnlyList<string> Countings { get { return OWN_SCREEN_CATEGORIES; } }

    /// <inheritdoc />
    [ObservableProperty]
    private bool _mediaMenuVisible;

    /// <inheritdoc />
    [ObservableProperty]
    private string _mediaMenuHeading = "Menu";

    /// <summary>The shape the wall wears, which the rail's view page answers.</summary>
    [ObservableProperty]
    private string _layout = LAYOUT_TILES;

    /// <summary>
    /// Whether the column of words at the right hand end of the screen stands.  On from the first value, exactly as
    /// a shelf of titles and a wall of names open: what a value is worth belongs beside the wall rather than behind
    /// a press, and only the viewer's own answer - the "Details" row of the rail - takes it away.
    /// </summary>
    [ObservableProperty]
    private bool _showDetails = true;

    /// <summary>The value the column of words speaks of.</summary>
    [ObservableProperty]
    private string _detailsName = "";

    /// <summary>What the wall is read by: the server's count, or the value itself.</summary>
    [ObservableProperty]
    private string _sort = SORT_COUNT;

    /// <summary>Whether that reading runs from the top end down, which is what both orders open with.</summary>
    [ObservableProperty]
    private bool _sortDescending = true;

    /// <summary>The rows the rail wears for the page <see cref="_menuPage"/> says it is on.</summary>
    public ObservableCollection<MediaBrowserMenuEntry> MediaMenu { get; } =
        new ObservableCollection<MediaBrowserMenuEntry>();

    /// <summary>The answers the column of words gives under the value it names.</summary>
    public ObservableCollection<MediaBrowserFact> DetailsFacts { get; } = new ObservableCollection<MediaBrowserFact>();

    /// <summary>Whether the column is up at all, which is the viewer's answer and nothing else.</summary>
    public bool ShowsSideDetails { get { return ShowDetails; } }

    /// <summary>Whether the column has a value to speak of, which is when its words may be painted.</summary>
    public bool ShowsDetailsWords { get { return ShowDetails && DetailsName.Length > 0; } }

    /// <summary>Puts the menu up.</summary>
    [RelayCommand]
    public void ShowMediaMenu()
    {
      _menuPage = MenuPage.Commands;
      BuildMediaMenu();
      MediaMenuVisible = true;
    }

    /// <summary>Takes the menu down again.</summary>
    [RelayCommand]
    public void HideMediaMenu()
    {
      MediaMenuVisible = false;
    }

    /// <summary>Puts another page of the menu up without reopening it, which is what a command's arrow does.</summary>
    private void GoToPage(MenuPage page)
    {
      _menuPage = page;
      BuildMediaMenu();
    }

    private void AddMenuRow(string label, string icon, System.Windows.Input.ICommand command, bool isCurrent = false,
        string detail = "")
    {
      MediaMenu.Add(new MediaBrowserMenuEntry(label, icon, command, isCurrent, detail));
    }

    /// <summary>The rail's rows, for the page the viewer stands on.</summary>
    private void BuildMediaMenu()
    {
      MediaMenu.Clear();
      switch (_menuPage)
      {
        case MenuPage.View:
          MediaMenuHeading = "View";
          // The rows first, as on every other screen's view page: the squares are what the wall wears by default,
          // so the row that changes it is the one that has to be found and read first.
          AddMenuRow("List", "view_list", new RelayCommand(() => PickLayout(LAYOUT_LIST)),
              string.Equals(Layout, LAYOUT_LIST, StringComparison.OrdinalIgnoreCase));
          AddMenuRow("Grid", "grid_view", new RelayCommand(() => PickLayout(LAYOUT_TILES)),
              string.Equals(Layout, LAYOUT_TILES, StringComparison.OrdinalIgnoreCase));
          AddMenuRow(DetailsLabel, "details", new RelayCommand(ToggleDetails), ShowDetails);
          return;

        case MenuPage.Sort:
          MediaMenuHeading = "Sort";
          foreach ((string word, string key) choice in SortChoices())
          {
            string picked = choice.key;
            bool stands = string.Equals(Sort, picked, StringComparison.OrdinalIgnoreCase);
            // The standing row names the end the wall is read from; pressing it again is what turns that around.
            AddMenuRow(choice.word, "sort", new RelayCommand(() => PickSort(picked)), stands,
                DirectionWord(stands && SortDescending));
          }
          return;

        case MenuPage.Counting:
          MediaMenuHeading = "Filter";
          // The other counting is a wall of the same shelf, so the row opens that wall rather than quietly
          // re-counting behind this screen's heading.  Back from there leads back here.
          foreach (string counting in Countings)
          {
            string asked = counting;
            AddMenuRow(Capitalised(WordOf(asked)), "filter", new RelayCommand(() => AskForCounting(asked)),
                string.Equals(asked, Category, StringComparison.OrdinalIgnoreCase));
          }
          return;

        default:
          MediaMenuHeading = "Menu";
          // The whole shelf, which is what "no counting" means here: this screen only ever shows the values a
          // shelf carries, so leaving the counting behind is not a change of the wall but a step out of it.
          AddMenuRow("All " + Shelf.Label, "library_books", new RelayCommand(delegate
          {
            HideMediaMenu();
            ValueChosen?.Invoke(this, Shelf);
          }));
          AddMenuRow("Filter", "filter", new RelayCommand(() => GoToPage(MenuPage.Counting)), false,
              Capitalised(Word));
          AddMenuRow(SortLabel, "sort", new RelayCommand(() => GoToPage(MenuPage.Sort)), false,
              DirectionWord(SortDescending));
          AddMenuRow("View", "grid_view", new RelayCommand(() => GoToPage(MenuPage.View)));
          return;
      }
    }

    /// <summary>
    /// The two orders this wall can be read by.  A tile holds a value and a number and nothing else, so there is
    /// nothing here for a third order to be read by.
    /// </summary>
    private (string word, string key)[] SortChoices()
    {
      return new (string, string)[]
      {
          ("By count", SORT_COUNT),
          ("By " + Word, SORT_VALUE)
      };
    }

    /// <summary>The word the rail spends on the order, which names what the wall is read by.</summary>
    private string SortLabel
    {
      get { return string.Equals(Sort, SORT_VALUE, StringComparison.OrdinalIgnoreCase) ? "By " + Word : "By count"; }
    }

    /// <summary>The end the reading starts at, in the fewest words that say it.</summary>
    private string DirectionWord(bool descending)
    {
      if (!string.Equals(Sort, SORT_VALUE, StringComparison.OrdinalIgnoreCase))
        return descending ? "most first" : "fewest first";

      // Years read newest first and genres read A to Z, because those are the two ends a viewer already knows.
      return descending
          ? (string.Equals(Category, "year", StringComparison.OrdinalIgnoreCase) ? "newest first" : "Z to A")
          : (string.Equals(Category, "year", StringComparison.OrdinalIgnoreCase) ? "oldest first" : "A to Z");
    }

    private void PickLayout(string layout)
    {
      Layout = layout;
      BuildMediaMenu();
    }

    private void PickSort(string sort)
    {
      if (string.Equals(Sort, sort, StringComparison.OrdinalIgnoreCase))
        SortDescending = !SortDescending;
      else
      {
        // A new order starts at its top end, and both orders of this wall read downwards from their top: the
        // biggest count first, and the newest year first - which is also the end the server's own counting arrives
        // at, so the wall does not change its face when its default order is picked again.
        Sort = sort;
        SortDescending = true;
      }

      BuildMediaMenu();
    }

    /// <summary>
    /// Asks the application for the wall of the other counting.  Raised rather than done, because a wall is a
    /// stop of its own in the workflow and only the application builds stops.
    /// </summary>
    public event EventHandler<string>? CountingChosen;

    private void AskForCounting(string by)
    {
      if (string.Equals(by, Category, StringComparison.OrdinalIgnoreCase))
        return;

      HideMediaMenu();
      CountingChosen?.Invoke(this, by);
    }

    /// <summary>The row of the rail that answers the details column, named as the shelf names it.</summary>
    private string DetailsLabel { get { return ShowDetails ? "Details on" : "Details off"; } }

    /// <summary>Turns the column of words at the wall's right hand end on or off.</summary>
    [RelayCommand]
    private void ToggleDetails()
    {
      ShowDetails = !ShowDetails;
      BuildMediaMenu();
    }

    /// <inheritdoc />
    public void MenuTileStood(string? label, string? detail)
    {
      string came = label ?? string.Empty;
      string added = detail ?? string.Empty;
      if (came == _menuTileLabel && added == _menuTileDetail)
        return;

      _menuTileLabel = came;
      _menuTileDetail = added;
      OnPropertyChanged(nameof(MediaMenuFootLine));
    }

    /// <summary>A page of the rail being replaced took every plate the foot line spoke of with it.</summary>
    partial void OnMediaMenuHeadingChanged(string value)
    {
      _menuTileLabel = "";
      _menuTileDetail = "";
      OnPropertyChanged(nameof(MediaMenuFootLine));
    }

    /// <summary>The line at the screen's foot while the menu is out: the stood plate and what it adds to that.</summary>
    public string MediaMenuFootLine
    {
      get
      {
        if (_menuTileLabel.Length == 0)
          return MediaMenuHeading;
        return _menuTileDetail.Length == 0 ? _menuTileLabel : _menuTileLabel + " · " + _menuTileDetail;
      }
    }

    /// <summary>
    /// The one line this screen says at its foot about the value the caret stands on: the value and the number of
    /// titles underneath it.  It stands where the shelf of titles and the walls of names stand theirs, and only
    /// while the column of words is turned away - the viewer turned the column off, not the knowledge.
    /// </summary>
    public string ContentInfo
    {
      get
      {
        BrowseValue? value = Selected;
        if (value != null)
          return value.Text;

        // A counting that was not answered has nothing to say about a value it never got.
        return Status;
      }
    }

    /// <summary>Whether that line stands.</summary>
    public bool ShowsContentInfo
    {
      get { return (Selected != null || Status.Length > 0) && !ShowDetails && !MediaMenuVisible; }
    }

    partial void OnShowDetailsChanged(bool value)
    {
      OnPropertyChanged(nameof(ShowsSideDetails));
      OnPropertyChanged(nameof(ShowsDetailsWords));
      OnPropertyChanged(nameof(ShowsContentInfo));
    }

    partial void OnMediaMenuVisibleChanged(bool value) => OnPropertyChanged(nameof(ShowsContentInfo));

    partial void OnStatusChanged(string value)
    {
      OnPropertyChanged(nameof(ContentInfo));
      OnPropertyChanged(nameof(ShowsContentInfo));
    }

    partial void OnSelectedChanged(BrowseValue? value)
    {
      DetailsName = value?.Value ?? "";

      DetailsFacts.Clear();
      if (value != null)
      {
        // The answer and the word under it, in the shelf's own pairs: how many titles carry this value, and which
        // shelf they were counted out of - the two things a viewer standing on a tile cannot read off it.
        DetailsFacts.Add(new MediaBrowserFact("Titles", value.Count.ToString("N0")));
        DetailsFacts.Add(new MediaBrowserFact("Shelf", Shelf.Label));
      }

      OnPropertyChanged(nameof(ShowsDetailsWords));
      OnPropertyChanged(nameof(ContentInfo));
      OnPropertyChanged(nameof(ShowsContentInfo));
    }

    partial void OnSortChanged(string value) => ApplyOrder();

    partial void OnSortDescendingChanged(bool value) => ApplyOrder();

    partial void OnDetailsNameChanged(string value) => OnPropertyChanged(nameof(ShowsDetailsWords));

    /// <summary>
    /// Puts the values in the order the viewer asked for.  The counting itself is never redone - one answer from
    /// the server holds every value of a shelf, so re-ordering it is a question the screen answers for itself.
    /// </summary>
    private void ApplyOrder()
    {
      if (Values.Count == 0)
        return;

      List<BrowseValue> ordered = new List<BrowseValue>(Values);
      bool byValue = string.Equals(Sort, SORT_VALUE, StringComparison.OrdinalIgnoreCase);
      StringComparer by_word = StringComparer.CurrentCultureIgnoreCase;

      if (byValue)
        ordered = SortDescending ? ordered.OrderByDescending(v => v.Value, by_word).ToList()
                                 : ordered.OrderBy(v => v.Value, by_word).ToList();
      else
        ordered = SortDescending ? ordered.OrderByDescending(v => v.Count).ToList()
                                 : ordered.OrderBy(v => v.Count).ToList();

      string? stood = Selected?.Value;
      Values.Clear();
      foreach (BrowseValue value in ordered)
        Values.Add(value);

      // The caret keeps hold of the value it stood on rather than of a position in the wall, which is the shelf's
      // law for its own re-orderings: a viewer who changed the order did not ask to be moved.
      BrowseValue? same = stood == null
          ? null
          : Values.FirstOrDefault(v => string.Equals(v.Value, stood, StringComparison.Ordinal));
      Selected = same ?? (Values.Count > 0 ? Values[0] : null);
    }

    /// <summary>The word a screen says for a counting, so that the rail and the heading say it alike.</summary>
    private static string WordOf(string by)
    {
      if (string.Equals(by, "genre", StringComparison.OrdinalIgnoreCase))
        return "genre";
      if (string.Equals(by, "year", StringComparison.OrdinalIgnoreCase))
        return "year";
      return by;
    }

    private static string Capitalised(string word)
    {
      return word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
    }
  }
}
