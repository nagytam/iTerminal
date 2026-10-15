namespace iTerminal.Core.Data.Music;

/// <summary>
/// An ordered set of tracks plus the cursor into it. Two orderings coexist: the visible order the
/// user arranged, and a shuffled play order used only to decide what comes next — so shuffling
/// never rearranges the list under the user's cursor, and turning it off restores plain sequence.
///
/// <para>All members are safe to call from the UI thread and the playback pump at the same time;
/// the playlist is the one piece of state both touch.</para>
/// </summary>
public sealed class Playlist
{
    private readonly object _syncLock = new();
    private readonly List<MusicTrack> _tracks = [];
    private readonly List<int> _playOrder = [];
    private readonly Random _random = new();

    private int _currentIndex = -1;
    private bool _isShuffled;
    private int _version;

    /// <summary>
    /// Bumped on every change to the track list. Lets the UI rebuild its rows and the session job
    /// decide whether to re-save, without either of them diffing the list.
    /// </summary>
    public int Version
    {
        get
        {
            lock (_syncLock)
            {
                return _version;
            }
        }
    }

    /// <summary>Snapshot of the tracks in user order. Safe to enumerate while playback continues.</summary>
    public IReadOnlyList<MusicTrack> Tracks
    {
        get
        {
            lock (_syncLock)
            {
                return [.. _tracks];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _tracks.Count;
            }
        }
    }

    /// <summary>Index of the playing (or last played) track, or -1 when nothing is cued.</summary>
    public int CurrentIndex
    {
        get
        {
            lock (_syncLock)
            {
                return _currentIndex;
            }
        }
        set
        {
            lock (_syncLock)
            {
                _currentIndex = value >= 0 && value < _tracks.Count ? value : -1;
            }
        }
    }

    public MusicTrack? CurrentTrack
    {
        get
        {
            lock (_syncLock)
            {
                return _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null;
            }
        }
    }

    public bool IsShuffled
    {
        get
        {
            lock (_syncLock)
            {
                return _isShuffled;
            }
        }
        set
        {
            lock (_syncLock)
            {
                if (_isShuffled == value)
                {
                    return;
                }

                _isShuffled = value;
                RebuildPlayOrder();
            }
        }
    }

    public MusicTrack? this[int index]
    {
        get
        {
            lock (_syncLock)
            {
                return index >= 0 && index < _tracks.Count ? _tracks[index] : null;
            }
        }
    }

    /// <summary>Appends a track unless the same path is already present. Returns true when added.</summary>
    public bool Add(MusicTrack track)
    {
        if (track is null)
        {
            return false;
        }

        lock (_syncLock)
        {
            if (_tracks.Contains(track))
            {
                return false;
            }

            _tracks.Add(track);
            RebuildPlayOrder();
            return true;
        }
    }

    /// <summary>Appends many tracks, skipping duplicates. Returns how many were actually added.</summary>
    public int AddRange(IEnumerable<MusicTrack> tracks)
    {
        var added = 0;
        lock (_syncLock)
        {
            foreach (var track in tracks ?? [])
            {
                if (track is null || _tracks.Contains(track))
                {
                    continue;
                }

                _tracks.Add(track);
                added++;
            }

            if (added > 0)
            {
                RebuildPlayOrder();
            }
        }

        return added;
    }

    /// <summary>
    /// Removes one entry, keeping <see cref="CurrentIndex"/> pointing at the same track when the
    /// removal happened above it — so deleting earlier rows never derails what is playing.
    /// </summary>
    public bool RemoveAt(int index)
    {
        lock (_syncLock)
        {
            if (index < 0 || index >= _tracks.Count)
            {
                return false;
            }

            _tracks.RemoveAt(index);

            if (_currentIndex == index)
            {
                _currentIndex = -1;
            }
            else if (_currentIndex > index)
            {
                _currentIndex--;
            }

            RebuildPlayOrder();
            return true;
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _tracks.Clear();
            _playOrder.Clear();
            _currentIndex = -1;
            _version++;
        }
    }

    /// <summary>Moves a row up or down, following it with the cursor when it is the current track.</summary>
    public bool Move(int index, int offset)
    {
        lock (_syncLock)
        {
            var target = index + offset;
            if (index < 0 || index >= _tracks.Count || target < 0 || target >= _tracks.Count || offset == 0)
            {
                return false;
            }

            (_tracks[target], _tracks[index]) = (_tracks[index], _tracks[target]);

            if (_currentIndex == index)
            {
                _currentIndex = target;
            }
            else if (_currentIndex == target)
            {
                _currentIndex = index;
            }

            RebuildPlayOrder();
            return true;
        }
    }

    /// <summary>Replaces the whole list, e.g. after loading an M3U. Resets the cursor.</summary>
    public void ReplaceAll(IEnumerable<MusicTrack> tracks)
    {
        lock (_syncLock)
        {
            _tracks.Clear();
            foreach (var track in tracks ?? [])
            {
                if (track is not null && !_tracks.Contains(track))
                {
                    _tracks.Add(track);
                }
            }

            _currentIndex = -1;
            RebuildPlayOrder();
        }
    }

    /// <summary>Updates the metadata of the entry at <paramref name="index"/> in place.</summary>
    public void UpdateAt(int index, MusicTrack track)
    {
        lock (_syncLock)
        {
            if (index >= 0 && index < _tracks.Count && track is not null)
            {
                _tracks[index] = track;
                _version++;
            }
        }
    }

    /// <summary>
    /// Index of the track after the current one, or -1 when playback should stop.
    /// <see cref="RepeatMode.One"/> returns the current track so it replays.
    /// </summary>
    public int GetNextIndex(RepeatMode repeatMode)
    {
        lock (_syncLock)
        {
            if (_tracks.Count == 0)
            {
                return -1;
            }

            if (repeatMode == RepeatMode.One && _currentIndex >= 0)
            {
                return _currentIndex;
            }

            var position = _playOrder.IndexOf(_currentIndex);
            if (position < 0)
            {
                return _playOrder.Count > 0 ? _playOrder[0] : -1;
            }

            if (position + 1 < _playOrder.Count)
            {
                return _playOrder[position + 1];
            }

            if (repeatMode == RepeatMode.All)
            {
                // Wrapping is the moment to reshuffle, so a repeating shuffled playlist does not
                // replay the same permutation forever.
                if (_isShuffled)
                {
                    RebuildPlayOrder(keepCurrentFirst: false);
                }

                return _playOrder.Count > 0 ? _playOrder[0] : -1;
            }

            return -1;
        }
    }

    /// <summary>Index of the track before the current one, wrapping only when repeating everything.</summary>
    public int GetPreviousIndex(RepeatMode repeatMode)
    {
        lock (_syncLock)
        {
            if (_tracks.Count == 0)
            {
                return -1;
            }

            var position = _playOrder.IndexOf(_currentIndex);
            if (position < 0)
            {
                return _playOrder.Count > 0 ? _playOrder[0] : -1;
            }

            if (position - 1 >= 0)
            {
                return _playOrder[position - 1];
            }

            return repeatMode == RepeatMode.All && _playOrder.Count > 0
                ? _playOrder[^1]
                : -1;
        }
    }

    private void RebuildPlayOrder(bool keepCurrentFirst = true)
    {
        _version++;
        _playOrder.Clear();
        for (int i = 0; i < _tracks.Count; i++)
        {
            _playOrder.Add(i);
        }

        if (!_isShuffled || _playOrder.Count <= 1)
        {
            return;
        }

        // Fisher-Yates over the whole list, then pull the current track to the front so the shuffle
        // decides what plays *after* it rather than interrupting it.
        for (int i = _playOrder.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_playOrder[i], _playOrder[j]) = (_playOrder[j], _playOrder[i]);
        }

        if (keepCurrentFirst && _currentIndex >= 0)
        {
            var position = _playOrder.IndexOf(_currentIndex);
            if (position > 0)
            {
                (_playOrder[0], _playOrder[position]) = (_playOrder[position], _playOrder[0]);
            }
        }
    }
}
