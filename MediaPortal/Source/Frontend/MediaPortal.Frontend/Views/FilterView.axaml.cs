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

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaPortal.Frontend.Api;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Workflow;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MediaPortal.Frontend.Views
{
  /// <summary>
  /// A wall of one counting of one shelf - every year, or every genre, its titles carry - worn like a media screen
  /// wears its shelf, and like the wall of people wears it.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The screen brings nothing of its own to the look of a media screen and means that as a promise, exactly as the
  /// wall of names does: the rail is <see cref="ShelfRail"/>, the styles and the menu plates are
  /// <c>ShelfChrome.axaml</c> and <c>ShelfChromeStyles.axaml</c>, the ground, the two bands, the empty head, the
  /// column of words and the line at the foot are the shelf's own classes, and a viewer who learned on a shelf of
  /// films where "View" stands finds it standing there on a wall of years.
  /// </para>
  /// <para>
  /// What the screen does add is the walk: the arrow keys hand the caret from the wall to the rail and back, Enter
  /// on a value asks the application for the shelf cut to it, and the room slides the two hundred points that carry
  /// the rail into view - the same move the shelf and the people wall make, for the same reason that a rail which
  /// covered the wall it belongs to would be a rail hiding the thing it is about.
  /// </para>
  /// </remarks>
  public partial class FilterView : UserControl, IWorkflowScreen, IShelfRailHost
  {
    /// <summary>How far the room stands off the rail, and how far the rail stands off the screen.</summary>
    private const double MENU_ROOM = 200;

    /// <summary>How long the room takes to travel, which is the shelf's own three hundred and fifty milliseconds.</summary>
    private static readonly TimeSpan SLIDE_TIME = TimeSpan.FromMilliseconds(350);

    /// <summary>The panel a wall of tiles is laid in: the same one the shelf's grid and covers lay their tiles in.</summary>
    private static readonly FuncTemplate<Panel?> TILE_PANEL =
        new FuncTemplate<Panel?>(() => new VirtualizingTilePanel());

    /// <summary>How far the wall travels ahead of and behind the stood value, and how long the travel takes.</summary>
    private const double TILE_BAND = 400.0;
    private const double ROW_BAND = 130.0;
    private static readonly TimeSpan TILE_GLIDE = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ROW_GLIDE = TimeSpan.FromMilliseconds(300);

    /// <summary>How many frames the wall asks a control for its caret before it gives up on it.</summary>
    private const int CARET_TRIES = 12;

    private readonly FilterViewModel? _wall;
    private readonly WorkflowManager? _workflow;
    private readonly Func<HomeSection, WorkflowFrame?>? _open;
    private readonly Func<FilterViewModel, string, WorkflowFrame?>? _counting;

    private readonly ShelfNod _nods = new ShelfNod();

    private ShelfRail? _rail;
    private ShellWindow? _chromeShell;
    private DispatcherTimer? _hideRail;
    private DispatcherTimer? _caretTimer;

    /// <summary>What the column of words was last told to be, so that a resize that changes nothing changes nothing.</summary>
    private double? _detailsColumnWidth;

    private bool _railWired;
    private bool _readOnce;
    private bool _menuUp;

    /// <summary>Which row of the commands the viewer climbed down from, or -1 while they never did.</summary>
    private int _menuSteppedFrom = -1;

    /// <summary>The list's own scroller, once it has been found: what the fade is laid over and what the glide
    /// is worn by.  The markup never names it, because a list builds it out of its template.</summary>
    private ScrollViewer? _wallScroller;
    private BandEndFade? _wallFade;

    /// <summary>The length of travel the scroller's transition is currently worn with, so that a shape change can
    /// be noticed and the other length written in its place.</summary>
    private TimeSpan? _wornGlide;

    /// <summary>For the XAML loader and the designer: the counting a front end with no server shows.</summary>
    public FilterView()
        : this(new FilterViewModel(null, new HomeSection("Movies", new AspectViewModel(new MediaAspect(
            Guid.Empty, "MovieItem", false, false, null, Array.Empty<AspectAttribute>()))), "year"))
    {
    }

    /// <param name="wall">One counting of one shelf: its years or its genres.</param>
    /// <param name="workflow">The workflow this screen stands in, for the frame a pressed value opens.</param>
    /// <param name="open">
    /// Builds the stop that shows a shelf of titles, which only the application can do - it is the application that
    /// knows how a shelf is opened, whether cut to one value or whole, what it is headed, and what its player does
    /// with a title.  Null on a screen with nowhere to go.
    /// </param>
    /// <param name="counting">
    /// Builds the stop that shows the OTHER counting of the same shelf, which the rail's filter page asks for.  Null
    /// on a screen that offers no second wall.
    /// </param>
    public FilterView(FilterViewModel? wall, WorkflowManager? workflow = null,
        Func<HomeSection, WorkflowFrame?>? open = null, Func<FilterViewModel, string, WorkflowFrame?>? counting = null)
    {
      DataContext = wall;
      _wall = wall;
      _workflow = workflow;
      _open = open;
      _counting = counting;
      InitializeComponent();

      WearTheShape();
      DpadNavigation.Attach(this, new List<Control> { valuesList });

      // The rail is the shelf's rail, wearing the shelf's ground: it asks this screen whose menu it is, where the
      // caret is asked for and how its ground nods, and knows everything else for itself.
      _rail = new ShelfRail(mediaMenuList, sideMenu, false, this);

      // THE HEAD LINE OF THE WINDOW, asked for exactly as a shelf of titles and a wall of names ask for it: the
      // reference hung its picture over the head band as well as the client area, so a header kept in a row of its
      // own above the screen would lay a black bar over the head of the wall.
      AttachedToVisualTree += delegate
      {
        _chromeShell = this.GetVisualAncestors().OfType<ShellWindow>().FirstOrDefault();
        _chromeShell?.SetChromeHeaderOverlay(true);
        _chromeShell?.SetChromeHeaderStowed(true);
        ApplyColumns();
      };
      DetachedFromVisualTree += delegate
      {
        _chromeShell?.SetChromeHeaderOverlay(false);
        _chromeShell?.SetChromeHeaderStowed(false);
        _chromeShell = null;
      };

      // The wall and its column of words divide the room between them, and the division is a number of points
      // rather than a proportion, so it is made again whenever the room changes size or the words go away.
      SizeChanged += delegate { ApplyColumns(); };

      // A value pointed at is the same question its Enter asks, which is the law every tile that is a door in this
      // front end answers (PeopleView.Hooks its cards the same way).  Hooked as a routed tap on the wall rather than
      // on a tile, because the tile is whatever the template grew.
      valuesList.AddHandler(InputElement.TappedEvent, delegate (object? sender, TappedEventArgs args)
      {
        for (Visual? stood = args.Source as Visual; stood != null && stood != valuesList;
             stood = stood.GetVisualParent())
        {
          if (stood is Control tile && tile.DataContext is BrowseValue pressed)
          {
            valuesList.SelectedItem = pressed;
            _wall?.OpenCommand.Execute(null);
            args.Handled = true;
            return;
          }
        }
      }, RoutingStrategies.Tunnel);

      if (_wall != null)
      {
        _wall.PropertyChanged += OnWallChanged;

        // A pressed value is a shelf, and a shelf is a stop, and only the application builds stops.
        _wall.ValueChosen += delegate (object? sender, HomeSection shelf)
        {
          // The cut shelf is pushed over this screen, so Back from the titles leads back onto the value that was
          // pressed rather than out of the library.
          WorkflowFrame? frame = _open?.Invoke(shelf);
          if (frame != null)
            _workflow?.Push(frame);
        };

        // The other counting of the same shelf is a second wall, and the way back from it leads to this one.
        _wall.CountingChosen += delegate (object? sender, string by)
        {
          WorkflowFrame? frame = _counting?.Invoke(_wall, by);
          if (frame != null)
            _workflow?.Push(frame);
        };
      }

      AddHandler(KeyDownEvent, delegate (object? sender, KeyEventArgs args)
      {
        if (args.Handled || _wall == null)
          return;

        if (args.Key == Key.Enter || args.Key == Key.Return)
        {
          if (valuesList.IsKeyboardFocusWithin && valuesList.SelectedItem is BrowseValue)
          {
            // A value is not a title: pressing one is the question "which of these titles?", which is a shelf cut
            // to that value and therefore a shelf like any other.
            args.Handled = true;
            _wall.OpenCommand.Execute(null);
          }
          return;
        }

        if (args.Key == Key.Escape && _menuUp)
        {
          // Back from a page of the rail is the page above it, and back from the commands is the rail going away -
          // which is what the rail's own back does when the viewer climbed down, so only the top page is asked
          // about here.
          args.Handled = true;
          ShowRail(false);
          return;
        }

        if (MediaPortal.Frontend.Settings.KeyBindings.Current.Is(
                MediaPortal.Frontend.Settings.KeyAction.Details, args))
        {
          // The same letter the shelf of titles answers, asking the same question the rail's view page asks: whether
          // the words beside the wall stand.  The wall itself is untouched by the answer.
          args.Handled = true;
          _wall.ToggleDetailsCommand.Execute(null);
          return;
        }

        if (args.Key == Key.Left && !_menuUp && !mediaMenuList.IsKeyboardFocusWithin
            && StandingOnTheLeadingEdge())
        {
          // Only the wall's leading edge points at the rail, and the leading edge is the WHOLE first column - the
          // shelf's law: a caret standing in the middle of a row of tiles is pointing at the tile beside it, and a
          // rail that jumped out on every Left would be a rail nobody can walk past.
          args.Handled = true;
          ShowRail(true);
          return;
        }

        if (args.Key == Key.Right && _menuUp && mediaMenuList.IsKeyboardFocusWithin)
        {
          args.Handled = true;
          ShowRail(false);
        }
      }, RoutingStrategies.Tunnel);

      // THE WALL NODS AT ITS OWN EDGES, by the shelf's rule: an arrow that points past one of a wall's edges leans
      // the wall that way twenty-two points and springs it back, so a viewer can tell an edge from a broken key.
      // Left is not one of these edges - at the leading edge it opens the rail, which the handler above answers
      // first, and anywhere else it means the tile beside the caret.  While the rail stands open the wall says
      // nothing at all, because the caret is two hundred points away to the left and the rail answers its own edges.
      Control wall_ground = (Control?)valuesList.Parent ?? valuesList;
      wall_ground.AddHandler(KeyDownEvent, delegate (object? sender, KeyEventArgs args)
      {
        if (args.Handled || _menuUp || _wall == null || valuesList.Items.Count == 0)
          return;

        if (!valuesList.IsKeyboardFocusWithin)
          return;

        int stood = valuesList.SelectedIndex;
        if (stood < 0)
          return;

        int columns = Math.Max(1, StandsAsRows() ? 1 : WallColumns());
        int values = valuesList.Items.Count;

        double diveX = 0;
        double diveY = 0;
        bool at_a_wall;

        switch (args.Key)
        {
          case Key.Right:
            at_a_wall = columns == 1 || stood % columns == columns - 1 || stood == values - 1;
            diveX = ShelfRail.RAIL_BOUNCE_DIVE;
            break;
          case Key.Up:
            at_a_wall = stood < columns;
            diveY = -ShelfRail.RAIL_BOUNCE_DIVE;
            break;
          case Key.Down:
            at_a_wall = stood + columns >= values;
            diveY = ShelfRail.RAIL_BOUNCE_DIVE;
            break;
          default:
            return;
        }

        if (!at_a_wall)
          return;

        _nods.Nod(valuesList, diveX, diveY);
        args.Handled = true;
      }, RoutingStrategies.Tunnel);

      // The Menu key and a right press are the two roads to a screen's own menu on every screen of this front end.
      AddHandler(ContextRequestedEvent, delegate (object? sender, RoutedEventArgs args)
      {
        args.Handled = true;
        ShowRail(!_menuUp);
      }, RoutingStrategies.Tunnel);

      AddHandler(PointerPressedEvent, delegate (object? sender, PointerPressedEventArgs args)
      {
        if (args.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
          args.Handled = true;
          ShowRail(!_menuUp);
        }
      }, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    public void OnScreenActivated()
    {
      if (_wall != null && !_readOnce)
      {
        _readOnce = true;
        _ = ReadWallAsync();
      }

      PutTheCaretOnTheWall();
    }

    /// <summary>The wall, never the rail: a rail shut behind the edge of the screen is not where keys start.</summary>
    public Control? KeysStartAt()
    {
      return valuesList;
    }

    /// <summary>Runs when the frame leaves the workflow: the wall is put down with it.</summary>
    public void OnScreenClosed()
    {
      if (_wall != null)
        _wall.PropertyChanged -= OnWallChanged;

      _hideRail?.Stop();
    }

    private async Task ReadWallAsync()
    {
      if (_wall == null)
        return;

      await _wall.ReadCommand.ExecuteAsync(null);

      // The values arrived, so the plates and the shapes have to be laid under them; the caret is asked for again
      // because the row it stood on was not there when it was first asked for.
      Dispatcher.UIThread.Post(delegate
      {
        PutTheCaretOnTheWall();
        WearTheWallChrome();
        ApplyColumns();
      }, DispatcherPriority.Loaded);
    }

    private void OnWallChanged(object? sender, PropertyChangedEventArgs args)
    {
      switch (args.PropertyName)
      {
        case nameof(FilterViewModel.MediaMenuVisible):
          ShowRail(_wall?.MediaMenuVisible == true);
          break;

        case nameof(FilterViewModel.MediaMenuHeading):
          if (_menuUp && _wall != null)
          {
            // A page of the open menu replaced its rows - the viewer climbed down over "View" or went back up with
            // the back key - and every row the rail wore, caret included, went with the Clear that preceded them.
            // Down a page the caret belongs on the first row; up to the commands it goes to the row the viewer
            // climbed down from, because the way back is only half an answer if nobody stands at its end.
            bool up_to_the_commands = _wall.MediaMenuHeading == ShelfRail.COMMANDS_HEADING;
            int wanted = up_to_the_commands && _menuSteppedFrom >= 0 ? _menuSteppedFrom : 0;
            if (up_to_the_commands)
              _menuSteppedFrom = -1;

            // Asked after this pass, because the heading is set between the clearing and the adding.
            Dispatcher.UIThread.Post(delegate { _rail?.StandOnRow(wanted); });
          }
          break;

        case nameof(FilterViewModel.Layout):
          WearTheShape();
          break;

        case nameof(FilterViewModel.ShowsPictures):
          // The counting arrived with pictures under its values, or with none, and the squares have to be the ones
          // that suit them.  The layout travels with the template, so the whole shape is put on again rather than
          // the template alone.
          WearTheShape();
          break;

        case nameof(FilterViewModel.ShowsSideDetails):
        case nameof(FilterViewModel.ShowsDetailsWords):
          // The words came or went, and with them the points of room they stand in.
          ApplyColumns();
          break;

        case nameof(FilterViewModel.Selected):
          if (_wall?.Selected is BrowseValue stood)
          {
            valuesList.ScrollIntoView(stood);
            if (NeedsCaretOnTheWall())
              StandTheCaret();
          }
          break;
      }
    }

    /// <summary>
    /// Whether the caret has to be asked for: nothing has it, or it stands on the wall's own list without having
    /// reached a value of it.  Anything else it stands on inside this screen - a tile, the rail - is where it stays.
    /// The shelf's <c>NeedsCaretOnTheShelf</c> and the people wall's, with the search field's claim left out because
    /// this screen has no field to spell into.
    /// </summary>
    private bool NeedsCaretOnTheWall()
    {
      Visual? focused = (TopLevel.GetTopLevel(this) as Window)?.FocusManager.GetFocusedElement() as Visual;
      if (focused == null)
        return true;

      bool inside = false;
      for (Visual? at = focused; at != null; at = at.GetVisualParent())
      {
        if (ReferenceEquals(at, this))
          inside = true;

        if (ReferenceEquals(at, valuesList))
        {
          // The list is the caret's waiting room, not its place: a caret that got no further than the list never
          // lit a tile, which is what the caret on a wall is for.
          if (ReferenceEquals(focused, valuesList))
            return true;

          int stand = valuesList.SelectedIndex < 0 ? 0 : valuesList.SelectedIndex;
          return !ReferenceEquals(focused, valuesList.ContainerFromIndex(stand));
        }
      }

      return !inside;
    }

    /// <summary>
    /// Puts the caret on the wall - on the value the wall says the caret stood on, or on the head of the wall when
    /// nobody is stood on it yet - and keeps asking for the wall until it can hold the caret.
    /// </summary>
    /// <remarks>
    /// Both laws are the shelf's, and the second is why this is a loop rather than a call: a list that has not been
    /// measured cannot hold a caret and answers <c>Focus()</c> with a failure and nothing else, which is the state
    /// this wall is in every time it is asked - the values came off the server one frame ago and the wall is still
    /// sliding in with the room.
    /// </remarks>
    private void PutTheCaretOnTheWall()
    {
      StandCaret(valuesList, delegate
      {
        if (_wall != null && _wall.Selected == null
            && valuesList.Items.OfType<BrowseValue>().FirstOrDefault() is BrowseValue head)
        {
          // The head of the wall through the wall's own answer, so that the column of words and the line at the
          // foot have something to speak of the moment the screen opens.
          _wall.Selected = head;
        }

        StandTheCaret();
      });
    }

    private void StandCaret(Control target, Action? whenStood)
    {
      _caretTimer?.Stop();

      target.Focus();
      if (target.IsKeyboardFocusWithin)
      {
        whenStood?.Invoke();
        return;
      }

      DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render)
      {
        Interval = TimeSpan.FromMilliseconds(16)
      };
      _caretTimer = timer;
      int left = CARET_TRIES;

      timer.Tick += delegate
      {
        if (target.IsKeyboardFocusWithin)
        {
          timer.Stop();
          whenStood?.Invoke();
          return;
        }

        if (left-- <= 0)
        {
          timer.Stop();
          return;
        }

        target.Focus();
      };

      timer.Start();
    }

    /// <summary>
    /// Stands the caret on the container the wall recycled for the selected value, which is where a shelf stands its
    /// caret and not an only-looking-fine detail: every rule of this skin that lights a tile's ring or whitens a
    /// row's sheet is written against <c>ListBoxItem:focus</c>.
    /// </summary>
    private void StandTheCaret(int tries = 2)
    {
      Control? stood = StoodContainer();
      if (stood == null)
      {
        if (tries > 0)
        {
          Dispatcher.UIThread.Post(delegate { StandTheCaret(tries - 1); }, DispatcherPriority.Loaded);
        }
        return;
      }

      stood.Focus();
      GlideWallToStoodValue();
    }

    /// <summary>The container the wall recycled for the value being stood on, or nothing while it is not laid.</summary>
    private Control? StoodContainer()
    {
      int at = valuesList.SelectedIndex;
      return at < 0 ? null : valuesList.ContainerFromIndex(at) as Control;
    }

    /// <summary>
    /// Carries the wall so that the stood value sits inside the band its shape walks by, and lets the scroller's own
    /// transition carry it there.  The band, not the middle: a value already inside it is left where it stands,
    /// which is what keeps a wall still for the first steps of a walk.
    /// </summary>
    private void GlideWallToStoodValue()
    {
      ScrollViewer? scroller = _wallScroller;
      Control? stood = StoodContainer();
      if (scroller == null || stood == null)
        return;

      double view = scroller.Viewport.Height;
      if (view <= 0)
        return;

      Point at = stood.TranslatePoint(new Point(0, 0), scroller) ?? new Point(0, 0);
      double top = scroller.Offset.Y + at.Y;
      double high = stood.Bounds.Height;
      bool rows = StandsAsRows();
      double before = rows ? ROW_BAND : TILE_BAND;
      double after = rows ? ROW_BAND : TILE_BAND;

      double wanted = scroller.Offset.Y;
      if (view - before - after >= high)
      {
        if (top < before)
          wanted = top;
        else if (top + high > view - after)
          wanted = top + high - (view - after);
      }
      else
      {
        wanted = top + high / 2.0 - view / 2.0;
      }

      double floor = Math.Max(0.0, scroller.Extent.Height - view);
      wanted = Math.Max(0.0, Math.Min(floor, wanted));
      if (Math.Abs(wanted - scroller.Offset.Y) < 0.5)
        return;

      scroller.Offset = new Vector(scroller.Offset.X, wanted);
    }

    /// <summary>Whether the wall stands as rows of words rather than as a wrap of squares.</summary>
    private bool StandsAsRows()
    {
      return _wall != null
          && string.Equals(_wall.Layout, FilterViewModel.LAYOUT_LIST, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the caret stands on the wall's leading edge, which is the whole first column of values and not the
    /// first value alone - the shelf reads its edge that way, because a viewer who walked one tile to the right must
    /// find the same Left they found before.  A wall laid as rows is one column, so every row stands on the edge.
    /// </summary>
    private bool StandingOnTheLeadingEdge()
    {
      int standing = valuesList.SelectedIndex;
      if (standing < 0 || StandsAsRows())
        return true;

      return standing % WallColumns() == 0;
    }

    /// <summary>
    /// How many values the wall lays across, taken from the panel that lays them - the only one who knows, since it
    /// is the tile's own width against the wall's that decides.
    /// </summary>
    private int WallColumns()
      => valuesList.GetVisualDescendants().OfType<VirtualizingTilePanel>().FirstOrDefault()?.Columns ?? 1;

    /// <summary>
    /// Divides the room between the wall and its column of words by the measure a shelf of titles divides its room by
    /// (<see cref="ShelfDetailsColumn"/>), so that the same window width hands a wall of values and a shelf the same
    /// number of points to the words.  With the words away the wall has the room to itself.
    /// </summary>
    private void ApplyColumns()
    {
      if (_wall == null)
        return;

      double room = Bounds.Width > 0 ? Bounds.Width : 1920;
      double details = ShelfDetailsColumn.WidthFor(room, _wall.ShowsSideDetails);
      if (_detailsColumnWidth.HasValue && Math.Abs(_detailsColumnWidth.Value - details) < 0.5)
        return;

      _detailsColumnWidth = details;
      contentGrid.ColumnDefinitions = new ColumnDefinitions
      {
          new ColumnDefinition(GridLength.Star),
          new ColumnDefinition(new GridLength(details))
      };
    }

    /// <summary>
    /// Puts the wall into the shape its viewer picked: the square a value is drawn in, or the strip of rows a list
    /// is.  The panel travels with the template, because a wrap of squares and a strip of rows are two ways of laying
    /// things out rather than two ways of drawing one thing.
    /// </summary>
    private void WearTheShape()
    {
      bool asRows = _wall != null
          && string.Equals(_wall.Layout, FilterViewModel.LAYOUT_LIST, StringComparison.OrdinalIgnoreCase);

      // THE SKIN'S OWN NAMES for what the wall is, which is how a shelf of titles is dressed: these two class names
      // are what the skin answers with the row's white sheet, the tile's shadow and ring, and the resting and stood
      // states of a row (MediaBrowserView.ApplyLayout).
      valuesList.Classes.Set("nereus-shelf-rows", asRows);
      valuesList.Classes.Set("nereus-shelf-tiles", !asRows);

      // Which square a value is drawn in depends on what the values carry: a wall of genres the library numbered is
      // a wall of pictures with a word under each, worn as the people wall wears its portraits, while a wall of
      // years - and a genre wall on a skin without the art - is a wall of words.  The wall decides this once, when
      // its counting arrives, and every square of it then stands the same way.
      object? key = asRows
          ? "ValueWallRow"
          : _wall!.ShowsPictures ? "ValueWallPictureTile" : "ValueWallTile";
      if (this.TryFindResource(key, out object? template) && template is IDataTemplate worn)
        valuesList.ItemTemplate = worn;

      if (asRows)
        valuesList.ClearValue(ItemsControl.ItemsPanelProperty);
      else
        valuesList.ItemsPanel = TILE_PANEL;

      WearTheWallChrome();
    }

    /// <summary>
    /// The two things a wall of the skin is asked to wear and a list of values is not: the mask that dissolves its
    /// rows into the top and bottom edges, and the glide that carries the wall to the value the caret stands on.
    /// Both live on the list's own scroller, which the markup never names, so it is asked for once the list has been
    /// laid, which is the moment the shelf asks for its own.
    /// </summary>
    private void WearTheWallChrome()
    {
      Dispatcher.UIThread.Post(delegate
      {
        ScrollViewer? scroller = valuesList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller == null)
          return;

        if (!ReferenceEquals(scroller, _wallScroller))
        {
          _wallScroller = scroller;
          _wallFade = new BandEndFade(scroller, false);
        }

        // The length of a travel belongs to the SHAPE and not to the scroller: rows are carried three hundred points
        // of the reference's list and squares four hundred of its grid.
        TimeSpan worn = StandsAsRows() ? ROW_GLIDE : TILE_GLIDE;
        if (_wornGlide != worn)
        {
          _wornGlide = worn;
          scroller.Transitions = new Transitions
          {
              new VectorTransition
              {
                  Property = ScrollViewer.OffsetProperty,
                  Duration = worn
              }
          };
        }

        _wallFade?.Update();
      }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The transform the room travels on, read off the room itself: naming a transform in the markup hands the code
    /// a field for a thing that is not a control, and asking the control what it is wearing answers the same
    /// question without that detour.
    /// </summary>
    private TranslateTransform RoomSlide
    {
      get { return (TranslateTransform)room.RenderTransform; }
    }

    /// <summary>
    /// Slides the room out of the rail's way, or back over it.  The rail is put into the tree when the room begins to
    /// move away and out of it again when the room has arrived, because Avalonia measures and paints a control nobody
    /// can see not at all.
    /// </summary>
    private void ShowRail(bool open)
    {
      if (_wall == null || _menuUp == open)
        return;

      _menuUp = open;
      _hideRail?.Stop();

      // The model is told, because it is the model that says what the foot line under the head reads and what the
      // plates are standing for; the word comes back here as a property change, which this method answers by
      // noticing it already stands where it was asked to stand.
      if (open)
        _wall.ShowMediaMenu();
      else
        _wall.HideMediaMenu();

      if (open)
      {
        sideMenu.IsVisible = true;

        // The rail takes the caret with it.  Walking off the leading edge of the wall to reach the rail is the
        // viewer leaving the wall, and the rail is the thing they are pointing at.
        if (!_railWired)
        {
          _railWired = true;
          _rail?.Wire();
        }

        RoomSlide.X = MENU_ROOM;
        Dispatcher.UIThread.Post(delegate
        {
          _rail?.Dress();
          if (mediaMenuList.Focus())
            _rail?.StandOnRow(0);
        }, DispatcherPriority.Loaded);
        return;
      }

      RoomSlide.X = 0;
      _menuSteppedFrom = -1;
      if (_hideRail == null)
      {
        _hideRail = new DispatcherTimer { Interval = SLIDE_TIME };
        _hideRail.Tick += delegate
        {
          _hideRail!.Stop();
          if (!_menuUp)
            sideMenu.IsVisible = false;
        };
      }
      _hideRail.Start();
      PutTheCaretOnTheWall();
    }

    // What the rail asks of this screen; see IShelfRailHost.
    /// <inheritdoc />
    IMenuRailOwner? IShelfRailHost.RailOwner => _wall;

    /// <inheritdoc />
    void IShelfRailHost.RailCaret(Control target, Action? whenStood)
    {
      StandCaret(target, whenStood);
    }

    /// <inheritdoc />
    void IShelfRailHost.RailNod(Control ground, double diveX, double diveY)
    {
      _nods.Nod(ground, diveX, diveY);
    }

    /// <inheritdoc />
    void IShelfRailHost.RailClimbedDown(int row)
    {
      // Remembered so that the way back can stand the caret on the command the viewer climbed down from.  The rail
      // itself does the standing; this screen only keeps the number.
      _menuSteppedFrom = row;
    }
  }
}
