using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.Theming;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Workflow;

namespace MediaPortal.Frontend.Views
{
  /// <summary>
  /// The settings hub: the wall of places a household configures, the narrowed list of them once one is open,
  /// and the screen of the one it picked.
  /// </summary>
  /// <remarks>
  /// Both shapes of the hub are on the screen at once and only one of them is ever visible, so the d-pad ring
  /// names both - a stop the viewer cannot see is stepped over rather than stopped at, which is what
  /// <see cref="DpadNavigation"/> does with an invisible control. Back is a key of this screen as well as the
  /// shell's: out of a screen into the wall, out of a folder into the level above, and only then out of the
  /// settings - the order the reference's configuration states kept.
  /// </remarks>
  public partial class SettingsView : UserControl, IWorkflowScreen
  {
    private readonly SettingsViewModel _model;

    /// <summary>
    /// The nod the wall gives a key that points past its edge: the plates lean toward the wall and spring
    /// back, the same answer the home's shelves give (see <see cref="ShelfNod"/>). It hangs on the host the
    /// wall scrolls in, so the whole visible wall - every plate at once - nods as one ground.
    /// </summary>
    private readonly ShelfNod _wallNods = new ShelfNod();

    /// <summary>The scrollviewer the wall of plates stands in, found once it is drawn.</summary>


    private readonly List<Control> _ring = new List<Control>();

    /// <summary>For the XAML loader: an empty hub, which the designer shows as an empty list.</summary>
    public SettingsView()
        : this(new SettingsViewModel())
    {
    }

    public SettingsView(SettingsViewModel sections)
    {
      DataContext = sections;
      _model = sections;
      InitializeComponent();

      _ring.Add(wallList);
      DpadNavigation.Attach(this, _ring);

      // The wall is walked with the caret of the keyboard, not with the list's selection: a picked plate IS
      // an opened section here, so a list that answered the arrow keys with its own selection would open a
      // section on every key - the hub deciding twice per breath what the viewer came for. This handler
      // therefore takes the arrow keys off the list while a plate stands under the caret, moves the caret
      // from plate to plate, and answers Enter as the press of the plate stood on. A direction nobody stands
      // under - no plate has the caret, or the wall ends in that direction - is left for the ring above.
      wallList.AddHandler(KeyDownEvent, OnWallKeysTunnel, RoutingStrategies.Tunnel);

      // Back belongs to the hub before it belongs to anything inside an open page: most pages never claim it
      // (a FanArt cache page, a metadata page, a plain list), and a key nobody claims bubbles out to the
      // shell - whose only back answer is to pop the whole Settings frame, landing the viewer on Home from
      // three levels deep. Heard on the BUBBLE road at handledEventsToo, this sees every unclaimed Back that
      // came from inside the hub - after any page that DOES claim one (the input devices editor steps its
      // table back to its chooser first) has left the field - and takes exactly one step down the hub's own
      // trail. A Back the hub cannot spend (its first level) stays unhandled for the shell, which leaves.
      AddHandler(KeyDownEvent, delegate (object? sender, KeyEventArgs e)
      {
        if (e.Handled || !Settings.KeyBindings.Current.Is(Settings.KeyAction.Back, e))
          return;
        if (!StepBackInsideHub())
          return;
        e.Handled = true;
        Target().Focus();
        StandCaretOn(Target(), CARET_TRIES);
      }, RoutingStrategies.Bubble, handledEventsToo: true);

      // Both lists show the level and only one of them is ever looked at, and a press leaves a number behind in
      // whichever held it - the number of the plate last pressed, which the control walks back to on the next
      // press. A number belonging to a level that is no longer on screen is not a wrong selection but an
      // ArgumentException out of Avalonia's selection model, unhandled, and that is what closed the client. So
      // both lists are made to forget theirs the moment the hub hands over another level - and the caret walks
      // onto the first plate of the new level, so the wall a viewer lands on always has a plate stood on.
      sections.LevelChanged += delegate
      {
        ForgetPlaces();
        ApplyHeaderTitle();
        FitWall(); // another level is another number of plates, and the fit is the plates' business
        StandCaretOn(Target(), CARET_TRIES);
      };

      // The fit is asked for wherever the room or the wall changes: the host's size (the window grew, the
      // first layout came), the wall coming back from beside a page (the hub's IsWall word), and the level
      // changing above.
      contentHost.SizeChanged += delegate { FitWall(); };

      // The screen an open page stands on. A page arms itself when it joins the window - but the hub builds
      // it detached and swaps it in, so "joined" can land a beat after another pick already replaced the
      // page; the page asks its hub whether it is still the one open before it claims anything.
      OpenedScreen = sections.CurrentView as Control;
      _model.PropertyChanged += delegate (object? sender, System.ComponentModel.PropertyChangedEventArgs e)
      {
        if (e.PropertyName == nameof(SettingsViewModel.IsWall) && wallScale.IsVisible)
          FitWall();

        // The corner word names the room that is looked at, and an open page or a fold back into the wall
        // is another room.
        if (e.PropertyName == nameof(SettingsViewModel.ScreenName))
          ApplyHeaderTitle();

        // An open screen is now the whole room, so the keyboard belongs to it rather than staying on the plate
        // that was pressed. A page may name a control of its own through AskFocusFrom; until such a control can
        // be drawn - or when the page names none - the hub stands on whatever inside the screen will take the
        // keyboard, bounded like everywhere else here.
        if (e.PropertyName == nameof(SettingsViewModel.CurrentView))
        {
          OpenedScreen = sections.CurrentView as Control;
          StandOnOpenScreen(CARET_TRIES);
        }
      };

      // No SECTION opens itself on arrival: opening the first one instead would be the hub deciding what a
      // viewer came for. A PLATE standing under the caret is not an opening - it is where the first arrow key
      // of the visit walks from, and what the edge of the wall says "this one" about.

      // The same ask the home makes (HomeView): take the header out of its row and let it float over the
      // screen, so the skin's ground reaches the top edge of the television and the clock stands over the
      // ground and its darkening rather than over a band of black. The bands this screen paints are the
      // home's own - see the overlays in the markup - and a screen that planned its space under a header
      // row would find its top cut off by the floating strip, so the ask is made on the way in and handed
      // back on the way out.
      AttachedToVisualTree += delegate
      {
        _chromeShell = this.GetVisualAncestors().OfType<ShellWindow>().FirstOrDefault();
        // The room wears no head line at all: neither the clock cluster nor the notification marks of the
        // header stand here - the viewer asked for the recording, connection and notice pictures to go, and
        // with them the title they carried. What the screen shows is said instead by one quiet word at the
        // foot of the window (SetScreenCaptionShown), which follows the level walked into.
        _chromeShell?.SetChromeHeaderStowed(true);
        _chromeShell?.SetScreenCaptionShown(true);
        ApplyHeaderTitle();
        // Joining the tree IS arriving here for the hub, but only joining the WINDOW makes drawing possible:
        // on the machine the view joins the tree while it is still detached from the window - the log said
        // "list bounds 0x0, listAttached=false" - so every caret ask at this moment lands nowhere. The first
        // real layout pass is waited for instead, and the caret is stood on it, which is the one moment a
        // plate can exist and take the keyboard.
        WaitFirstLayoutThenStandTheCaret();
      };
      DetachedFromVisualTree += delegate
      {
        _chromeShell?.SetChromeHeaderStowed(false);
        _chromeShell?.SetScreenCaptionShown(false);
        _chromeShell = null;
      };
    }

    /// <summary>
    /// Stands the caret once the wall can actually carry it: a plate only exists once the view stands inside
    /// a drawn window, and on the machine that is a pass later than the moment the hub joins the tree (the
    /// workflow builds screens detached and shows them afterwards). A bounded timer asks every layout beat
    /// whether the list has grown its first container; when it has, the caret is stood as before. The number
    /// of beats is generous because a television box lays out slower than the bench, and the ask gives up
    /// rather than tick forever if the screen is walked away from.
    /// </summary>
    private void WaitFirstLayoutThenStandTheCaret()
    {
      int beats_left = 120; // two seconds' worth of layout beats
      DispatcherTimer waiting = new DispatcherTimer(DispatcherPriority.Loaded)
      {
        Interval = TimeSpan.FromMilliseconds(16)
      };
      waiting.Tick += delegate
      {
        if (!IsLoaded || !IsVisible)
        {
          if (--beats_left <= 0)
            waiting.Stop(); // gone again - nothing to stand on here
          return;
        }

        bool drawn = wallList.IsLoaded && wallList.Items.Count > 0
            && wallList.ContainerFromIndex(0) != null;
        if (drawn || --beats_left <= 0)
        {
          waiting.Stop();
          if (_model.ShowingContents)
            StandOnOpenScreen(CARET_TRIES);
          else
            StandCaretOn(Target(), CARET_TRIES);
        }
      };
      waiting.Start();
    }

    private ShellWindow? _chromeShell;

    /// <summary>
    /// The screen the hub is showing right now, as the view sees it. A page that arms itself on joining the
    /// window asks this whether it is still the one open - the hub builds pages detached and swaps them in,
    /// so a page can join the tree a beat after the viewer already picked something else, and an old page
    /// claiming the keyboard or the next key press would steal it from the screen actually on show.
    /// </summary>
    public Control? OpenedScreen { get; private set; }

    /// <summary>
    /// The scale the wall stands at right now; 0 while none has ever been written. Half a thousandth is
    /// nothing a television can show, and a new transform object is a new measure for the whole wall, so
    /// the number standing is remembered - the same law the home's wall keeps for its transform.
    /// </summary>
    private double _wallFitScale;

    /// <summary>The column count the wall was last laid out in; -1 while it was never laid out.</summary>
    private int _wallFitCols = -1;

    /// <summary>While one <see cref="FitWall"/> measures, no re-arrange may ask it again.</summary>
    private bool _wallFitting;

    /// <summary>
    /// How small the wall may shrink before it stops fitting and lets the scroll take over - the home wall's
    /// own floor. A level with more plates than even that can carry reads its names at a quarter of a
    /// hundredth of the television, and a plate nobody can read has stopped being a plate.
    /// </summary>
    private const double WALL_FLOOR = 0.35;

    /// <summary>
    /// Scaling the wall so every plate of the level stands on the screen - aspect locked, never grown past
    /// the size the skin drew. The host between the gradient bands is the room. The arithmetic is done from
    /// the plate's own step (the square the skin drew plus the gap it put around it) and the level's plate
    /// count: try every column count, and the one whose scale is the largest is the wall - width written so
    /// the wrap lays exactly that many per row, one transform over the whole of it. No measuring of the
    /// control on the way: a wrap panel and a scale asked in turns can hand each other different row counts
    /// and never settle, and a stale measure answer leaves the wall arranged to a size the screen does not
    /// have - which is the scrolling wall this replaces.
    /// </summary>
    /// <remarks>
    /// One transform for the whole wall - plates, bands, names and focus rings keep the proportion the face
    /// drew them in - which is the home shelf's technique (HomeView.ScaleNereusWall) rather than a new one.
    /// It is worked out for the tile views only: the narrowed list and the page beside it keep the full
    /// room, which is what the viewer asked for and what a wall of text needs anyway.
    /// </remarks>
    private void FitWall()
    {
      if (_wallFitting)
        return;

      _wallFitting = true;
      try
      {
        if (!wallScale.IsVisible)
          return;

        Avalonia.Thickness inset = wallScale.Margin;
        double availW = contentHost.Bounds.Width - inset.Left - inset.Right;
        double availH = contentHost.Bounds.Height - inset.Top - inset.Bottom;
        int plates = wallList.ItemCount;
        if (availW < 100 || availH < 100)
          return; // nothing to fit into yet; the host's SizeChanged asks again

        if (plates < 1)
        {
          wallList.ClearValue(WidthProperty);
          _wallFitScale = 0;
          _wallFitCols = 0;
          wallScale.LayoutTransform = null;
          return;
        }

        double step = WallPlateStep();
        double best = -1;
        int bestCols = plates;
        for (int cols = 1; cols <= plates; cols++)
        {
          int rows = (plates + cols - 1) / cols;
          double s = Math.Min(1.0, Math.Min(availW / (cols * step), availH / (rows * step)));
          if (s > best + 0.0001)
          {
            // The first count to reach a scale wins its ties, which keeps the balanced wall out of the
            // argument: six and five in two rows, not nine and two because the ninth plate was lonely.
            best = s;
            bestCols = cols;
          }
        }

        double scale = Math.Max(best, WALL_FLOOR);
        if (Math.Abs(scale - _wallFitScale) < 0.0005 && bestCols == _wallFitCols)
          return;

        _wallFitScale = scale;
        _wallFitCols = bestCols;
        wallList.Width = bestCols * step;
        wallScale.LayoutTransform = scale < 0.9995
            ? new Avalonia.Media.ScaleTransform(scale, scale)
            : null;
      }
      finally
      {
        _wallFitting = false;
      }
    }

    /// <summary>
    /// The room one plate and its gap take in the worn skin's own numbers - the square plus the margin the
    /// template hangs around it. A skin that keeps no numbers of its own for the wall answers with the
    /// markup's own plate and gap, which is what that skin draws.
    /// </summary>
    private double WallPlateStep()
    {
      double plate = SkinEngine.WornNumber("SettingsTilePlateWidth") ?? 355;
      object? found = null;
      if (Application.Current != null)
        Application.Current.TryFindResource("SettingsTileMargin", out found);
      double gap = found is Avalonia.Thickness t ? (t.Left + t.Right) / 2 : 8;
      return plate + gap * 2;
    }

    /// <summary>
    /// Tells the shell which word stands at the foot of the window for this screen. The hub lives on one
    /// workflow frame labelled "Settings", so a level walked into would otherwise keep the frame's word;
    /// the corner caption instead names the room actually being looked at - an open page, a folder's wall,
    /// or the first wall. See <see cref="ShellWindow.SetScreenCaption"/>.
    /// </summary>
    private void ApplyHeaderTitle()
    {
      _chromeShell?.SetScreenCaption(_model.ScreenName);
    }

    /// <summary>
    /// Dropping the places the two lists remember: the selected one, and the anchor a single-select list walks
    /// back to when the next plate is pressed.
    /// </summary>
    private void ForgetPlaces()
    {
      Forget(wallList);
    }

    private static void Forget(ListBox list)
    {
      list.SelectedIndex = -1;
      list.Selection.AnchorIndex = -1;
    }

    /// <summary>Runs every time the workflow shows this screen. See IWorkflowScreen.</summary>
    public void OnScreenActivated()
    {
      // A frame pushed over the hub and left again makes the shell name the header from the frame's label,
      // which is "Settings" however deep the viewer had walked inside the hub before the dialog came. The
      // word is spoken again here because this call is the hub's notice that it is back on screen.
      ApplyHeaderTitle();

      if (_model.ShowingContents)
        StandOnOpenScreen(CARET_TRIES); // returning to a frame over the hub returns to the open page too
      else
        StandCaretOn(Target(), CARET_TRIES);
    }

    /// <summary>
    /// Where the shell's net starts looking for the keyboard: the wall of plates while a wall stands, and
    /// nothing while a page is open. The "nothing" is deliberate: without it the net takes the first
    /// focusable control in tree order, which is a plate of the invisible wall behind the page - the keys
    /// then walk around on a wall nobody can see, and every press lands on the wrong thing. A page holds
    /// its own keyboard (see StandOnOpenScreen), so the net has nothing to improve there.
    /// See <see cref="IWorkflowScreen.KeysStartAt"/>.
    /// </summary>
    public Control? KeysStartAt()
    {
      return _model.ShowingContents ? null : wallList;
    }

    /// <summary>
    /// A page opened beside the lists asking the keyboard for one of its own controls - the input devices
    /// editor hands over its chooser so that the first key a viewer presses means something inside the page.
    /// The hub takes the ask by handing it to the model, which stands the keyboard on it as soon as the control
    /// can be drawn for it.
    /// </summary>
    public void AskFocusFrom(Control wanted)
    {
      _model.FocusWanted = wanted;
      if (_model.ShowingContents)
        StandOnOpenScreen(CARET_TRIES); // the asked control may need a layout pass before it can take the keyboard
    }

    /// <summary>
    /// Moving the keyboard onto the screen that is open: the control the page asked for, failing that the first
    /// focusable thing inside it. The content host has only a ContentControl standing in the page's rows, so an
    /// ask that lands on nothing yet is repeated a bounded number of layout passes later, while the same screen
    /// is still the one open. A list that takes the keyboard is not enough - the viewer must SEE where they
    /// stand - so the first row's container is focused too, without selecting it (a selection here would open
    /// the entry).
    /// </summary>
    private void StandOnOpenScreen(int tries)
    {
      Control? view = _model.CurrentView;
      if (view == null || !_model.ShowingContents)
        return;

      if (StandOnOpenScreenOnce(view))
        return;

      if (tries <= 0)
        return;

      // The retry rides a timer, not a posted delegate - see StandCaretOn for why a self-re-posting Loaded
      // delegate grows the stack instead of the queue.
      Control was = view;
      DispatcherTimer asking = new DispatcherTimer(DispatcherPriority.Loaded)
      {
        Interval = TimeSpan.FromMilliseconds(16)
      };
      asking.Tick += delegate
      {
        if (!_model.ShowingContents || !ReferenceEquals(_model.CurrentView, was))
        {
          asking.Stop(); // the viewer walked on while the layout was settling
          return;
        }

        if (StandOnOpenScreenOnce(was))
        {
          asking.Stop();
          return;
        }

        if (--tries <= 0)
          asking.Stop();
      };
      asking.Start();
    }

    /// <summary>
    /// One attempt at putting the keyboard onto the open screen: the control it asked for, the screen itself,
    /// or the first focusable thing inside it. Answers whether the keyboard came to rest there.
    /// </summary>
    private bool StandOnOpenScreenOnce(Control view)
    {
      Control? wanted = _model.FocusWanted;
      if (wanted != null && wanted.Focus())
      {
        if (wanted is ListBox stood)
          StandCaretOnPlate(stood, 0);
        return true;
      }

      if (view.IsKeyboardFocusWithin)
        return true; // the page already holds the keyboard, wherever inside it it chose to stand

      if (view.Focus())
        return true;

      foreach (Control child in view.GetVisualDescendants().OfType<Control>())
      {
        if (child.Focusable && child.IsEffectivelyVisible && child.Focus())
          return true;
      }

      return false;
    }

    /// <summary>
    /// How often a caret asks again when the plates it wants are not drawn yet - the same bounded asking
    /// the news column does (NewsView), because a list whose rows are still arriving answers the first
    /// arrow key with a page rather than a row.
    /// </summary>
    private const int CARET_TRIES = 6;

    /// <summary>
    /// Stands the caret ON a row of the wall: the first row - never picked, only focused, because a picked
    /// plate here is an opened section. Asks again on a timer while the list holds the keyboard, because the
    /// containers may not be drawn the moment the screen is activated. Named apart from its Control-taking
    /// sibling on purpose: same-name overloads that dispatch through a base-typed argument are how this
    /// method ended up calling itself twenty-four thousand deep.
    /// </summary>
    private void StandCaretOnPlate(ListBox list, int tries)
    {
      // The caret is the focus here: a picked plate IS an opened section, so standing on the wall means
      // focusing a plate WITHOUT selecting it. Avalonia's own focus navigation would set SelectedIndex (and
      // open a section); that is why every arrow of this wall is answered by hand below.
      if (StandThePlate(list))
        return;

      if (tries <= 0 || list.Items.Count == 0)
        return;

      list.Focus();

      // A plate can only take the keyboard once it has been DRAWED, and on the machine that is a second or
      // more after the screen came up - the wall is laid out slowly there. Asking six times over a tenth of
      // a second asked before the plates existed at all (the log said so: container null at every ask, and
      // one second later the first arrow found them drawn). So the asking spans two seconds instead, and
      // stops the moment a plate holds the keyboard.
      DispatcherTimer asking = new DispatcherTimer(DispatcherPriority.Loaded)
      {
        Interval = TimeSpan.FromMilliseconds(50)
      };
      tries = 40;
      asking.Tick += delegate
      {
        if (!list.IsKeyboardFocusWithin || list.Items.Count == 0)
        {
          asking.Stop(); // the keys left the wall, or the wall emptied - nothing further to stand on
          return;
        }

        // The shell's net hands the keys to the LIST itself while no plate exists to hold them. That is not
        // the caret: a list with the keyboard and no plate under it shows nothing and answers no arrow. As
        // long as the keys sit on the bare list, the asking keeps going until a plate is drawn.
        if (StandThePlate(list))
        {
          asking.Stop();
          return;
        }

        if (--tries <= 0)
        {
          asking.Stop(); // asked as often as a wall is worth asking; where it stands now is where it stands
        }
      };
      asking.Start();
    }

    /// <summary>
    /// One attempt at standing the caret: focuses the plate the caret belongs to (the selected row's, else
    /// the first) without selecting anything. Answers whether the plate took the keyboard.
    /// </summary>
    private static bool StandThePlate(ListBox list)
    {
      int index = list.SelectedIndex >= 0 ? list.SelectedIndex : 0;
      if (list.Items.Count == 0)
        return false;

      Control? plate = list.ContainerFromIndex(index) as Control;
      if (plate == null)
        return false; // not drawn yet - the caller keeps asking until the first layout pass draws it

      // A plate that cannot show itself cannot take the keyboard either, and asking it anyway is worse than
      // waiting: Avalonia walks focus up to the list when the item refuses, which looks like a working caret
      // until the first arrow key proves otherwise.
      if (!plate.IsEffectivelyVisible || !plate.IsEffectivelyEnabled)
        return false;

      plate.Focus();
      return list.IsKeyboardFocusWithin;
    }

    /// <summary>
    /// The wall's caret asked for by control: only a list of plates can carry a caret, and anything else the
    /// hub may name (a page's plain view) is simply not stood on. Named apart from <see cref="StandCaretOnPlate"/>
    /// because two same-named overloads dispatching through a base-typed argument are what once made this
    /// call itself forever.
    /// </summary>
    private void StandCaretOn(Control target, int tries)
    {
      if (target is ListBox list)
        StandCaretOnPlate(list, tries);
    }

    /// <summary>The number of the plate the caret stands on, or -1 while no plate of the wall has it.</summary>
    private int StoodPlate()
    {
      for (int i = 0; i < wallList.Items.Count; i++)
      {
        if (wallList.ContainerFromIndex(i) is ListBoxItem { IsKeyboardFocusWithin: true })
          return i;
      }

      return -1;
    }

    /// <summary>
    /// The plate one row above (-1) or below (+1) the stood one: the wall wraps, so a row is what the
    /// containers' own places say, and the neighbour of a plate is the plate of the next row that stands
    /// nearest under it. -1 when the wall has no such row - the top has nothing above it.
    /// </summary>
    private int RowNeighbour(int index, int down)
    {
      if (wallList.ContainerFromIndex(index) is not Control stood)
        return -1;

      double stoodTop = stood.Bounds.Top;
      double rowTop = double.NaN;
      for (int i = 0; i < wallList.Items.Count; i++)
      {
        if (i == index || wallList.ContainerFromIndex(i) is not Control plate || !plate.IsEffectivelyVisible)
          continue;

        double top = plate.Bounds.Top;
        bool beyond = down > 0 ? top > stoodTop + 10 : top < stoodTop - 10;
        if (beyond && (double.IsNaN(rowTop) || (down > 0 ? top < rowTop : top > rowTop)))
          rowTop = top;
      }

      if (double.IsNaN(rowTop))
        return -1;

      double stoodCenter = stood.Bounds.Center.X;
      int best = -1;
      double bestDistance = double.MaxValue;
      for (int i = 0; i < wallList.Items.Count; i++)
      {
        if (wallList.ContainerFromIndex(i) is not Control plate || !plate.IsEffectivelyVisible)
          continue;
        if (Math.Abs(plate.Bounds.Top - rowTop) > 10)
          continue;

        double distance = Math.Abs(plate.Bounds.Center.X - stoodCenter);
        if (distance < bestDistance)
        {
          bestDistance = distance;
          best = i;
        }
      }

      return best;
    }

    private void OnWallKeysTunnel(object? sender, KeyEventArgs e)
    {
      if (!_model.IsWall)
        return;

      int stood = StoodPlate();
      if (stood < 0)
        return;

      if (Settings.KeyBindings.Current.Is(Settings.KeyAction.Select, e))
      {
        // The press of the plate the viewer stands on. Selection stays out of it: the press goes to the hub
        // as the pointer's press of the same plate goes, and the caret alone does not pick.
        if (wallList.Items[stood] is SettingsSection plate)
        {
          e.Handled = true;
          _ = _model.OpenAsync(plate);
        }

        return;
      }

      int to = e.Key switch
      {
        Key.Left => stood - 1,
        Key.Right => stood + 1,
        Key.Up => RowNeighbour(stood, -1),
        Key.Down => RowNeighbour(stood, 1),
        _ => -2
      };

      if (to == -2)
        return; // not an arrow

      // The wall is fitted to the screen as a whole (FitWall shrinks it until every plate stands on the
      // television), so where no neighbour exists there is nothing to scroll to either: the absence of a
      // row or plate in the direction asked IS the edge. Earlier this asked the scroller whether it could
      // still travel, and since a fitted wall never scrolls the answer was "no edge" - the bounce never
      // leaned on the machine. One wall now answers all four sides.
      if (to < 0)
      {
        NodWall(e.Key == Key.Left ? -ShelfRail.RAIL_BOUNCE_DIVE : 0,
                e.Key == Key.Down ? ShelfRail.RAIL_BOUNCE_DIVE : (e.Key == Key.Up ? -ShelfRail.RAIL_BOUNCE_DIVE : 0));
        e.Handled = true;
        return;
      }

      if (to >= wallList.Items.Count)
      {
        // The last plate of the last row: right into the side wall.
        NodWall(ShelfRail.RAIL_BOUNCE_DIVE, 0);
        e.Handled = true;
        return;
      }

      if (wallList.ContainerFromIndex(to) is Control next && next.IsEffectivelyVisible)
      {
        e.Handled = true;
        next.Focus();
        return;
      }

      // A plate the virtualized wall has not drawn yet is asked for by number: the scroll realizes it and
      // the caret steps onto it a layout later. The key is kept meanwhile - letting it out here would hand
      // the list a selection to commit, and a committed selection is an opened section.
      e.Handled = true;
      wallList.ScrollIntoView(to);
      StandOnRealizedPlate(to, CARET_TRIES);
    }

    /// <summary>
    /// Leans the wall's visible ground toward the wall the viewer pointed at and springs it back. Nods the
    /// scroller rather than one plate because the wall scrolls as one ground and every plate on the screen
    /// should say "edge" together, the way the shelf's rail does.
    /// </summary>
    private void NodWall(double diveX, double diveY)
    {
      // The ground that leans is the scaled host of the wall: the wall scrolls as one ground and every plate
      // on the screen should say "edge" together, the way the shelf's rail does. There is no ScrollViewer
      // around this wall (it is fitted to the screen so it never scrolls), and leaning the ListBox alone
      // would lean only the plates' block - the host carries the whole of it.
      Control? ground = wallScale;
      if (ground == null)
        return;

      _wallNods.Nod(ground, diveX, diveY);
    }

    private void StandOnRealizedPlate(int index, int tries)
    {
      if (wallList.ContainerFromIndex(index) is Control plate && plate.IsEffectivelyVisible)
      {
        plate.Focus();
        return;
      }

      if (tries <= 0)
        return;

      // The retry rides a timer, not a posted delegate: a delegate that re-posts itself from inside its own
      // invocation grows the stack one frame per ask (Avalonia pumps lower priorities from within a Loaded
      // item), which is how this file crashed twice with a stack overflow. A timer tick always runs at the
      // top of the queue.
      DispatcherTimer asking = new DispatcherTimer(DispatcherPriority.Loaded)
      {
        Interval = TimeSpan.FromMilliseconds(16)
      };
      asking.Tick += delegate
      {
        if (!wallList.IsKeyboardFocusWithin || !_model.IsWall)
        {
          asking.Stop();
          return;
        }

        if (wallList.ContainerFromIndex(index) is Control stood && stood.IsEffectivelyVisible)
        {
          stood.Focus();
          asking.Stop();
          return;
        }

        if (--tries <= 0)
          asking.Stop();
      };
      asking.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
      // The back key has its answer in the hub's constructor, on the bubble road with handledEventsToo -
      // one step down the trail before any page's unclaimed Back reaches the shell.

      base.OnKeyDown(e);
    }

    /// <summary>
    /// Takes exactly one step down the hub's own trail: out of an open screen onto the wall that held its
    /// plate, or from a folder's wall up to the level above it. Answers whether the step was spent; false
    /// means the hub stands at its first level and the caller may leave the room. The shell window asks this
    /// directly when a Back bubbles past the hub without meeting the keyboard inside it, so a deep page is
    /// never mistaken for the door.
    /// </summary>
    public bool StepBackInsideHub()
    {
      if (!_model.GoBack())
        return false;

      var back = Target();
      back.Focus();
      StandCaretOn(back, CARET_TRIES);
      return true;
    }

    /// <summary>The control a press after going back walks from: the wall of plates. An open screen keeps its
    /// own keyboard (the page itself, or the list it asked for), so the hub names no target while one stands.</summary>
    private Control Target()
    {
      return wallList;
    }
  }
}