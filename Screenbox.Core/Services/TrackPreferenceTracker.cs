using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

/// <summary>
/// Remembers the audio and subtitle tracks of the media items that were played most recently.
/// </summary>
/// <remarks>
/// The choices are kept in memory and written to the database after every change, because a media player is
/// usually closed by closing its window rather than by an orderly shutdown.
/// </remarks>
public sealed partial class TrackPreferenceTracker : ObservableRecipient, ITrackPreferenceTracker,
    IRecipient<SuspendingMessage>
{
    private const int Capacity = 256;

    private readonly IDatabaseService _databaseService;
    private readonly ILogger<TrackPreferenceTracker> _logger;
    private readonly object _lock = new();
    private List<MediaTrackPreference> _preferences = new();
    private Task _saveTask = Task.CompletedTask;
    private Task? _loadTask;

    public TrackPreferenceTracker(IDatabaseService databaseService, ILogger<TrackPreferenceTracker> logger)
    {
        _databaseService = databaseService;
        _logger = logger;

        Messenger.Register<SuspendingMessage>(this);
    }

    public void Receive(SuspendingMessage message)
    {
        message.Reply(SaveToDiskAsync());
    }

    /// <inheritdoc/>
    public Task EnsureLoadedAsync()
    {
        lock (_lock)
        {
            return _loadTask ??= LoadFromDiskAsync();
        }
    }

    /// <inheritdoc/>
    public MediaTrackPreference? Get(string location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        lock (_lock)
        {
            return _preferences.Find(x => x.Location == location);
        }
    }

    /// <inheritdoc/>
    public void UpdateAudio(string location, MediaTrackChoice choice)
    {
        Update(location, preference => preference with { Audio = choice });
    }

    /// <inheritdoc/>
    public void UpdateSubtitle(string location, MediaTrackChoice choice)
    {
        Update(location, preference => preference with { Subtitle = choice });
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        lock (_lock)
        {
            _preferences.Clear();
        }

        ScheduleSave();
    }

    /// <inheritdoc/>
    public async Task SaveToDiskAsync()
    {
        List<MediaTrackPreference> snapshot;
        lock (_lock)
        {
            snapshot = new List<MediaTrackPreference>(_preferences);
        }

        await WriteAsync(snapshot);
    }

    private void Update(string location, Func<MediaTrackPreference, MediaTrackPreference> change)
    {
        if (string.IsNullOrEmpty(location)) return;

        lock (_lock)
        {
            int index = _preferences.FindIndex(x => x.Location == location);
            MediaTrackPreference preference = index >= 0
                ? _preferences[index]
                : new MediaTrackPreference(location, null, null, DateTimeOffset.Now);

            preference = change(preference) with { Updated = DateTimeOffset.Now };
            if (index >= 0)
            {
                _preferences.RemoveAt(index);
            }

            // The most recently played items are kept, because they are the ones that are played again.
            _preferences.Insert(0, preference);
            if (_preferences.Count > Capacity)
            {
                _preferences.RemoveRange(Capacity, _preferences.Count - Capacity);
            }
        }

        ScheduleSave();
    }

    /// <summary>
    /// Queues a write of the current choices behind the write that may already be running, because two writes
    /// at the same time would replace the same rows.
    /// </summary>
    private void ScheduleSave()
    {
        List<MediaTrackPreference> snapshot;
        lock (_lock)
        {
            snapshot = new List<MediaTrackPreference>(_preferences);
            _saveTask = _saveTask.ContinueWith(_ => WriteAsync(snapshot), TaskScheduler.Default).Unwrap();
        }
    }

    private async Task WriteAsync(List<MediaTrackPreference> snapshot)
    {
        try
        {
            await Task.Run(() => _databaseService.ReplaceTrackPreferencesAsync(snapshot));
        }
        catch (Exception e)
        {
            // Losing a remembered track only means the next playback starts with the default tracks.
            _logger.LogError(e, "Failed to save the audio and subtitle track choices.");
        }
    }

    private async Task LoadFromDiskAsync()
    {
        try
        {
            List<MediaTrackPreference> loaded = await Task.Run(() => _databaseService.LoadTrackPreferencesAsync());
            lock (_lock)
            {
                // Choices made while loading are newer than what is on disk, so they are kept.
                foreach (MediaTrackPreference preference in _preferences)
                {
                    loaded.RemoveAll(x => x.Location == preference.Location);
                }

                loaded.InsertRange(0, _preferences);
                if (loaded.Count > Capacity)
                {
                    loaded.RemoveRange(Capacity, loaded.Count - Capacity);
                }

                _preferences = loaded;
            }
        }
        catch (Exception e)
        {
            // Non-fatal: playback starts with the default tracks.
            _logger.LogError(e, "Failed to load the audio and subtitle track choices.");
        }
    }
}
