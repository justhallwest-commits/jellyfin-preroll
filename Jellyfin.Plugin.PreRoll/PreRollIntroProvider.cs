using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

public class PreRollServerEntryPoint : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollServerEntryPoint> _logger;
    private readonly object _lock = new();

    private enum State { Idle, Injected }

    private readonly Dictionary<string, (State State, Guid MainItemId, DateTime InjectedAt)> _sessions
        = new(StringComparer.OrdinalIgnoreCase);

    // How long after injection to suppress re-injection
    // Must be longer than the longest pre-roll clip
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly HashSet<Guid> _preRollItemIds = new();

    public PreRollServerEntryPoint(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ILogger<PreRollServerEntryPoint> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart   += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart   -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var config  = Plugin.Instance?.Configuration;
        var item    = e.Item;
        var session = e.Session;

        if (config is null || item is null || session is null) return;

        lock (_lock)
        {
            // This item is from the pre-roll library — never inject before it
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: '{Name}' is a pre-roll — skipping.", item.Name);
                return;
            }

            // Within cooldown window — this is the main episode starting
            // after the pre-roll finished. Suppress injection.
            if (_sessions.TryGetValue(session.Id, out var s) && s.State == State.Injected)
            {
                if (DateTime.UtcNow - s.InjectedAt < Cooldown)
                {
                    _logger.LogDebug(
                        "Pre-Roll: Session {Id} in cooldown — suppressing.", session.Id);
                    return;
                }

                // Cooldown expired — user started something new
                _sessions.Remove(session.Id);
            }
        }

        bool shouldApply = item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };
        if (!shouldApply) return;

        if (string.IsNullOrWhiteSpace(config.PreRollLibraryId) ||
            !Guid.TryParse(config.PreRollLibraryId, out var libraryId))
            return;

        var videos = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId      = libraryId,
            Recursive     = true,
            IsVirtualItem = false
        });

        if (videos.Count == 0) return;

        int max      = Math.Clamp(config.MaxPreRolls, 1, 10);
        var selected = config.RandomOrder
            ? videos.OrderBy(_ => Random.Shared.Next()).Take(max).ToList()
            : videos.Take(max).ToList();

        lock (_lock)
        {
            foreach (var v in selected)
                _preRollItemIds.Add(v.Id);

            // Set Injected state BEFORE sending the command so any
            // PlaybackStopped/Start events fired by SendPlayCommand
            // are already suppressed
            _sessions[session.Id] = (State.Injected, item.Id, DateTime.UtcNow);
        }

        var newQueue = selected.Select(v => v.Id).Append(item.Id).ToArray();

        _logger.LogInformation(
            "Pre-Roll: Injecting {Count} clip(s) before '{Item}' on session {Session}.",
            selected.Count, item.Name, session.Id);

        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            try
            {
                await _sessionManager.SendPlayCommand(
                    session.Id,
                    session.Id,
                    new PlayRequest
                    {
                        ItemIds     = newQueue,
                        PlayCommand = PlayCommand.PlayNow,
                        StartIndex  = 0
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pre-Roll: Failed to send play command.");
                lock (_lock) { _sessions.Remove(session.Id); }
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || e.Session is null) return;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(e.Session.Id, out var s)) return;
            if (s.State != State.Injected) return;
            if (s.MainItemId != e.Item.Id) return;

            // Only clear the guard when the main item stops AFTER the cooldown.
            // Within the cooldown, the stop was caused by our own SendPlayCommand
            // replacing the queue — keep the guard active so the re-triggered
            // PlaybackStart for the main item doesn't cause another injection.
            if (DateTime.UtcNow - s.InjectedAt >= Cooldown)
            {
                _sessions.Remove(e.Session.Id);
                _logger.LogDebug(
                    "Pre-Roll: Session {Id} cleared after main item finished.", e.Session.Id);
            }
            else
            {
                _logger.LogDebug(
                    "Pre-Roll: Session {Id} stop within cooldown — keeping guard.", e.Session.Id);
            }
        }
    }
}
