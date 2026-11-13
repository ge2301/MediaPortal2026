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
using ManagedBass;

namespace MediaPortal.Frontend.Player
{
  /// <summary>
  /// The codec add-ons BASS is told about, and which of them actually answered.
  /// </summary>
  /// <remarks>
  /// <para>
  /// BASS core decodes mp3, flac, ogg, wav and aiff; everything else a music library holds arrives as an
  /// add-on of the same licence, from the same publisher, fetched by the same script. Loading them is not a
  /// preference: an AAC file either has its add-on next to the library or it does not play, and the difference
  /// between those two states has to be a line in the settings rather than a mystery.
  /// </para>
  /// <para>
  /// So this class does two things: it loads what it finds next to <c>bass.dll</c>, and it remembers what is
  /// missing. An add-on that is not there is not a failure - a listener without WavPack files never notices -
  /// but it is reported, because the settings page promises a list of formats and a promise kept quietly is
  /// still a promise.
  /// </para>
  /// <para>
  /// The names are the ones the fetched archives actually contain (measured against them, not remembered):
  /// <c>bass_aac.dll</c> on Windows, <c>libbass_aac.so</c> on Linux, <c>libbass_mpc.dylib</c> on macOS, and
  /// two of the thirteen - WMA and AAC - are not shipped for every platform, which is why the missing list is
  /// never treated as damage.
  /// </para>
  /// </remarks>
  public static class BassPlugins
  {
    /// <summary>The add-on base names, in the order BASS should be offered them (the order decides ties).</summary>
    public static readonly IReadOnlyList<string> AddOnNames = new[]
    {
      "bass_aac", "bass_ac3", "bassopus", "basswma", "bassalac", "bassape", "bassdsd", "basshls",
      "bassmidi", "bass_mpc", "bass_spx", "bass_tta", "basswv"
    };

    static readonly Dictionary<string, int> s_handles = new Dictionary<string, int>(StringComparer.Ordinal);
    static readonly List<string> s_missing = new List<string>();
    static bool s_attempted;

    /// <summary>The add-ons that answered, by base name.</summary>
    public static IReadOnlyList<string> Loaded => new List<string>(s_handles.Keys);

    /// <summary>The add-ons that were looked for and were not there, by base name.</summary>
    public static IReadOnlyList<string> Missing => s_missing;

    /// <summary>True once a load has been attempted, so that "nothing loaded" can mean two different things.</summary>
    public static bool Attempted => s_attempted;

    /// <summary>
    /// The file types the loaded add-ons answer for, as lowercase extensions without repetition. A codec that is
    /// missing is simply not in the list, which is the honest way of reporting it.
    /// </summary>
    public static IReadOnlyList<string> Formats()
    {
      HashSet<string> extensions = new HashSet<string>(StringComparer.Ordinal);
      foreach (KeyValuePair<string, int> taken in s_handles)
      {
        // An add-on somebody else had already plugged in has a handle we do not know, so it reports nothing.
        if (taken.Value == 0)
          continue;

        PluginFormat[]? formats = Bass.PluginGetInfo(taken.Value).Formats;
        if (formats == null)
          continue;
        foreach (PluginFormat format in formats)
        {
          string? list = format.FileExtensions;
          if (string.IsNullOrEmpty(list))
            continue;
          foreach (string part in list!.Split(';', StringSplitOptions.RemoveEmptyEntries))
          {
            string extension = part.Trim().ToLowerInvariant().TrimStart('*');
            if (extension.Length > 0)
              extensions.Add(extension);
          }
        }
      }
      List<string> sorted = new List<string>(extensions);
      sorted.Sort(StringComparer.Ordinal);
      return sorted;
    }

    /// <summary>
    /// One line for a settings row or a console: what is in, what is out.
    /// </summary>
    public static string Describe()
    {
      if (!s_attempted)
        return "no add-on has been looked for";
      if (s_missing.Count == 0 && s_handles.Count > 0)
        return s_handles.Count + " codec add-ons";
      if (s_handles.Count == 0)
        return s_missing.Count == AddOnNames.Count
            ? "no codec add-on found"
            : s_handles.Count + " of " + AddOnNames.Count + " codec add-ons";
      return s_handles.Count + " of " + AddOnNames.Count + " codec add-ons (missing: " +
             string.Join(", ", s_missing) + ")";
    }

    /// <summary>
    /// Load every add-on in the folders this run could have fetched them into. Safe to call twice; the second
    /// call answers from what the first learned, because BASS keeps one copy of an add-on per process.
    /// </summary>
    /// <param name="libraryDirectory">The folder <c>bass.dll</c> itself came from; may be null.</param>
    /// <returns>How many add-ons are now loaded, including the ones a previous call loaded.</returns>
    public static int Load(string? libraryDirectory)
    {
      if (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
      {
        // iOS does not permit dynamic libraries at all, so un4seen ships the add-ons as static libraries that
        // have to be linked into the app package. Nothing to load here, and nothing to report as missing.
        s_attempted = true;
        return s_handles.Count;
      }

      s_attempted = true;
      foreach (string directory in SearchFolders(libraryDirectory))
      {
        foreach (string name in AddOnNames)
        {
          if (s_handles.ContainsKey(name))
            continue;
          string path = Path.Combine(directory, FileName(name));
          if (!File.Exists(path))
            continue;

          IntPtr handle = Bass.PluginLoad(path);
          if (handle != IntPtr.Zero)
          {
            s_handles[name] = (int)handle;
            s_missing.Remove(name);
            continue;
          }

          // BASS answers "already" when the same add-on was plugged in before, which is the same success.
          if (Bass.LastError == Errors.Already)
          {
            s_handles[name] = 0;
            s_missing.Remove(name);
            continue;
          }
          // Anything else (wrong bitness, wrong platform) leaves the add-on on the missing list rather than
          // pretending the file is there and working.
          s_handles.Remove(name);
        }
      }

      foreach (string name in AddOnNames)
      {
        if (!s_handles.ContainsKey(name) && !s_missing.Contains(name))
          s_missing.Add(name);
      }

      return s_handles.Count;
    }

    /// <summary>The file name one add-on has on this platform.</summary>
    public static string FileName(string addOnName)
    {
      if (OperatingSystem.IsWindows())
        return addOnName + ".dll";
      return "lib" + addOnName + (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ||
                                  OperatingSystem.IsMacCatalyst()
          ? ".dylib"
          : ".so");
    }

    /// <summary>The folders to look in: the library's own folder first, then the program's, then the fetched one.</summary>
    static IEnumerable<string> SearchFolders(string? libraryDirectory)
    {
      HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
      if (!string.IsNullOrEmpty(libraryDirectory) && Directory.Exists(libraryDirectory!))
      {
        seen.Add(Path.GetFullPath(libraryDirectory!));
        yield return libraryDirectory!;
      }

      string program = AppContext.BaseDirectory;
      if (seen.Add(Path.GetFullPath(program)))
        yield return program;

      foreach (string runtimeDirectory in NativeBass.RuntimeDirectoryNames())
      {
        foreach (string root in FetchedRoots(program))
        {
          string candidate = Path.Combine(root, "libs", "bass", runtimeDirectory);
          if (Directory.Exists(candidate) && seen.Add(Path.GetFullPath(candidate)))
            yield return candidate;
        }
      }
    }

    static IEnumerable<string> FetchedRoots(string programDirectory)
    {
      DirectoryInfo? directory = new DirectoryInfo(programDirectory);
      for (int depth = 0; directory != null && depth < 7; depth++, directory = directory.Parent)
        yield return directory.FullName;
    }
  }
}
