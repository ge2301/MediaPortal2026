/*
 * MediaPortal 2 - Avalonia front end
 *
 * This code distributed under the GPLv3. The source of the project is
 * located at https://github.com/Ge2301/multiplatform
 */

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using MediaPortal.Frontend.Theming;

namespace Tests.Frontend
{
  /// <summary>
  /// A run of these tests speaks for the front end, not for the machine it happens to be typed on - so the one
  /// settings file the application reads before a single test has said anything is pointed at an empty file of
  /// the run's own.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Every fixture that saves an answer names its own file or its own <c>MP2_SKIN_SETTINGS_DIR</c>, and that is
  /// how the suite never touches what a real viewer saved. What is left are the reads the front end and the
  /// screens do <em>without being asked</em>, and there are four of them: the client settings, the screens'
  /// memories, the skin's own saved answers - which sit next to the theme on a development machine - and the
  /// folder the worn face's loose pictures are read from. All four are pointed at the run's own ground. The application's own start is the one reader outside
  /// that rule: <c>App.OnFrameworkInitializationCompleted</c> asks the client settings who the house watches as,
  /// because the name owns the skin's colours and pictures and has to be standing before the palette goes on.
  /// On a development machine that file holds a name - and a name redirects the skin settings to the file of
  /// that person, <c>skin-nereus-gerrit.json</c> instead of the <c>skin-nereus.json</c> the test next door just
  /// wrote - so a screen built afterwards reads a stranger's answers and the test complains about the screen.
  /// </para>
  /// <para>
  /// Said once for the whole run rather than per fixture, because the read happens while the application object
  /// is made, which is before any fixture of the test's own has had its word. A caller that isolated the file
  /// itself - a gate, or a suite run against a scratch machine - is left alone.
  /// </para>
  /// </remarks>
  [SetUpFixture]
  public sealed class IsolatedMachineSettings
  {
    private const string CLIENT_FILE = "MP2_CLIENT_FILE";
    private const string SCREENS_FILE = "MP2_SCREEN_VIEWS_FILE";
    private const string SKIN_SETTINGS_DIR = "MP2_SKIN_SETTINGS_DIR";

    private string _previous = "";
    private string _previous_screens = "";
    private string _previous_skin_settings = "";
    private string? _skinsFolderBefore = "";
    private string? _own;
    private string? _own_screens;
    private string? _own_skin_settings;

    [OneTimeSetUp]
    public void TheClientSettingsOfTheMachineAreSteppedAround()
    {
      _previous = Environment.GetEnvironmentVariable(CLIENT_FILE) ?? "";
      if (_previous.Length == 0)
      {
        _own = Path.Combine(Path.GetTempPath(),
            "mp2-frontend-tests-client-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable(CLIENT_FILE, _own);
      }

      // The screens' memories are the other file every shelf reads and writes without being asked: a shelf
      // remembers the order it was read in, the plates it stood under and the value it was cut to, and a
      // fixture that names no file of its own would otherwise leave those answers in what a real viewer on
      // this machine picked - and open its next shelf filtered by the test before it.
      _previous_screens = Environment.GetEnvironmentVariable(SCREENS_FILE) ?? "";
      if (_previous_screens.Length == 0)
      {
        _own_screens = Path.Combine(Path.GetTempPath(),
            "mp2-frontend-tests-screens-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable(SCREENS_FILE, _own_screens);
      }

      // The skin's own answers - which face the house wears, which colour family, which home rows - are the third
      // file the front end reads without being asked, and they live next to the theme unless this variable says
      // otherwise. On a development machine that folder holds what the person sitting here saved: a face, a
      // colour, a set of home rows, all of which a screen reads the moment it is built. A fixture that names its
      // own folder is unaffected, but one that does not would be dressed by whoever last used the television.
      _previous_skin_settings = Environment.GetEnvironmentVariable(SKIN_SETTINGS_DIR) ?? "";
      if (_previous_skin_settings.Length == 0)
      {
        _own_skin_settings = Path.Combine(Path.GetTempPath(),
            "mp2-frontend-tests-skinsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_own_skin_settings);
        Environment.SetEnvironmentVariable(SKIN_SETTINGS_DIR, _own_skin_settings);
      }

      // The skins themselves are not pinned here on purpose: the shipped faces ride inside the front end's
      // assembly, and a fixture either wears those or names a folder - or an asset root - of its own. Pointing
      // the whole run at the repository's folder would take the asset copy away from the tests that read the
      // face out of the assembly, which is half of what the skin engine is for.
    }

    [OneTimeTearDown]
    public void TheClientSettingsOfTheMachineAreHandedBack()
    {
      Environment.SetEnvironmentVariable(CLIENT_FILE, _previous.Length > 0 ? _previous : null);
      Environment.SetEnvironmentVariable(SCREENS_FILE, _previous_screens.Length > 0 ? _previous_screens : null);
      Environment.SetEnvironmentVariable(SKIN_SETTINGS_DIR,
          _previous_skin_settings.Length > 0 ? _previous_skin_settings : null);

      Delete(_own);
      Delete(_own_screens);
      DeleteFolder(_own_skin_settings);
      SkinEngine.SkinsFolder = _skinsFolderBefore;
      _own = null;
      _own_screens = null;
      _own_skin_settings = null;
    }

    private static void DeleteFolder(string? folder)
    {
      if (folder == null || !Directory.Exists(folder))
        return;

      try
      {
        Directory.Delete(folder, true);
      }
      catch (IOException)
      {
        // A scratch folder the system will collect anyway is not worth a complaint.
      }
    }

    private static void Delete(string? file)
    {
      if (file == null)
        return;

      try
      {
        File.Delete(file);
      }
      catch (IOException)
      {
        // A scratch file the system will collect anyway is not worth a complaint.
      }
    }
  }
}
