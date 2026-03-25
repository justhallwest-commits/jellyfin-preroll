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

    // Sessions we have injected a pre-roll into, mapped to the main item id
    private readonly Dictionary<string, Guid> _injectedSessions =
        new(StringComparer.OrdinalIgnoreCase);

    // Ids of items that live in the pre-roll library
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
        var config = Plugin.Instance?.Configuration;
        if (config is null) return;

        var item    = e.Item;
        var session = e.Session;
        if (item is null || session is null) return;

        lock (_lock)
        {
            // This item is a pre-roll — do nothing
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: Skipping — item is a pre-roll.");
                return;
            }

            // Already injected for this session+item combo — do nothing
            // (fires when the main episode starts after the pre-roll finishes)
            if (_injectedSessions.TryGetValue(session.Id, out var trackedId)
                && trackedId == item.Id)
            {
                _logger.LogDebug(
                    "Pre-Roll: Main item already handled for session {Id}.", session.Id);
                return;
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

            // Track session → main item so we skip when main item starts
            _injectedSessions[session.Id] = item.Id;
        }

        var newQueue = selected.Select(v => v.Id).Append(item.Id).ToArray();

        _logger.LogInformation(
            "Pre-Roll: Injecting {Count} clip(s) before '{Item}' on session {Session}.",
            selected.Count, item.Name, session.Id);

        _ = Task.Run(async () =>
        {
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
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || e.Session is null) return;

        lock (_lock)
        {
            // Only clear the session guard when the MAIN item finishes,
            // not when the pre-roll finishes — this breaks the loop
            if (_injectedSessions.TryGetValue(e.Session.Id, out var trackedId)
                && trackedId == e.Item.Id)
            {
                _injectedSessions.Remove(e.Session.Id);
                _logger.LogDebug(
                    "Pre-Roll: Cleared session {Id} after main item finished.", e.Session.Id);
            }
        }
    }
}
