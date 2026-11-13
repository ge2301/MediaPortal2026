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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ManagedBass;

namespace MediaPortal.Frontend.Player
{
  /// <summary>
  /// Finding BASS on the machine this runs on, loading it, and setting the two configuration values the
  /// wrapper cannot set.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Same arrangement as <see cref="NativeLibVlc"/> and for the same reason: the library is not ours to ship.
  /// Un4seen Developments publishes the binaries themselves and does not allow anyone else to redistribute
  /// them, which is also why the ManagedBass NuGet is the managed wrapper only. So the file is found at run
  /// time, and a player that cannot find one is a supported state that says so in a sentence - not a
  /// <see cref="DllNotFoundException"/> on a TV.
  /// </para>
  /// <para>
  /// One file, not a directory of plugins: that is the difference from libVLC and the reason this class hands
  /// back a path rather than a folder. <c>scripts/ci/get-bass.ps1</c> fetches it into <c>libs/bass/&lt;runtime&gt;/</c>
  /// in the repository, a published application carries it next to the executable, and both are looked for.
  /// </para>
  /// <para>
  /// Loading it is not enough on its own. ManagedBass declares its imports as <c>DllImport("bass")</c>, and
  /// .NET looks next to the executable and in the system paths - it does not know about a folder we found
  /// ourselves. So the file is loaded by path first and the handle is handed to a resolver registered on the
  /// wrapper's own assembly, which is the one supported way of pointing an import at a location the loader
  /// would not think of.
  /// </para>
  /// <para>
  /// Windows on ARM is worth one sentence of its own: un4seen ships no win-arm64 library at all (checked
  /// against the archive list on their download page), so that platform asks for <c>win-x64</c> and lets the
  /// emulator do what it is good at. Asking for ARM64 first anyway means that the day a native build appears
  /// it is used without a change here.
  /// </para>
  /// </remarks>
  public static class NativeBass
  {
    /// <summary>Environment variable holding a directory (or the library itself) to look at first.</summary>
    public const string EnvironmentVariableName = "MP2_BASS";

    /// <summary>
    /// How many milliseconds of audio BASS keeps buffered in the output device.
    /// </summary>
    /// <remarks>
    /// Measured with the spike: the default of 30 ms is tight, and BASS reports the position of the mixing
    /// point, so a small buffer means the audible sound sits noticeably behind the number the seek bar shows.
    /// 150 ms is what the reference player in this tree uses; it costs a fifth of a second of delay on a
    /// pause, which nobody can hear as wrong, and it is the difference between a meter that keeps up and one
    /// that stutters when the disk has a bad day.
    /// </summary>
    public const int OutputBufferMs = 150;

    static IntPtr s_library;
    static string? s_loadedFrom;
    static bool s_resolverRegistered;

    /// <summary>File names that mean "BASS is here", per platform.</summary>
    public static IReadOnlyList<string> LibraryFileNames
    {
      get
      {
        if (OperatingSystem.IsWindows())
          return new[] { "bass.dll" };
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS())
          return new[] { "libbass.dylib", "libbass.a" };
        return new[] { "libbass.so" };
      }
    }

    /// <summary>
    /// The runtime folders to look in, best match first, including the fallbacks this platform runs instead.
    /// </summary>
    /// <remarks>
    /// The names are the .NET runtime identifiers of <c>scripts/ci/get-bass.ps1</c> so that a folder that
    /// script produced is found by this class without a translation table in between.
    /// </remarks>
    public static IReadOnlyList<string> RuntimeDirectoryNames()
    {
      Architecture architecture = RuntimeInformation.ProcessArchitecture;
      if (OperatingSystem.IsWindows())
      {
        return architecture switch
        {
          // No win-arm64 library exists (see the remarks); the x64 one runs under the emulator.
          Architecture.Arm64 => new[] { "win-arm64", "win-x64", "win-x86" },
          Architecture.X86 => new[] { "win-x86" },
          _ => new[] { "win-x64", "win-x86" }
        };
      }

      if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS())
        // One library for both Apple chips: its fat header names x86_64, i386 and arm64, measured.
        return new[] { "osx" };

      if (OperatingSystem.IsAndroid())
        return new[] { "android" };

      return architecture switch
      {
        Architecture.Arm64 => new[] { "linux-arm64", "linux-arm" },
        Architecture.Arm => new[] { "linux-arm" },
        Architecture.X86 => new[] { "linux-x86" },
        _ => new[] { "linux-x64", "linux-x86" }
      };
    }

    /// <summary>The folders a Linux (or BSD-ish) host puts shared libraries in.</summary>
    public static IReadOnlyList<string> DefaultDirectories()
    {
      if (OperatingSystem.IsWindows() || OperatingSystem.IsAndroid() ||
          OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
        return Array.Empty<string>();
      if (OperatingSystem.IsMacOS())
        return new[] { "/usr/local/lib", "/opt/homebrew/lib" };

      // The multiarch tuple differs per architecture, so all plausible ones are listed rather than guessed.
      return new[]
      {
        "/usr/lib/x86_64-linux-gnu",
        "/usr/lib/aarch64-linux-gnu",
        "/usr/lib/arm-linux-gnueabihf",
        "/usr/lib64",
        "/usr/lib",
        "/usr/local/lib"
      };
    }

    /// <summary>The library this process loaded, or null when the system loader is being trusted instead.</summary>
    public static string? LoadedFrom => s_loadedFrom;

    /// <summary>
    /// Where the library is, or why this run has to go without it.
    /// </summary>
    /// <param name="configuredDirectory">
    /// A directory - or the library file itself - from the caller (<see cref="PlayerOptions.LibraryDirectory"/>).
    /// Null to skip that step.
    /// </param>
    /// <param name="libraryPath">
    /// The file to load, or null meaning "no file - let the platform's loader find it", which is a working
    /// answer on Linux and not one on Windows.
    /// </param>
    /// <param name="detail">What was looked at and what came of it; always filled.</param>
    /// <returns>True when a player may be built; false with the reason in <paramref name="detail"/>.</returns>
    public static bool TryFind(string? configuredDirectory, out string? libraryPath, out string detail)
    {
      libraryPath = null;
      List<string> searched = new List<string>();

      string? configured = configuredDirectory;
      if (string.IsNullOrWhiteSpace(configured))
        configured = Environment.GetEnvironmentVariable(EnvironmentVariableName);

      if (!string.IsNullOrWhiteSpace(configured!))
      {
        if (IsLibraryFile(configured!, out string? chosen))
        {
          libraryPath = chosen;
          detail = $"'{Path.GetFileName(chosen)}' named by configuration";
          return true;
        }
        if (TryInDirectory(configured!, out chosen))
        {
          libraryPath = chosen;
          detail = $"'{Path.GetFileName(chosen)}' found in the configured directory '{configured}'";
          return true;
        }
        detail = $"the configured BASS directory '{configured}' does not contain " +
                 string.Join(" or ", LibraryFileNames);
        return false;
      }

      // Next to the program first: that is where a published application carries its own copy, and a library
      // that arrived with the build beats a library some other program installed.
      string baseDirectory = AppContext.BaseDirectory;
      if (TryInDirectory(baseDirectory, out string? nextToProgram))
      {
        libraryPath = nextToProgram;
        detail = $"'{Path.GetFileName(nextToProgram)}' found next to the program in '{baseDirectory}'";
        return true;
      }
      searched.Add(baseDirectory);

      // Then the fetched folder, both under the program (a deployment that kept the layout) and above it (a
      // repository, where the program sits in bin\Debug\net10.0 and the libraries stand at the tree's root).
      foreach (string root in CandidateRoots(baseDirectory))
      {
        string bassRoot = Path.Combine(root, "libs", "bass");
        if (!Directory.Exists(bassRoot))
          continue;
        searched.Add(bassRoot);
        foreach (string runtimeDirectory in RuntimeDirectoryNames())
        {
          string candidate = Path.Combine(bassRoot, runtimeDirectory);
          if (TryInDirectory(candidate, out string? file))
          {
            libraryPath = file;
            detail = $"'{Path.GetFileName(file)}' found in '{candidate}'";
            return true;
          }
          searched.Add(candidate);
        }
      }

      foreach (string candidate in DefaultDirectories())
      {
        if (TryInDirectory(candidate, out string? file))
        {
          libraryPath = file;
          detail = $"'{Path.GetFileName(file)}' found in '{candidate}'";
          return true;
        }
        searched.Add(candidate);
      }

      if (OperatingSystem.IsWindows())
      {
        detail = "BASS was not found in " + Join(searched) + "; run scripts\\ci\\get-bass.ps1 or set " +
                 EnvironmentVariableName + " to the folder holding " + Join(LibraryFileNames);
        return false;
      }

      // Nothing on disk, but the loader may still resolve the SONAME - a distro package installs exactly that.
      // Whether it resolves is decided at the first call and reported as a failure then.
      libraryPath = null;
      detail = "not found in " + Join(searched) + "; asking the system loader for " + Join(LibraryFileNames);
      return true;
    }

    /// <summary>
    /// Load the library and point the wrapper's imports at it. Once per process; a second call with the same
    /// answer is a no-op, because loading the same native library twice would give two copies of one engine
    /// that believes itself to be a singleton.
    /// </summary>
    public static bool EnsureLoaded(string? libraryPath, out string detail)
    {
      if (s_library != IntPtr.Zero && string.Equals(s_loadedFrom, libraryPath, StringComparison.Ordinal))
      {
        detail = $"'{Path.GetFileName(s_loadedFrom!)}' already loaded for this process";
        return true;
      }

      if (string.IsNullOrEmpty(libraryPath))
      {
        if (s_library == IntPtr.Zero)
        {
          detail = "no library file; the platform's loader will be asked for " + Join(LibraryFileNames);
          return true;
        }
        detail = $"'{Path.GetFileName(s_loadedFrom!)}' already loaded for this process";
        return true;
      }

      try
      {
        IntPtr handle = NativeLibrary.Load(libraryPath!);
        RegisterResolver(handle);
        s_library = handle;
        s_loadedFrom = libraryPath;
        detail = $"'{Path.GetFileName(libraryPath!)}' loaded from '{Path.GetDirectoryName(libraryPath)}'";
        return true;
      }
      catch (Exception ex)
      {
        // DllNotFoundException for a 32-bit library on a 64-bit process and BadImageFormatException for a
        // library of another platform both land here, and in both cases the path is the useful half.
        detail = $"'{libraryPath}' could not be loaded: {ex.Message}";
        return false;
      }
    }

    /// <summary>
    /// The configuration BASS needs before it opens a device.
    /// </summary>
    /// <remarks>
    /// ManagedBass 4.0.2 wraps <c>BASS_GetConfig</c> and not <c>BASS_SetConfig</c> - measured as "does not
    /// contain a definition for 'SetConfig'" - so the one call the player needs is made here against the
    /// library the resolver just pointed at. The option number comes from the wrapper's own enumeration, so a
    /// future wrapper that spells the same option differently is noticed at compile time rather than at the
    /// first seek.
    /// </remarks>
    public static bool Configure(int outputBufferMs, out string detail)
    {
      bool buffer = NativeMethods.SetConfig((int)Configuration.DeviceBufferLength, outputBufferMs);
      int now = NativeMethods.GetConfig((int)Configuration.DeviceBufferLength);
      detail = buffer
          ? $"output buffer {now} ms"
          : $"the output buffer could not be set (it stayed at {now} ms)";
      return buffer;
    }

    /// <summary>
    /// Tell the wrapper's assembly that "bass" means the handle this class loaded.
    /// </summary>
    /// <remarks>
    /// Registered against the wrapper's own assembly, because that is the assembly carrying the import - a
    /// resolver registered against this one would never be consulted. The handle is never freed: BASS holds
    /// state that outlives a player (the device, the plugins), and unloading the library under it is how a
    /// process dies on exit rather than on start.
    /// </remarks>
    static void RegisterResolver(IntPtr handle)
    {
      if (s_resolverRegistered)
        return;
      Assembly wrapper = typeof(Bass).Assembly;
      DllImportResolver resolver = delegate(string name, Assembly _, DllImportSearchPath? __)
      {
        // Every import either assembly declares is "bass"; anything else is not ours to answer.
        if (!name.Equals("bass", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("bass.dll", StringComparison.OrdinalIgnoreCase))
          return IntPtr.Zero;
        return handle;
      };
      // Both assemblies, because a resolver is consulted for the imports of the assembly it was registered
      // on: the wrapper's for its own calls, this one for <see cref="NativeMethods"/>.
      NativeLibrary.SetDllImportResolver(wrapper, resolver);
      if (wrapper != typeof(NativeBass).Assembly)
        NativeLibrary.SetDllImportResolver(typeof(NativeBass).Assembly, resolver);
      s_resolverRegistered = true;
    }

    /// <summary>
    /// The program folder and its parents, deepest first, where a repository's <c>libs</c> may stand.
    /// </summary>
    /// <remarks>
    /// Twelve floors, which is more than any layout here needs and still stops somewhere: the front end's own
    /// program folder sits seven floors under the tree's root (<c>bin\Debug\net10.0</c> under
    /// <c>Source\Frontend\MediaPortal.Frontend</c>), so a walk that stops short of that finds the fetched
    /// library for a test binary and misses it for the application that was built for - which is the worse
    /// way round. Each stop is only a folder existence check.
    /// </remarks>
    static IEnumerable<string> CandidateRoots(string baseDirectory)
    {
      DirectoryInfo? directory = new DirectoryInfo(baseDirectory);
      for (int depth = 0; directory != null && depth < 12; depth++, directory = directory.Parent)
        yield return directory.FullName;
    }

    static bool TryInDirectory(string directory, out string? libraryFile)
    {
      libraryFile = null;
      if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        return false;
      foreach (string name in LibraryFileNames)
      {
        string candidate = Path.Combine(directory, name);
        if (File.Exists(candidate))
        {
          libraryFile = candidate;
          return true;
        }
      }
      return false;
    }

    static bool IsLibraryFile(string path, out string? libraryFile)
    {
      libraryFile = null;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return false;
      foreach (string name in LibraryFileNames)
      {
        if (Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
          libraryFile = path;
          return true;
        }
      }
      return false;
    }

    static string Join(IReadOnlyList<string> items)
    {
      return items.Count == 0 ? "nothing" : string.Join(", ", items);
    }

    /// <summary>True when this process already has the library in hand.</summary>
    public static bool IsLoaded => s_library != IntPtr.Zero;

    /// <summary>
    /// The output buffer BASS now believes it has, in milliseconds; -1 when it is not loaded or does not answer.
    /// </summary>
    /// <remarks>
    /// Asked for by the settings page, which would otherwise print the number it wrote and call that a fact.
    /// </remarks>
    public static int ConfiguredOutputBufferMs
        => s_library == IntPtr.Zero ? -1 : NativeMethods.GetConfig((int)Configuration.DeviceBufferLength);
  }

  /// <summary>
  /// The two BASS calls the wrapper does not wrap.
  /// </summary>
  /// <remarks>
  /// A type of their own because that is what the analyser asks for (CA1060), and because "the wrapper has no
  /// BASS_SetConfig" is the kind of fact that belongs where a person can read it once and stop wondering.
  /// </remarks>
  internal static class NativeMethods
  {
    [DllImport("bass", EntryPoint = "BASS_SetConfig")]
    internal static extern bool SetConfig(int option, int value);

    [DllImport("bass", EntryPoint = "BASS_GetConfig")]
    internal static extern int GetConfig(int option);
  }
}
