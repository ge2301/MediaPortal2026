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
using System.Globalization;
using System.IO;
using System.Threading;
using ManagedBass;

namespace MediaPortal.Frontend.Player
{
  /// <summary>
  /// Playing sound through BASS.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is the second engine on the same seam as <see cref="LibVlcPlayer"/>, and it is here for one reason:
  /// BASS listens to itself. The spectrum bars the reference draws need the frequency content of what is
  /// audible right now, and libVLC only offers that at the price of taking over the output entirely (see
  /// <see cref="IAudioSpectrumSource"/>). Everything a screen can ask, this answers too - position, length,
  /// pause, volume, the end of a track - so that a screen never has to know which of the two is running.
  /// </para>
  /// <para>
  /// It makes sound and no picture. Both <c>AttachTo</c> overloads answer false with a sentence, which is the
  /// honest answer rather than a limitation: a picture from BASS would have to be decoded here, and the video
  /// half of the seam already has an engine that does nothing else. A viewer who chooses BASS for sound keeps
  /// the video engine for the screen.
  /// </para>
  /// <para>
  /// Threading follows what the seam promises: BASS calls its own threads, the <see cref="EndOfMedia"/> event
  /// comes from one of them, and the position ticks come from a timer of this instance. One lock guards the
  /// handles, and no event is raised inside it, because a screen answers events by calling back in.
  /// </para>
  /// <para>
  /// The numbers in this class came from measuring, not from documentation; the measurements are in the plan
  /// file next to the spike that made them. The three that shaped the design: a pause lets BASS's own position
  /// creep on by about twelve milliseconds, so a paused player reports the position it froze rather than the
  /// one BASS reports; the end of a track is announced about a hundred and fifty milliseconds after the samples
  /// run out, which is the buffer and not a fault; and the frequency answer is taken from the playing channel
  /// itself, which needs no second stream and no mixer.
  /// </para>
  /// </remarks>
  public sealed class BassPlayer : IVideoPlayer, IAudioSpectrumSource
  {
    /// <summary>
    /// The rate the output device is opened at. Each stream keeps its own rate and BASS resamples on the way
    /// out, so this number never touches the material being played - it only decides what the device asks for.
    /// </summary>
    const int DeviceRate = 44100;

    /// <summary>
    /// How many floats a <c>BASS_DATA_FFT4096</c> answer holds. Measured: the call answers 16384 bytes, which is
    /// 4096 floats and not the 2048 a person reading the flag's name would expect. A smaller buffer is overrun.
    /// </summary>
    const int FftFloats = 4096;

    /// <summary>How often the position is looked at, in ms. libVLC's player moves at about this pace too.</summary>
    const int TickIntervalMs = 200;

    /// <summary>
    /// The URL schemes BASS has no way of opening. Said here as a sentence instead of arriving as an empty
    /// failure, because "the decoder refused" is not what happened - nobody can decode it anywhere.
    /// </summary>
    static readonly string[] UnreachableSchemes = { "rtsp", "rtp", "udp", "smb", "sftp", "ftp" };

    static readonly object s_deviceGate = new object();
    static int s_openedDevice = int.MinValue;
    static string s_deviceSaid = "the output device has not been opened yet";

    readonly object _gate = new object();
    readonly SyncProcedure _endSync;
    readonly float[] _fft = new float[FftFloats];
    readonly Timer _ticker;
    PlayerState _state = PlayerState.Idle;
    MediaLocation? _current;
    string? _lastFailure;
    int _stream;
    int _sync;
    bool _stopAsked;
    bool _disposed;
    long _pausedAtMs = -1;
    long _lastPositionMs = -1;
    long _lastLengthMs = -1;
    int _volume = 100;
    bool _muted;
    int _volumeBeforeMute = 100;

    /// <summary>
    /// What BASS calls the output a viewer chose, with the fallback it had to take. Kept so that a settings row
    /// can show the difference between "chosen" and "settled for".
    /// </summary>
    string _outputSaid = "";

    BassPlayer(string outputSaid)
    {
      _outputSaid = outputSaid;
      // The delegate has to live as long as the sync does; a local one would be collected under BASS.
      _endSync = OnEnd;
      _ticker = new Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// What this engine is, for a settings row and for the console: the library's own version.
    /// </summary>
    public string EngineDescription { get; private set; } = "BASS";

    /// <summary>
    /// The output device this instance plays through, in the engine's words.
    /// </summary>
    public string OutputDescription => _outputSaid;

    /// <summary>
    /// The codecs BASS answered for, as file types. For a settings row that has to say what a choice can play.
    /// </summary>
    public static IReadOnlyList<string> KnownFileTypes()
    {
      return BassPlugins.Formats();
    }

    /// <summary>
    /// Build a player on the machine's BASS, or say in one sentence why there is none.
    /// </summary>
    /// <remarks>
    /// The shape of this - static, answering a reason rather than throwing - is <see cref="LibVlcPlayer.TryCreate"/>'s,
    /// on purpose: a caller that offers two engines asks both the same question and compares two answers.
    /// </remarks>
    /// <param name="player">The player, or null.</param>
    /// <param name="reason">Why there is none; empty when there is one.</param>
    /// <param name="options">Where to look for the library and which output to use.</param>
    public static bool TryCreate(out BassPlayer? player, out string reason, PlayerOptions? options = null)
    {
      player = null;
      reason = "";
      PlayerOptions asked = options ?? new PlayerOptions();

      if (asked.Headless)
      {
        // BASS has no silent device of its own - measured: asking for one is refused - and a headless run is a
        // test or a service, which asked for sound by nobody. Playing through the loudspeaker anyway would be
        // the worse lie, so the answer here is a sentence.
        reason = "BASS makes sound, and a headless run asked for none. Choose libVLC for a run without speakers.";
        return false;
      }

      if (!NativeBass.TryFind(asked.LibraryDirectory, out string? library, out string found))
      {
        reason = found;
        return false;
      }
      if (!NativeBass.EnsureLoaded(library!, out string loaded))
      {
        reason = loaded;
        return false;
      }

      // The add-ons have to be in before anything is asked to decode, and they live next to the library.
      BassPlugins.Load(Path.GetDirectoryName(Path.GetFullPath(library!)));

      int device = PickDevice(asked.AudioOutputId, out string picked);
      if (!TryOpenDevice(device, out string opened))
      {
        reason = opened;
        return false;
      }

      BassPlayer built = new BassPlayer(picked)
      {
        EngineDescription = "BASS " + Bass.Version.ToString(3)
      };
      player = built;
      return true;
    }
    /// <summary>
    /// Which device number to hand BASS for the name a viewer chose; -1 is BASS's own "whatever the machine
    /// decided". A name nothing answers to falls back to that default and says so, rather than failing a viewer
    /// whose sound card was unplugged - the sound they used to get is still the sound to give them.
    /// </summary>
    static int PickDevice(string? wanted, out string said)
    {
      if (string.IsNullOrWhiteSpace(wanted))
      {
        said = "the system's own output";
        return -1;
      }

      // Device nought is BASS's own "no sound" driver on Windows and a loopback on others; neither is something
      // a viewer chose when they named their speakers.
      for (int number = 1; number < Bass.DeviceCount; number++)
      {
        if (!Bass.GetDeviceInfo(number, out DeviceInfo info) || info.IsLoopback)
          continue;
        if (!string.Equals(info.Name, wanted, StringComparison.OrdinalIgnoreCase))
          continue;

        said = info.Name;
        return number;
      }

      said = "the system's own output (nothing answered to '" + wanted + "')";
      return -1;
    }

    /// <summary>
    /// Open one output device for the whole process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BASS keeps the device, the add-ons and the configuration in the process rather than in a handle, so a
    /// second player asking for the same device must not open it twice, and a player asking for another one takes
    /// it away from everybody. Both are handled here rather than in a caller, because no caller can see the
    /// other callers.
    /// </para>
    /// <para>
    /// The buffer is set before the device is opened because that is the only moment BASS listens: measured on
    /// this machine, asking afterwards left the reported latency where it was, while asking before moved it from
    /// twenty five milliseconds to a hundred and forty six.
    /// </para>
    /// </remarks>
    static bool TryOpenDevice(int device, out string reason)
    {
      lock (s_deviceGate)
      {
        if (s_openedDevice == device && IsDeviceUsable(device))
        {
          reason = s_deviceSaid;
          return true;
        }

        NativeBass.Configure(NativeBass.OutputBufferMs, out string configured);

        if (s_openedDevice != int.MinValue)
        {
          // Handing the old device back is what makes a switch of output work at all; BASS keeps no state that
          // a person would want to survive it.
          Bass.Free();
          s_openedDevice = int.MinValue;
        }

        if (!Bass.Init(device, DeviceRate, DeviceInitFlags.Default))
        {
          reason = BassFailure.Describe("the output could not be opened")
              + " (library: " + (NativeBass.LoadedFrom ?? "not loaded") + ")";
          s_deviceSaid = reason;
          return false;
        }

        s_openedDevice = device;
        s_deviceSaid = configured;
        reason = configured;
        return true;
      }
    }

    /// <summary>
    /// True when the device BASS was told to use is still the one it has. A Windows user unplugging their
    /// speakers leaves BASS holding a device that answers nothing, and the only way to notice is to ask.
    /// </summary>
    static bool IsDeviceUsable(int device)
    {
      if (device < 0)
      {
        // The default device has no number to check. BASS knowing any initialised device is as close as it gets.
        return Bass.CurrentDevice >= 0;
      }
      return Bass.GetDeviceInfo(device, out DeviceInfo info) && info.IsInitialized;
    }

    /// <summary>
    /// Give the output device back. Called when the application goes away, not when a player does: a second
    /// player that has to open the device again after every navigation would be heard as a click.
    /// </summary>
    public static void Shutdown()
    {
      lock (s_deviceGate)
      {
        if (s_openedDevice == int.MinValue)
          return;
        s_openedDevice = int.MinValue;
        Bass.Free();
      }
    }

    /// <summary>
    /// The outputs a viewer can choose, as the engine names them, in the order BASS lists them.
    /// </summary>
    /// <remarks>
    /// Asking this opens nothing. A settings page that has never played anything can still list the sound cards,
    /// which is what a person holding a speaker cable wants.
    /// </remarks>
    public static IReadOnlyList<string> Outputs()
    {
      List<string> names = new List<string>();
      for (int number = 1; number < Bass.DeviceCount; number++)
      {
        if (!Bass.GetDeviceInfo(number, out DeviceInfo info) || info.IsLoopback)
          continue;
        if (!string.IsNullOrEmpty(info.Name))
          names.Add(info.Name!);
      }
      return names;
    }
    /// <summary>The state as last known.</summary>
    public PlayerState State
    {
      get { lock (_gate) return _state; }
    }

    /// <summary>What is loaded. Kept after a stop, because "what was playing" is what a screen shows then.</summary>
    public MediaLocation? Current
    {
      get { lock (_gate) return _current; }
    }

    /// <summary>The last thing that went wrong, in a sentence.</summary>
    public string? LastFailure
    {
      get { lock (_gate) return _lastFailure; }
    }

    /// <inheritdoc/>
    public event EventHandler<PlayerStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<PlayerFailedEventArgs>? Failed;

    /// <inheritdoc/>
    public event EventHandler? EndOfMedia;

    /// <inheritdoc/>
    public event EventHandler<PlaybackPositionEventArgs>? PositionChanged;

    /// <summary>
    /// Load a media and start it.
    /// </summary>
    /// <remarks>
    /// BASS decodes the header here, which for a file is quick and for a URL is a network round trip - up to the
    /// timeout BASS was configured with. That is the engine's timing and the seam allows it: the call answers
    /// whether the material can be played at all, which is the one thing a screen cannot learn from an event.
    /// </remarks>
    public bool Open(MediaLocation location)
    {
      lock (_gate)
      {
        if (_disposed)
          return Fail("this player has been disposed.");
        _lastFailure = null;
        _stopAsked = false;
        _pausedAtMs = -1;
        _lastPositionMs = -1;
        _lastLengthMs = -1;
      }

      string? refusal = RefusalOf(location);
      if (refusal != null)
      {
        Fail(refusal);
        RaiseFailure(refusal);
        SetState(PlayerState.Failed);
        return false;
      }

      StopTicker();
      ReleaseStream();

      // Float and software mixing is the combination the spike measured: it answers the frequency question on
      // the playing channel, which is the reason this engine is in the tree at all.
      const BassFlags Flags = BassFlags.Float | BassFlags.SoftwareMixing;
      int handle;
      try
      {
        handle = location.IsFile
            ? Bass.CreateStream(location.EngineString, 0, 0, Flags)
            : Bass.CreateStream(location.EngineString, 0, Flags, null, IntPtr.Zero);
      }
      catch (Exception ex)
      {
        // The wrapper throws only for its own trouble (a marshal failure, a missing entry point), and a listener
        // whose music stopped does not need to know which of those it was - but the sentence has to be here
        // rather than as a stack trace, because a failure that throws loses the screen's reaction with it.
        return FailWithFailure(location.Describe() + " could not be handed to BASS: " + ex.Message);
      }

      if (handle == 0)
        return FailWithFailure(BassFailure.Describe(location.Describe() + " could not be decoded"));

      lock (_gate)
      {
        _stream = handle;
        _current = location;
      }

      // The end has to be listened for rather than polled: the position runs out before the sound does, and a
      // screen that watched the position would advance to the next track over the last syllable.
      _sync = Bass.ChannelSetSync(handle, SyncFlags.End, 0, _endSync, IntPtr.Zero);

      ApplyVolume(handle);
      if (!Bass.ChannelPlay(handle, false))
        return FailWithFailure(BassFailure.Describe(location.Describe() + " could not be started"));

      SetState(PlayerState.Playing);
      StartTicker();
      AnnouncePosition(true);
      return true;
    }

    /// <summary>
    /// A sentence about a media this engine will not attempt, or null to try it.
    /// </summary>
    static string? RefusalOf(MediaLocation location)
    {
      if (location.IsFile)
        return null;

      string url = location.EngineString;
      int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
      string scheme = schemeEnd < 0 ? "" : url.Substring(0, schemeEnd).ToLowerInvariant();
      foreach (string unsupported in UnreachableSchemes)
      {
        if (!scheme.Equals(unsupported, StringComparison.Ordinal))
          continue;
        return "'" + scheme + "' is not something BASS can open. Choose libVLC for that stream.";
      }
      return null;
    }

    /// <summary>
    /// Resume, or start the same media again from its beginning when it had run out or been stopped.
    /// </summary>
    public bool Play()
    {
      MediaLocation? again = null;
      bool fromBeginning;
      lock (_gate)
      {
        if (_disposed)
          return Fail("this player has been disposed.");
        if (_stream == 0)
        {
          if (_current == null)
            return Fail("nothing has been opened on this player.");
          again = _current;
          fromBeginning = false;
        }
        else
        {
          fromBeginning = _state == PlayerState.Ended || _state == PlayerState.Stopped;
        }
      }

      if (again != null)
        return Open(again.Value);

      int handle;
      lock (_gate)
        handle = _stream;

      // Letting go of the frozen position before asking BASS, so that the first tick after this reports where
      // the sound really is and not where the pause left it.
      lock (_gate)
        _pausedAtMs = -1;

      if (!Bass.ChannelPlay(handle, fromBeginning))
        return FailWithFailure(BassFailure.Describe("the track could not be started again"));

      SetState(PlayerState.Playing);
      StartTicker();
      return true;
    }

    /// <summary>
    /// Pause, and hold the position where the listener heard it.
    /// </summary>
    public bool Pause()
    {
      int handle;
      lock (_gate)
      {
        if (_disposed)
          return Fail("this player has been disposed.");
        if (_stream == 0)
          return Fail("there is nothing loaded to pause.");
        if (_state != PlayerState.Playing)
          return Fail("the track is " + Describe(State) + "; pausing it would change nothing.");
        handle = _stream;
      }

      long where = ReadPositionMs(handle);
      if (!Bass.ChannelPause(handle))
        return Fail(BassFailure.Describe("the pause did not happen"));

      lock (_gate)
        _pausedAtMs = where;
      StopTicker();
      SetState(PlayerState.Paused);
      AnnouncePosition(true);
      return true;
    }

    /// <summary>
    /// Stop and let go of the media. The instance stays usable.
    /// </summary>
    public bool Stop()
    {
      int handle;
      lock (_gate)
      {
        if (_disposed)
          return Fail("this player has been disposed.");
        _stopAsked = true;
        handle = _stream;
      }

      StopTicker();
      if (handle != 0)
        Bass.ChannelStop(handle);
      ReleaseStream();
      lock (_gate)
      {
        _pausedAtMs = -1;
        _stopAsked = false;
      }
      SetState(PlayerState.Stopped);
      return true;
    }
    /// <summary>
    /// How long the media is. Zero for a live stream, which is what BASS answers with no length to give.
    /// </summary>
    public long LengthInMs
    {
      get
      {
        int handle;
        lock (_gate)
          handle = _stream;
        return handle == 0 ? 0 : ReadLengthMs(handle);
      }
    }

    /// <summary>
    /// Where the media is, and the way to move it.
    /// </summary>
    /// <remarks>
    /// While paused this is the position the pause happened at, not what BASS reports: measured on this machine,
    /// a paused track's own position creeps on by about twelve milliseconds, which is the mixer rather than the
    /// listener's ear. A progress bar that moved while nothing moved would be believed as a fault.
    /// </remarks>
    public long TimeInMs
    {
      get
      {
        long frozen;
        int handle;
        lock (_gate)
        {
          frozen = _pausedAtMs;
          handle = _stream;
        }
        if (frozen >= 0)
          return frozen;
        return handle == 0 ? 0 : ReadPositionMs(handle);
      }
      set
      {
        int handle;
        lock (_gate)
        {
          if (_disposed)
            return;
          handle = _stream;
        }
        if (handle == 0)
          return;

        long asked = value < 0 ? 0 : value;
        long bytes = Bass.ChannelSeconds2Bytes(handle, asked / 1000.0);
        if (bytes < 0 || !Bass.ChannelSetPosition(handle, bytes, PositionFlags.Bytes))
        {
          Fail(BassFailure.Describe("the jump to " + asked.ToString(CultureInfo.InvariantCulture)
              + " ms could not be made"));
          return;
        }

        // A paused player shows its own number, so after a jump while paused that number has to be moved too -
        // otherwise the bar walks back to where the pause caught the sound.
        lock (_gate)
        {
          if (_pausedAtMs >= 0)
            _pausedAtMs = asked;
        }
        AnnouncePosition(true);
      }
    }

    /// <summary>
    /// Volume nought to a hundred, clamped. Kept on the instance as well as in the engine, because a player that
    /// has nothing loaded still has to remember what it was told.
    /// </summary>
    public int Volume
    {
      get { lock (_gate) return _volume; }
      set
      {
        int clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
        int handle;
        lock (_gate)
        {
          _volume = clamped;
          // Unmuting by turning up is what a volume knob does on every amplifier, so it is what this does too.
          if (!_muted)
            _volumeBeforeMute = clamped;
          handle = _stream;
        }
        if (handle != 0)
          ApplyVolume(handle);
      }
    }

    /// <summary>
    /// Mute, without touching the volume a viewer set.
    /// </summary>
    public bool Muted
    {
      get { lock (_gate) return _muted; }
      set
      {
        int handle;
        lock (_gate)
        {
          if (_muted == value)
            return;
          _muted = value;
          if (value)
            _volumeBeforeMute = _volume;
          else
            _volume = _volumeBeforeMute;
          handle = _stream;
        }
        if (handle != 0)
          ApplyVolume(handle);
      }
    }

    /// <summary>
    /// Nothing to choose: a BASS stream is one track of sound, and a file carrying several sound tracks is rare
    /// enough that pretending otherwise would be a menu full of the same word.
    /// </summary>
    public IReadOnlyList<PlaybackTrack> Tracks(TrackKind kind)
    {
      return Array.Empty<PlaybackTrack>();
    }

    /// <summary>
    /// Refused with the reason, because a screen that is offered a track list it cannot fill asks once.
    /// </summary>
    public bool SelectTrack(PlaybackTrack track)
    {
      return Fail("BASS plays one sound track per media, so there is nothing to choose here.");
    }

    /// <summary>The sound track is always the one that is playing, and it has no id.</summary>
    public int CurrentTrackId(TrackKind kind)
    {
      return -1;
    }

    /// <summary>Sound has no chapters.</summary>
    public IReadOnlyList<PlaybackChapter> Chapters => Array.Empty<PlaybackChapter>();

    /// <summary>Nothing to say about a picture this engine does not make.</summary>
    public PlaybackQuality? Quality => null;

    /// <summary>Frames are not counted here; see <see cref="PlayerStatistics.None"/>.</summary>
    public PlayerStatistics Statistics => PlayerStatistics.None;

    /// <summary>
    /// Refused: BASS makes sound, and a window is for pictures.
    /// </summary>
    public bool AttachTo(nint videoTarget)
    {
      return Fail("BASS makes sound and no picture, so a window is nothing it can use. "
          + "The picture stays with the video engine.");
    }

    /// <summary>
    /// Refused for the same reason as the window: a frame sink wants pictures.
    /// </summary>
    public bool AttachTo(VideoFrameSink frameSink)
    {
      return Fail("BASS makes sound and no picture, so a frame sink is nothing it can fill. "
          + "The picture stays with the video engine.");
    }

    /// <summary>
    /// The rate of what is playing, which is what turns the frequency answers into bands. The channel's own rate
    /// and not the device's: measured with tones, the bands of the answer lie where the channel rate puts them,
    /// so reading the device's forty eight thousand instead would slide every bar to the right.
    /// </summary>
    public int SampleRate
    {
      get
      {
        int handle;
        lock (_gate)
          handle = _stream;
        if (handle == 0)
          return 0;
        double rate = Bass.ChannelGetAttribute(handle, ChannelAttribute.Frequency);
        return rate > 0 ? (int)rate : 0;
      }
    }

    /// <summary>
    /// The frequency content of what is audible now, as magnitudes for a screen to turn into heights.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is read from the playing channel itself. The reference keeps a second stream alongside the
    /// playing one for this (its own comment on the subject is in the plan file), which BASS does not need here:
    /// measured with five single tones, the playing channel's own answer puts every tone in the band its
    /// frequency names.
    /// </para>
    /// <para>
    /// One reader only. BASS measures into a buffer it hands out once, so a second control asking the same
    /// question would take the answer from under the first. One screen drawing one picture is the case, and a
    /// second one would need a copy made here rather than two readers.
    /// </para>
    /// </remarks>
    public bool GetFftData(float[] bands)
    {
      if (bands == null || bands.Length == 0)
        return false;

      int handle;
      lock (_gate)
        handle = _stream;
      if (handle == 0 || Bass.ChannelIsActive(handle) != PlaybackState.Playing)
        return false;

      // The length argument is the flag, and the answer is bytes: 16384 of them, which is the whole buffer.
      int bytes = Bass.ChannelGetData(handle, _fft, (int)DataFlags.FFT4096);
      if (bytes <= 0)
        return false;

      int copied = Math.Min(bands.Length, FftFloats);
      Array.Copy(_fft, bands, copied);
      return true;
    }
    /// <summary>
    /// Where the sound is, in ms, as BASS itself reports it.
    /// </summary>
    static long ReadPositionMs(int handle)
    {
      long bytes = Bass.ChannelGetPosition(handle, PositionFlags.Bytes);
      if (bytes < 0)
        return 0;
      double seconds = Bass.ChannelBytes2Seconds(handle, bytes);
      return seconds < 0 ? 0 : (long)(seconds * 1000.0);
    }

    /// <summary>
    /// How long the media is, in ms. Zero when BASS has no length to give, which is the answer for a live stream
    /// and for the moment before a network stream has read far enough to know.
    /// </summary>
    static long ReadLengthMs(int handle)
    {
      long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
      if (bytes < 0)
        return 0;
      double seconds = Bass.ChannelBytes2Seconds(handle, bytes);
      return seconds < 0 ? 0 : (long)(seconds * 1000.0);
    }

    /// <summary>
    /// Hand the engine the volume the instance remembers.
    /// </summary>
    void ApplyVolume(int handle)
    {
      int asked;
      lock (_gate)
        asked = _muted ? 0 : _volume;
      // The engine's scale is nought to one and linear, the same hundred in another dress; no loudness curve is
      // invented here because the seam's number is already what a viewer reads on the screen.
      Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, asked / 100f);
    }

    /// <summary>
    /// Tell the screens where the media is. <paramref name="force"/> is for the moments a viewer caused - a
    /// pause, a jump, a new media - where the number has to arrive even when the last one looked the same.
    /// </summary>
    void AnnouncePosition(bool force)
    {
      int handle;
      long frozen;
      lock (_gate)
      {
        handle = _stream;
        frozen = _pausedAtMs;
      }
      if (handle == 0)
        return;

      long time = frozen >= 0 ? frozen : ReadPositionMs(handle);
      long length = ReadLengthMs(handle);
      lock (_gate)
      {
        if (!force && time == _lastPositionMs && length == _lastLengthMs)
          return;
        _lastPositionMs = time;
        _lastLengthMs = length;
      }

      PositionChanged?.Invoke(this, new PlaybackPositionEventArgs(time, length));
    }

    /// <summary>The timer's beat: report the position while the sound moves.</summary>
    void Tick(object? _)
    {
      if (_disposed)
        return;
      lock (_gate)
      {
        if (_state != PlayerState.Playing || _stream == 0)
          return;
      }
      AnnouncePosition(false);
    }

    /// <summary>
    /// BASS says the samples ran out.
    /// </summary>
    /// <remarks>
    /// Called on a thread of BASS's own, about a buffer after the last sample left for the sound card, which on
    /// this machine is a hundred and fifty milliseconds - the buffer the player asked for, and not a fault to fix.
    /// A stop that the viewer asked for is reported as the stop and not as an end, because a screen that starts
    /// the next track over a stopped one is a complaint waiting to be made.
    /// </remarks>
    void OnEnd(int handle, int channel, int data, IntPtr user)
    {
      PlayerState previous;
      lock (_gate)
      {
        if (_disposed || _stopAsked || _state == PlayerState.Ended)
          return;
        previous = _state;
        _state = PlayerState.Ended;
        _pausedAtMs = -1;
      }

      StopTicker();
      RaiseStateChanged(previous, PlayerState.Ended);
      EndOfMedia?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Move to a state and tell the screens, outside the lock: a screen answers a state change by calling back.
    /// </summary>
    void SetState(PlayerState next)
    {
      PlayerState previous;
      lock (_gate)
      {
        previous = _state;
        if (previous == next)
          return;
        _state = next;
      }
      RaiseStateChanged(previous, next);
    }

    void RaiseStateChanged(PlayerState previous, PlayerState next)
    {
      StateChanged?.Invoke(this, new PlayerStateChangedEventArgs(previous, next));
    }

    void RaiseFailure(string reason)
    {
      Failed?.Invoke(this, new PlayerFailedEventArgs(reason));
    }

    /// <summary>
    /// Say what went wrong, for the screen to show and for <see cref="LastFailure"/> to keep. The same shape as
    /// <see cref="LibVlcPlayer"/>'s: a guard that reads as one statement.
    /// </summary>
    bool Fail(string reason)
    {
      lock (_gate)
        _lastFailure = reason;
      return false;
    }

    /// <summary>
    /// A media this engine cannot play: remembered, announced, and the state moved to broken.
    /// </summary>
    bool FailWithFailure(string reason)
    {
      Fail(reason);
      RaiseFailure(reason);
      SetState(PlayerState.Failed);
      return false;
    }

    /// <summary>
    /// Let go of the media: the sync first, because it points into this instance, then the stream.
    /// </summary>
    void ReleaseStream()
    {
      int handle;
      int sync;
      lock (_gate)
      {
        handle = _stream;
        sync = _sync;
        _stream = 0;
        _sync = 0;
        _pausedAtMs = -1;
      }
      if (handle == 0)
        return;
      if (sync != 0)
        Bass.ChannelRemoveSync(handle, sync);
      Bass.StreamFree(handle);
    }

    void StartTicker()
    {
      if (_disposed)
        return;
      _ticker.Change(TickIntervalMs, TickIntervalMs);
    }

    void StopTicker()
    {
      if (_disposed)
        return;
      try
      {
        _ticker.Change(Timeout.Infinite, Timeout.Infinite);
      }
      catch (ObjectDisposedException)
      {
        // Disposed while a BASS thread was on its way in. The timer is gone, which is what this wanted anyway.
      }
    }

    static string Describe(PlayerState state)
    {
      return state switch
      {
        PlayerState.Playing => "playing",
        PlayerState.Paused => "paused",
        PlayerState.Opening => "still opening",
        PlayerState.Ended => "at its end",
        PlayerState.Failed => "broken",
        PlayerState.Stopped => "stopped",
        _ => "idle"
      };
    }

    /// <summary>
    /// Give everything of this instance's back. The device is kept: another player is one keypress away, and
    /// opening the sound card again is heard as a click.
    /// </summary>
    public void Dispose()
    {
      PlayerState previous;
      lock (_gate)
      {
        if (_disposed)
          return;
        _disposed = true;
        previous = _state;
      }

      StopTicker();
      ReleaseStream();
      _ticker.Dispose();

      if (previous != PlayerState.Idle && previous != PlayerState.Stopped)
        SetState(PlayerState.Idle);
    }
  }
}