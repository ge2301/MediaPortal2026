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

namespace MediaPortal.Frontend.Player;

/// <summary>
/// What a media said about itself while it was playing, in the one line a screen has room for.
/// </summary>
/// <remarks>
/// One line and no fields for station, artist and track, because that is what a live stream is able to answer:
/// a Shoutcast server hands over "StreamTitle='whoever - whatever'", and everything this side could make of that
/// is a guess about where one name ends and the other starts. A screen that was given three fields would have to
/// decide what to do when two of them are empty, which is the engine's vocabulary showing through rather than the
/// media's own.
/// </remarks>
public sealed class StreamMetaEventArgs : EventArgs
{
  public StreamMetaEventArgs(string line)
  {
    Line = line ?? "";
  }

  /// <summary>What the media named this piece of sound. Never empty - an engine says nothing instead.</summary>
  public string Line { get; }
}

/// <summary>
/// An engine that can hear what a stream says about itself.
/// </summary>
/// <remarks>
/// <para>
/// Its own interface rather than a member of <see cref="IVideoPlayer"/>, for the same reason the spectrum source
/// is: a media that is a file says nothing new while it plays, so an engine that has nothing to report should not
/// be made to carry the question, and a screen should ask rather than be handed an event that can only ever be
/// silent. The screens ask with <c>as IStreamMetaSource</c> and get on without it when the answer is null.
/// </para>
/// <para>
/// The news is about the media, not the engine: the screen that shows a tune is the one that decides that the
/// station's own name beats the name the shelf called it by, and for how long.
/// </para>
/// <para>
/// BASS answers this today. The libVLC build does not, and honestly so: its metadata arrives on the
/// <c>Media</c> object it plays, which its player here does not hold on to - it hands the engine an address and
/// lets go - so wiring the question would mean rebuilding the way that engine opens a media, for an answer the
/// default engine already gives. A screen that gets nothing keeps the name the viewer chose, which is the
/// answer it gives a file anyway.
/// </para>
/// </remarks>
public interface IStreamMetaSource
{
  /// <summary>
  /// Raised when the media says something about itself that it had not said before. Anything the sender has to
  /// say about the current media belongs here, and nothing it merely repeats.
  /// </summary>
  event EventHandler<StreamMetaEventArgs>? MetaChanged;
}
