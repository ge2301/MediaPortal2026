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

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MediaPortal.Frontend.Api;
using MediaPortal.Frontend.Input;
using MediaPortal.Frontend.ViewModels;
using MediaPortal.Frontend.Workflow;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaPortal.Frontend.Views
{
  /// <summary>
  /// One counting of one shelf on a screen of its own: the years, or the genres, one tile apiece.
  /// </summary>
  /// <remarks>
  /// What only a screen can add: the tiles are walked as a wall is walked - left and right one tile, up and down
  /// a whole row of them, the number of rows the panel says it laid them in - and Enter opens the titles that
  /// carry the tile the caret stands on. Back leaves the counting the way it found it, because nothing here has
  /// been changed, only counted; the shelf a value opened is a stop of its own on the way back.
  /// </remarks>
  public partial class FilterView : UserControl, IWorkflowScreen
  {
    /// <summary>The panel a wall of tiles is laid in: the one the shelf's grid and the people's wall use.</summary>
    private static readonly FuncTemplate<Panel?> TILE_PANEL =
        new FuncTemplate<Panel?>(() => new VirtualizingTilePanel());

    private readonly FilterViewModel? _values;

    /// <summary>For the XAML loader and the designer: the counting a front end with no server shows.</summary>
    public FilterView()
        : this(new FilterViewModel(null, new HomeSection("Movies", new AspectViewModel(new MediaAspect(
            Guid.Empty, "MovieItem", false, false, null, Array.Empty<AspectAttribute>()))), "year"))
    {
    }

    /// <param name="values">One counting of one shelf: its years or its genres.</param>
    /// <param name="workflow">The workflow this screen stands in, for the frame a pressed value opens.</param>
    /// <param name="openShelf">
    /// Builds the stop that shows the titles carrying one value, which only the application can do - it is the
    /// application that knows how a shelf is opened, what it is headed, and what its player does with a title.
    /// Null on a screen with nowhere to go.
    /// </param>
    public FilterView(FilterViewModel values, WorkflowManager? workflow = null,
        Func<HomeSection, WorkflowFrame>? openShelf = null)
    {
      DataContext = values;
      _values = values;
      InitializeComponent();

      // The tiles are laid as a wall, which is the shelf's business and not the markup's: the panel wraps whole
      // tiles, keeps a thousand of them off the tree, and says how many columns it laid - which is what the up and
      // down arrows below need to know before they can move a row.
      valuesList.ItemsPanel = TILE_PANEL;

      var ring = new List<Control> { valuesList };
      DpadNavigation.Attach(this, ring);
      MouseRemote.Attach(this, valuesList);

      if (openShelf != null && workflow != null)
      {
        _values.ValueChosen += delegate (object? sender, HomeSection shelf)
        {
          // The cut shelf is pushed over this screen, so Back from the titles leads back onto the value that was
          // pressed rather than out of the library.
          WorkflowFrame? frame = openShelf(shelf);
          if (frame != null)
            workflow.Push(frame);
        };
      }

      AddHandler(KeyDownEvent,
          delegate (object? sender, KeyEventArgs args)
          {
            if (args.Handled || !valuesList.IsKeyboardFocusWithin)
              return;

            if (args.Key == Key.Enter || args.Key == Key.Return)
            {
              if (_values.OpenCommand.CanExecute(null))
              {
                _values.OpenCommand.Execute(null);
                args.Handled = true;
              }
              return;
            }

            WalkTheWall(args);
          },
          RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Moves the caret one tile - or one whole row - over, and says whose key the arrow was.
    /// </summary>
    /// <remarks>
    /// Asked of the panel rather than counted here, because how many tiles stand in a row is a measurement of the
    /// room the wall was given and not a number anybody may write down: the same screen at a different width has
    /// different rows, and up would otherwise mean whatever the markup happened to say.  An arrow that would walk
    /// off the wall is handed back unclaimed, which is what every other screen of this front end does at its edges.
    /// </remarks>
    private void WalkTheWall(KeyEventArgs args)
    {
      int step;
      switch (args.Key)
      {
        case Key.Left:
          step = -1;
          break;
        case Key.Right:
          step = 1;
          break;
        case Key.Up:
          step = -Columns();
          break;
        case Key.Down:
          step = Columns();
          break;
        default:
          return;
      }

      int stood = _values == null ? -1 : _values.Values.IndexOf(_values.Selected!);
      if (stood < 0)
        return;

      int to = stood + step;
      if (_values == null || to < 0 || to >= _values.Values.Count)
        return;

      _values.Selected = _values.Values[to];
      valuesList.ScrollIntoView(_values.Selected);

      // The tile itself takes the caret, because the ring the skin lights is the ring of the container that has
      // the focus - a wall whose caret only moves its selection is a wall that answers the arrows with nothing.
      if (valuesList.ContainerFromIndex(to) is Control tile && tile.IsEffectivelyVisible)
        tile.Focus();

      args.Handled = true;
    }

    /// <summary>How many tiles one row of the wall holds at the width this screen was measured with.</summary>
    private int Columns()
      => valuesList.GetVisualDescendants().OfType<VirtualizingTilePanel>().FirstOrDefault()?.Columns ?? 1;

    /// <summary>
    /// Runs every time the workflow shows this screen: the first showing counts, every showing takes the focus.
    /// See <see cref="IWorkflowScreen"/>.
    /// </summary>
    public void OnScreenActivated()
    {
      // The counting is asked for each time the screen is stood on, for the reason the browse screen gives its own
      // columns: a shelf gains titles while a viewer walks about, and a wall of stale counts sends them to titles
      // that are not there.  The command guards itself against a screen with no server.
      _values?.ReadCommand.Execute(null);

      // The tiles take the focus so the first arrow moves the caret instead of saying nothing.
      valuesList.Focus();
    }
}
}
