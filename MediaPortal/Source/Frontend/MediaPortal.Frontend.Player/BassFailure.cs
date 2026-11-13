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

using ManagedBass;

namespace MediaPortal.Frontend.Player
{
  /// <summary>
  /// What a BASS failure means, said to a person rather than as a number.
  /// </summary>
  /// <remarks>
  /// <para>
  /// BASS reports an error code and nothing else. The code is carried along in brackets, because somebody
  /// looking for help looks for the number rather than for the sentence. The wording matches the sentences
  /// <see cref="LibVlcPlayer"/> writes into the same field, so that one label never holds two languages.
  /// </para>
  /// <para>
  /// The numbers are the wrapper's and not always BASS's own: for one and the same attempt
  /// <c>BASS_ErrorGetCode</c> answered 43 where <see cref="Bass.LastError"/> answered 23. The table was
  /// therefore written against the wrapper this assembly references and not against the un4seen documentation.
  /// </para>
  /// </remarks>
  public static class BassFailure
  {
    /// <summary>
    /// A sentence about whatever BASS was asked to do last, read from the code it left behind.
    /// </summary>
    public static string Describe(string what)
    {
      return Describe(what, (int)Bass.LastError);
    }

    /// <summary>
    /// A sentence about a failure whose code is already known.
    /// </summary>
    public static string Describe(string what, int code)
    {
      string reason = (Errors)code switch
      {
        Errors.OK => "nothing failed",
        Errors.Memory => "there was not enough memory",
        Errors.FileOpen => "the file could not be opened",
        Errors.Driver => "the output device refused to cooperate",
        Errors.BufferLost => "the output buffer was lost, usually because the device went away",
        Errors.Handle => "the channel is no longer valid",
        Errors.SampleFormat => "BASS does not understand that sample format",
        Errors.Position => "that position is outside the track",
        Errors.Init => "BASS is not initialised",
        Errors.Start => "playback could not be started",
        Errors.SSL => "the encryption of the connection was not accepted",
        Errors.NoCD => "there is no CD in the drive",
        Errors.CDTrack => "that CD track is not available",
        Errors.Already => "that had already happened",
        Errors.NotPaused => "the track was not running in the first place",
        Errors.NotAudioTrack => "that is not an audio track",
        Errors.NoChannel => "there is no channel",
        Errors.Type => "that call does not fit this kind of channel",
        Errors.Parameter => "a parameter was invalid",
        Errors.No3D => "3D sound is not available",
        Errors.NoEAX => "EAX is not available",
        Errors.Device => "that output device has nothing to offer",
        Errors.NotPlaying => "the track is not running",
        Errors.SampleRate => "BASS cannot work at that sample rate",
        Errors.NotFile => "BASS wanted a data stream here and not a file name",
        Errors.NoHW => "the hardware will not do that",
        Errors.Empty => "the stream is empty",
        Errors.NoInternet => "BASS cannot get there without a network",
        Errors.Create => "the stream could not be created",
        Errors.NoFX => "that effect is not available",
        Errors.Playing => "the track is still running",
        Errors.NotAvailable => "that is not available at the moment",
        Errors.Decode => "the decoder could not get any further",
        Errors.DirectX => "DirectX did not cooperate",
        Errors.Timeout => "the server did not answer",
        Errors.FileFormat => "no decoder can read that file format",
        Errors.Speaker => "the speaker arrangement does not fit",
        Errors.Version => "the library is too old for that call",
        Errors.Codec => "the codec that would fit is missing",
        Errors.Ended => "the track is over",
        Errors.Busy => "the device is still busy",
        Errors.Unstreamable => "the stream cannot be fetched in pieces",
        _ => "BASS did not name a reason"
      };
      return what + ": " + reason + " (error " + code + ")";
    }
  }
}