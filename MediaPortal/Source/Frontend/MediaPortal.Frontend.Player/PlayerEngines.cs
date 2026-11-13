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

namespace MediaPortal.Frontend.Player
{
  /// <summary>
  /// What one engine of this build can do, and how to get one.
  /// </summary>
  /// <remarks>
  /// A row of the settings page is a row of these. Keeping the answer in one place is what lets the page offer
  /// an engine that is not in the build at all - mpv is named and refused in the same breath, which is truer to
  /// a viewer than a menu where the word is simply missing.
  /// </remarks>
  public sealed class PlayerEngine
  {
    internal PlayerEngine(string key, string title, bool makesSound, bool makesPicture, bool built,
        string refusal, Func<PlayerOptions?, PlayerBuild>? build)
    {
      Key = key;
      Title = title;
      MakesSound = makesSound;
      MakesPicture = makesPicture;
      Built = built;
      Refusal = refusal;
      _build = build;
    }

    readonly Func<PlayerOptions?, PlayerBuild>? _build;

    /// <summary>What a setting stores. Lower case, because it is written into a file a person may edit.</summary>
    public string Key { get; }

    /// <summary>What a screen shows, spelled the way its makers spell it.</summary>
    public string Title { get; }

    /// <summary>Whether choosing this engine can be heard.</summary>
    public bool MakesSound { get; }

    /// <summary>Whether choosing this engine can be seen.</summary>
    public bool MakesPicture { get; }

    /// <summary>Whether this build can actually run it. A name in a menu is not an engine.</summary>
    public bool Built { get; }

    /// <summary>Why not; empty when it can.</summary>
    public string Refusal { get; }

    /// <summary>
    /// Whether this engine is the one that made the given player. Asked by a screen before it rebuilds an engine
    /// that is already at work, because building the engine that is playing means opening its sound card a second
    /// time over the tune it holds. An engine this build cannot name answers "no" for a player it did not build,
    /// which is the honest answer rather than a guess from a name.
    /// </summary>
    public bool Describes(IVideoPlayer? player)
    {
      if (player == null)
        return false;
      return Key switch
      {
        PlayerEngines.LibVlcKey => player is LibVlcPlayer,
        PlayerEngines.BassKey => player is BassPlayer,
        _ => false
      };
    }

    /// <summary>
    /// Make a player of this engine, or say why there is none. Asking an engine that is not built answers the
    /// sentence it already carries rather than a second one, so a refusal read twice is the same refusal.
    /// </summary>
    public PlayerBuild Build(PlayerOptions? options = null)
    {
      if (_build == null)
        return PlayerBuild.Refused(Refusal);
      return _build(options);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
      return Title + (Built ? "" : " (not in this build)");
    }
  }

  /// <summary>
  /// What asking an engine for a player produced: the player, or the reason there is none.
  /// </summary>
  public readonly struct PlayerBuild
  {
    PlayerBuild(IVideoPlayer? player, string engineDescription, string reason)
    {
      Player = player;
      EngineDescription = engineDescription;
      Reason = reason;
    }

    /// <summary>The player, or null.</summary>
    public IVideoPlayer? Player { get; }

    /// <summary>What the engine calls itself, version included; empty when there is no player.</summary>
    public string EngineDescription { get; }

    /// <summary>Why there is none; empty when there is one.</summary>
    public string Reason { get; }

    /// <summary>True when there is a player to use.</summary>
    public bool Succeeded => Player != null;

    /// <summary>A player and what it said about itself.</summary>
    public static PlayerBuild Made(IVideoPlayer player, string engineDescription)
    {
      if (player == null)
        throw new ArgumentNullException(nameof(player));
      return new PlayerBuild(player, engineDescription, "");
    }

    /// <summary>No player, and the sentence that explains it.</summary>
    public static PlayerBuild Refused(string reason)
    {
      return new PlayerBuild(null, "", reason ?? "");
    }
  }

  /// <summary>
  /// The engines this build knows about.
  /// </summary>
  /// <remarks>
  /// <para>
  /// A list rather than an enumeration because the settings page has to show what cannot be chosen as well as
  /// what can, and because adding the third engine is meant to be one row here plus its class - nothing in a
  /// screen is supposed to change when it arrives.
  /// </para>
  /// <para>
  /// Nothing in this list is looked at until somebody asks: <see cref="All"/> hands out descriptions, and only
  /// <see cref="PlayerEngine.Build"/> touches a library. A settings page can therefore be drawn on a machine
  /// with no BASS and no libVLC at all.
  /// </para>
  /// </remarks>
  public static class PlayerEngines
  {
    /// <summary>The key of the engine that has always been here.</summary>
    public const string LibVlcKey = "libvlc";

    /// <summary>The key of the engine that listens to itself.</summary>
    public const string BassKey = "bass";

    /// <summary>The key of the engine that is planned and not written.</summary>
    public const string MpvKey = "mpv";

    /// <summary>Every engine a setting may name, in the order a settings page lists them.</summary>
    public static IReadOnlyList<PlayerEngine> All { get; } = new[]
    {
      new PlayerEngine(LibVlcKey, "libVLC", true, true, true, "", delegate(PlayerOptions? options)
      {
        if (!LibVlcPlayer.TryCreate(out LibVlcPlayer? player, out string reason, options))
          return PlayerBuild.Refused(reason);
        return PlayerBuild.Made(player!, player!.EngineDescription);
      }),

      new PlayerEngine(BassKey, "BASS", true, false, true, "", delegate(PlayerOptions? options)
      {
        if (!BassPlayer.TryCreate(out BassPlayer? player, out string reason, options))
          return PlayerBuild.Refused(reason);
        return PlayerBuild.Made(player!, player!.EngineDescription);
      }),

      new PlayerEngine(MpvKey, "mpv", true, true, false,
          "mpv is planned as the third engine and is not in this build. libVLC plays video until then.",
          null)
    };

    /// <summary>
    /// The engine a key names, or null for a key nothing answers to. Never a quiet fallback: a setting that
    /// names an unknown engine is a setting somebody typed, and the sentence they deserve is "there is no such
    /// engine" rather than a different engine starting silently.
    /// </summary>
    public static PlayerEngine? Find(string? key)
    {
      if (string.IsNullOrWhiteSpace(key))
        return null;
      foreach (PlayerEngine engine in All)
      {
        if (string.Equals(engine.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
          return engine;
      }
      return null;
    }

    /// <summary>
    /// The engine to use for a key, with a fallback when it is not there: the engine of the sentence a caller
    /// can show. The caller decides whether to show it - which is the difference between falling back and
    /// pretending.
    /// </summary>
    public static PlayerEngine Resolve(string? key, string? fallbackKey = null)
    {
      PlayerEngine? asked = Find(key);
      if (asked != null)
        return asked;
      PlayerEngine? fallback = Find(fallbackKey);
      return fallback ?? Find(LibVlcKey)!;
    }
  }
}