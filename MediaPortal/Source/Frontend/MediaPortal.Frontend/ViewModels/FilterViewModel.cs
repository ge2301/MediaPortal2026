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
using System.Collections.ObjectModel;
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
  /// </remarks>
  public sealed partial class FilterViewModel : ObservableObject
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
    /// What stands above the tiles: the shelf and the counting, in that order, because the viewer came out of that
    /// shelf and the counting is the new word. "Movies by year", the same words the shelf's own menu rows wear.
    /// </summary>
    public string Heading { get { return Shelf.Label + " by " + Word; } }

    /// <summary>The sentence under the heading, which says what a press does before the first press.</summary>
    public string Subtitle
    {
      get
      {
        return "Every " + Word + " the titles of this shelf carry, " + Shelf.Label + " counted by the server, "
            + "biggest first - press OK for the titles that carry the one you stand on.";
      }
    }

    /// <summary>The values this shelf's titles carry, biggest first, with the number that justifies each row.</summary>
    public ObservableCollection<BrowseValue> Values { get; } = new ObservableCollection<BrowseValue>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private BrowseValue? _selected;

    [ObservableProperty]
    private bool _isBusy;

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
          Values.Add(new BrowseValue(group.Value!, group.Count));
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
          Status = Values.Count + " " + PluralWord + " among " + Shelf.Label + ", biggest first.";
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
  }
}
