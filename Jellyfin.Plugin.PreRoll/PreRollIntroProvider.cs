using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Intercepts playback sessions server-side and injects pre-roll videos
/// into the client queue — works on all clients including Fire TV.
/// </summary>
public class PreRollServerEntryPoint : IServerEntryPoint
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollServerEntryPoint> _logger;

    // Sessions we have already injected a pre-roll into — cleared when
    // the pre-roll itself finishes so the next episode gets one too.
    private readonly HashSet<string> _injectedSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // Ids of items that live in the pre-roll library — used to break loops.
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

    public Task RunAsync()
    {
        _sessionManager.PlaybackStart   += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null) return;

        var item = e.Item;
        var session = e.Session;
        if (item is null || session is null) return;

        // ── Loop guard: if this item IS a pre-roll, don't inject again ──
        lock (_lock)
        {
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: Skipping injection — item is a pre-roll.");
                return;
            }
        }

        // ── Check whether this content type should get a pre-roll ────────
        bool shouldApply = item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };
        if (!shouldApply) return;

        // ── Guard: already injected for this session ─────────────────────
        lock (_lock)
        {
            if (_injectedSessions.Contains(session.Id))
            {
                _logger.LogDebug("Pre-Roll: Already injected for session {Id}.", session.Id);
                return;
            }
        }

        // ── Resolve pre-roll library ──────────────────────────────────────
        if (string.IsNullOrWhiteSpace(config.PreRollLibraryId) ||
            !Guid.TryParse(config.PreRollLibraryId, out var libraryId))
        {
            _logger.LogDebug("Pre-Roll: No library configured.");
            return;
        }

        var query = new InternalItemsQuery
        {
            ParentId      = libraryId,
            Recursive     = true,
            IsVirtualItem = false
        };

        var videos = _libraryManager.GetItemList(query);
        if (videos.Count == 0)
        {
            _logger.LogDebug("Pre-Roll: Library {Id} has no videos.", libraryId);
            return;
        }

        int max = Math.Clamp(config.MaxPreRolls, 1, 10);
        var selected = config.RandomOrder
            ? videos.OrderBy(_ => Random.Shared.Next()).Take(max).ToList()
            : videos.Take(max).ToList();

        // Register pre-roll ids so we can break the loop when they play
        lock (_lock)
        {
            foreach (var v in selected)
                _preRollItemIds.Add(v.Id);

            _injectedSessions.Add(session.Id);
        }

        // Build new queue: pre-rolls first, then the original item
        var newQueue = selected.Select(v => v.Id)
            .Append(item.Id)
            .ToArray();

        _logger.LogInformation(
            "Pre-Roll: Injecting {Count} clip(s) before '{Item}' on session {Session}.",
            selected.Count, item.Name, session.Id);

        // Fire-and-forget — send the new play queue to the client
        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.SendPlayCommand(
                    session.Id,
                    session.Id,
                    new PlayRequest
                    {
                        ItemIds    = newQueue,
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
        // When a pre-roll finishes, clear the session so the NEXT episode
        // in the same session still gets a pre-roll.
        if (e.Item is null || e.Session is null) return;

        lock (_lock)
        {
            if (_preRollItemIds.Contains(e.Item.Id))
            {
                _injectedSessions.Remove(e.Session.Id);
                _logger.LogDebug(
                    "Pre-Roll: Cleared injection state for session {Id}.", e.Session.Id);
            }
        }
    }

    public void Dispose()
    {
        _sessionManager.PlaybackStart   -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        GC.SuppressFinalize(this);
    }
}
