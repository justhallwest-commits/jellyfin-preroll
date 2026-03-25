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

    private readonly HashSet<string> _injectedSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
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

        var item = e.Item;
        var session = e.Session;
        if (item is null || session is null) return;

        lock (_lock)
        {
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: Skipping — item is a pre-roll.");
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

        lock (_lock)
        {
            if (_injectedSessions.Contains(session.Id))
                return;
        }

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

        int max = Math.Clamp(config.MaxPreRolls, 1, 10);
        var selected = config.RandomOrder
            ? videos.OrderBy(_ => Random.Shared.Next()).Take(max).ToList()
            : videos.Take(max).ToList();

        lock (_lock)
        {
            foreach (var v in selected)
                _preRollItemIds.Add(v.Id);
            _injectedSessions.Add(session.Id);
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
            if (_preRollItemIds.Contains(e.Item.Id))
                _injectedSessions.Remove(e.Session.Id);
        }
    }
}
