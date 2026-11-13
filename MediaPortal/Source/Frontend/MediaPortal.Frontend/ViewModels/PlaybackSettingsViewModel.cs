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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaPortal.Frontend.Settings;

namespace MediaPortal.Frontend.ViewModels;

/// <summary>
/// The viewer's answers to the player, in the shape every settings screen here uses: a row per answer, a
/// button whose face shows the current choice or a checkbox for the two-answer switch, each row saving in
/// the same breath it changes and saying so in the status line.
/// </summary>
/// <remarks>
/// Only answers this front end can honour are rows: the reference's DirectShow answers (hardware decoding,
/// DVD paths) have no equivalent behind either engine and are not offered as decoration. The sound card row is
/// the reference's "audio renderer device" question and belongs to the engine that listens to its own output -
/// choosing it while libVLC plays the sound changes nothing, which is what that row says. The language rows are
/// preferences, not commands - they pick a track only when a media labels one with that language, which
/// <see cref="NowPlayingViewModel.ApplyTrackPreferences"/> does when a media's tracks have become known.
///
/// <para>
/// <b>One screen, or the pages the reference had.</b> Asked for no group, this is the whole list it always
/// was. Named a group, it is the page of that group only, which is how the reference filed the same answers:
/// its Players branch stood over <c>Players_General</c>, <c>Players_AudioPlayer</c>, <c>Players_VideoPlayer</c>
/// and <c>Players_Image</c> (one plate each, ServerSettings.Client/plugin.xml), and a household read the film
/// answers without scrolling past a slideshow. Same settings file, same rows, same saving - a page is the list
/// with its attention narrowed, not a second copy of the answers.
/// </para>
/// </remarks>
public partial class PlaybackSettingsViewModel : ObservableObject
{
  /// <summary>The group of answers one page shows: the player of films and music.</summary>
  public const string GROUP_PLAYERS = "players";

  /// <summary>The group of answers one page shows: what this machine does with pictures.</summary>
  public const string GROUP_PICTURES = "pictures";

  /// <summary>The group of answers one page shows: when a title counts as watched, and how a player leaves.</summary>
  public const string GROUP_TITLES = "titles";

  /// <summary>The group of answers one page shows: which engine makes the sound, and which card it plays to.</summary>
  public const string GROUP_AUDIO = "audio";

  private readonly string? _group;
  private readonly PlaybackSettings _settings;
  private readonly string? _settingsPath;

  public PlaybackSettingsViewModel(string? settingsPath = null, string? group = null)
  {
    _settingsPath = settingsPath;
    _group = group;
    _settings = PlaybackSettings.Load(settingsPath, out string? problem);
    Status = problem ?? "";

    // Whether a row of this screen belongs to the page that was asked for. Nobody named a group, so every row
    // stands, which is the screen as it was before it had pages.
    bool Shows(string kind) => _group == null || _group == kind;

    if (Shows(GROUP_PLAYERS))
    {
      Options.Add(new SkinSettingOption("Video engine",
          "Which engine plays the film. Only libVLC plays video in this front end today, so an answer naming "
          + "another engine is answered by libVLC playing it. A new answer is taken the next time the front end "
          + "starts: the window hands its surface to one engine and does not give it back.",
          VideoEngineText, AdvanceVideoEngine, Changed));

      Options.Add(new SkinSettingOption("Instant skip",
          "How far one instant jump reaches - a share of the title, or seconds when those are preferred",
          InstantSkipText, AdvanceInstantSkip, Changed));

      Options.Add(new SkinSettingOption("Instant skip in seconds",
          "Whether the instant jump counts seconds instead of per cent of the title",
          () => _settings.PreferInstantSkipSeconds, v => _settings.PreferInstantSkipSeconds = v, Changed));

      Options.Add(new SkinSettingOption("Volume step", "How much one press of the volume key moves",
          VolumeStepText, AdvanceVolumeStep, Changed));

      Options.Add(new SkinSettingOption("Start volume", "Where the sound begins when this machine starts a media",
          StartVolumeText, AdvanceStartVolume, Changed));

      Options.Add(new SkinSettingOption("Subtitles on", "Whether subtitles may come on by themselves",
          () => _settings.SubtitlesEnabled, v => _settings.SubtitlesEnabled = v, Changed));

      Options.Add(new SkinSettingOption("Audio language", "Which language to pick when a media offers several",
          AudioLanguageText, AdvanceAudioLanguage, Changed));

      Options.Add(new SkinSettingOption("Subtitle language", "Which subtitle language to pick when there are several",
          SubtitleLanguageText, AdvanceSubtitleLanguage, Changed));
    }

    if (Shows(GROUP_AUDIO))
    {
      Options.Add(new SkinSettingOption("Sound engine",
          "Which engine makes the sound. BASS opens the codecs its add-ons carry and reads the bands of its own "
          + "playback; libVLC plays sound and picture through one engine and has a silent second one draw its "
          + "bands. Either answer moves the bars. The tune that is playing is carried to the answer over it, "
          + "where it stands.",
          SoundEngineText, AdvanceSoundEngine, Changed));

      Options.Add(new SkinSettingOption("Sound card",
          "Which output the BASS engine is told to use. The first answer hands the choice back to the engine, "
          + "which takes the card the operating system picked. Asked while a tune plays, the tune is carried to "
          + "the card where it stood. Asked of libVLC this changes nothing.",
          SoundCardText, AdvanceSoundCard, Changed));
    }

    if (Shows(GROUP_PICTURES))
    {
      Options.Add(new SkinSettingOption("Picture shows for", "How long one picture stands during a slideshow",
          SlideshowText, AdvanceSlideshow, Changed));

      Options.Add(new SkinSettingOption("Ken Burns effect",
          "Whether a slideshow slowly zooms and pans the pictures it shows",
          () => _settings.KenBurnsEffect, v => _settings.KenBurnsEffect = v, Changed));
    }

    if (Shows(GROUP_TITLES))
    {
      Options.Add(new SkinSettingOption("Watched at",
          "How much of a title must be played before it counts as watched",
          WatchedMarkText, AdvanceWatchedMark, Changed));

      Options.Add(new SkinSettingOption("Close when finished",
          "Whether the playback screen steps aside by itself when the film ends",
          () => _settings.ClosePlayerWhenFinished, v => _settings.ClosePlayerWhenFinished = v, Changed));
    }
  }

  public ObservableCollection<SkinSettingOption> Options { get; } = new ObservableCollection<SkinSettingOption>();

  /// <summary>
  /// The node the reference filed these answers under, so that the screen is called what its plate is
  /// called: <c>[Settings.General]</c> under <c>/Players</c> for what counts as watched
  /// (Media/plugin.xml:385), <c>[Settings.Players.VideoPlayer]</c> for the video player's engine, skip, volume
  /// and languages (VideoPlayer/plugin.xml:56), <c>[Settings.Players.BASSAudioPlayer]</c> for the sound engine
  /// and its card (BassPlayer/plugin.xml:75) and <c>[Settings.Players.Image]</c> "Image Player" for the
  /// slideshow (ImagePlayer/plugin.xml:46).
  /// </summary>
  public string Heading => _group switch
  {
    GROUP_PLAYERS => "Video Player",
    GROUP_PICTURES => "Image Player",
    GROUP_TITLES => "General",
    GROUP_AUDIO => "Audio Player",
    _ => "Players"
  };

  public string Subtitle => _group switch
  {
    GROUP_PLAYERS =>
        "Which engine plays the film, how far the player skips, how loud it starts, how big its volume steps "
        + "are and which language it reaches for. Saved at once; belongs to this machine.",
    GROUP_PICTURES =>
        "How long one picture stands and whether a slideshow slowly zooms and pans it. Saved at once; belongs "
        + "to this machine.",
    GROUP_TITLES =>
        "When a title counts as watched, and whether the playback screen steps aside when it ends. Saved at "
        + "once; belongs to this machine.",
    GROUP_AUDIO =>
        "Which engine makes the sound and which card it plays to. Saved at once; belongs to this machine.",
    _ =>
        "What the player on this box was told. Saved at once; belongs to this machine."
  };

  [ObservableProperty]
  private string _status;

  /// <summary>The row the highlight stands on; Enter and the remote's OK work it like its control.</summary>
  [ObservableProperty]
  private SkinSettingOption? _selected;

  public void PressSelected()
  {
    Selected?.Press();
  }

  private string InstantSkipText()
  {
    return _settings.PreferInstantSkipSeconds
        ? _settings.InstantSkipSeconds + " seconds"
        : _settings.InstantSkipPercent + " per cent of the title";
  }

  private void AdvanceInstantSkip()
  {
    if (_settings.PreferInstantSkipSeconds)
      _settings.InstantSkipSeconds = Next(PlaybackSettings.INSTANT_SKIP_SECONDS, _settings.InstantSkipSeconds);
    else
      _settings.InstantSkipPercent = Next(PlaybackSettings.INSTANT_SKIP_PERCENTS, _settings.InstantSkipPercent);
  }

  private string VolumeStepText()
  {
    return _settings.VolumeStep + " per press";
  }

  private void AdvanceVolumeStep()
  {
    _settings.VolumeStep = Next(PlaybackSettings.VOLUME_STEPS, _settings.VolumeStep);
  }

  private string StartVolumeText()
  {
    return _settings.InitialVolume + " %";
  }

  private void AdvanceStartVolume()
  {
    // Walks by quarters and wraps: every press lands on a round answer, and the row reaches 0 as easily
    // as it reaches 100.
    int next = _settings.InitialVolume + 25;
    if (next > 100)
      next = 0;
    _settings.InitialVolume = next;
  }

  private string SoundEngineText()
  {
    return EngineLabel(_settings.AudioEngine, Player.PlayerEngines.BassKey);
  }

  private void AdvanceSoundEngine()
  {
    _settings.AudioEngine = NextEngine(_settings.AudioEngine, true);
  }

  private string VideoEngineText()
  {
    return EngineLabel(_settings.VideoEngine, Player.PlayerEngines.LibVlcKey);
  }

  private void AdvanceVideoEngine()
  {
    _settings.VideoEngine = NextEngine(_settings.VideoEngine, false);
  }

  /// <summary>
  /// What an engine row shows: the engine's own name when the machine can build it, and the refusal next to
  /// the name that ends up playing when it cannot. A row reading "mpv" while another engine made the sound
  /// would be showing the file rather than the truth.
  /// </summary>
  private static string EngineLabel(string? stored, string fallbackKey)
  {
    Player.PlayerEngine named = Player.PlayerEngines.Resolve(stored, fallbackKey);
    if (named.Built)
      return named.Title;
    return named.Title + " - " + named.Refusal + ", so "
        + Player.PlayerEngines.Resolve(null, fallbackKey).Title + " plays it";
  }

  /// <summary>
  /// The next engine of a kind, in the register's own order and including the engines that are only a promise:
  /// an answer nobody built is the viewer's to read, and a row that stepped over it would move a choice they
  /// never made. An answer the register does not know at all steps onto the first engine of the kind.
  /// </summary>
  private static string NextEngine(string? current, bool forSound)
  {
    List<Player.PlayerEngine> ofKind = new List<Player.PlayerEngine>();
    foreach (Player.PlayerEngine engine in Player.PlayerEngines.All)
    {
      if (forSound ? engine.MakesSound : engine.MakesPicture)
        ofKind.Add(engine);
    }
    if (ofKind.Count == 0)
      return current ?? "";
    int at = ofKind.FindIndex(delegate (Player.PlayerEngine engine)
    {
      return string.Equals(engine.Key, current, StringComparison.OrdinalIgnoreCase);
    });
    return ofKind[(at + 1) % ofKind.Count].Key;
  }

  private string SoundCardText()
  {
    return string.IsNullOrWhiteSpace(_settings.AudioOutput) ? "The engine decides" : _settings.AudioOutput;
  }

  private void AdvanceSoundCard()
  {
    List<string> cards = SoundCards();
    int at = cards.FindIndex(delegate (string name)
    {
      return string.Equals(name, _settings.AudioOutput, StringComparison.OrdinalIgnoreCase);
    });
    _settings.AudioOutput = cards[(at + 1) % cards.Count];
  }

  /// <summary>
  /// The cards the row walks: the engine's own answer first, then whatever the engine says it can reach. A
  /// machine without BASS - or without its library, which is not redistributable and has to be fetched by
  /// hand - walks the one answer it has and promises nothing more.
  /// </summary>
  private static List<string> SoundCards()
  {
    List<string> cards = new List<string> { "" };
    try
    {
      cards.AddRange(Player.BassPlayer.Outputs());
    }
    catch (DllNotFoundException)
    {
      // No BASS on this box. The engine's own answer is all there is to offer, and the row above explains why.
    }
    return cards;
  }

  private string AudioLanguageText()
  {
    return LanguageLabel(_settings.AudioLanguage);
  }

  private void AdvanceAudioLanguage()
  {
    _settings.AudioLanguage = NextLanguage(_settings.AudioLanguage);
  }

  private string SubtitleLanguageText()
  {
    return LanguageLabel(_settings.SubtitleLanguage);
  }

  private void AdvanceSubtitleLanguage()
  {
    _settings.SubtitleLanguage = NextLanguage(_settings.SubtitleLanguage);
  }

  private string SlideshowText()
  {
    return _settings.SlideshowSeconds + " seconds";
  }

  private void AdvanceSlideshow()
  {
    _settings.SlideshowSeconds = Next(PlaybackSettings.SLIDESHOW_SECONDS, _settings.SlideshowSeconds);
  }

  private string WatchedMarkText()
  {
    return _settings.WatchedMarkPercentage + " %";
  }

  private void AdvanceWatchedMark()
  {
    _settings.WatchedMarkPercentage = Next(PlaybackSettings.WATCHED_MARKS, _settings.WatchedMarkPercentage);
  }

  /// <summary>What every row does after its answer changed: save, say it, let the screens follow.</summary>
  private void Changed(SkinSettingOption option)
  {
    option.Refresh();
    string says = _settings.Save(_settingsPath, out bool saved);
    Status = saved
        ? string.Format("{0} is now {1}. {2}", option.Label, option.ValueText, says)
        : string.Format("{0} is now {1} for this session; saving failed - {2}",
            option.Label, option.ValueText, says);
  }

  private static int Next(int[] answers, int current)
  {
    for (int i = 0; i < answers.Length; i++)
      if (answers[i] == current)
        return answers[(i + 1) % answers.Length];

    return answers[0];
  }

  private static string LanguageLabel(string code)
  {
    foreach ((string known, string label) in PlaybackSettings.TRACK_LANGUAGES)
      if (string.Equals(known, code, StringComparison.OrdinalIgnoreCase))
        return label;

    return code.Length == 0 ? "The media decides" : code;
  }

  private static string NextLanguage(string current)
  {
    for (int i = 0; i < PlaybackSettings.TRACK_LANGUAGES.Length; i++)
      if (string.Equals(PlaybackSettings.TRACK_LANGUAGES[i].Code, current, StringComparison.OrdinalIgnoreCase))
        return PlaybackSettings.TRACK_LANGUAGES[(i + 1) % PlaybackSettings.TRACK_LANGUAGES.Length].Code;

    return PlaybackSettings.TRACK_LANGUAGES[0].Code;
  }
}
