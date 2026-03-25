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

    // Per-session state machine
    private enum State { Idle, Injected }
    private readonly Dictionary<string, (State State, Guid MainItemId, DateTime InjectedAt)> _sessions
        = new(StringComparer.OrdinalIgnoreCase);

    // How long to suppress re-injection after sending a play command
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    // Ids of items in the pre-roll library
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
            // Always skip pre-roll items — never inject before a pre-roll
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: '{Name}' is a pre-roll item — skipping.", item.Name);
                return;
            }

            // If we are in Injected state for this session, suppress until cooldown expires
            if (_sessions.TryGetValue(session.Id, out var s) && s.State == State.Injected)
            {
                // Still within cooldown — this is the main item starting after the pre-roll
                if (DateTime.UtcNow - s.InjectedAt < Cooldown)
                {
                    _logger.LogDebug(
                        "Pre-Roll: Session {Id} in cooldown — not re-injecting.", session.Id);
                    return;
                }

                // Cooldown expired — treat as fresh (user started something new)
                _sessions.Remove(session.Id);
            }
        }

        // Check content type
        bool shouldApply = item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };
        if (!shouldApply) return;

        // Resolve library
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

            // Enter Injected state — suppress re-injection for Cooldown period
            _sessions[session.Id] = (State.Injected, item.Id, DateTime.UtcNow);
        }

        var newQueue = selected.Select(v => v.Id).Append(item.Id).ToArray();

        _logger.LogInformation(
            "Pre-Roll: Injecting {Count} clip(s) before '{Item}' on session {Session}.",
            selected.Count, item.Name, session.Id);

        _ = Task.Run(async () =>
        {
            // Small delay so the current PlaybackStart event fully resolves first
            await Task.Delay(500).ConfigureAwait(false);
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
                _logger.LogError(ex, "Pre-Roll: Failed to send play command to session {Id}.", session.Id);
                // Roll back state so the next attempt works
                lock (_lock) { _sessions.Remove(session.Id); }
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || e.Session is null) return;

        lock (_lock)
        {
            // When the main item finishes, reset to Idle so the next episode gets a pre-roll
            if (_sessions.TryGetValue(e.Session.Id, out var s)
                && s.State == State.Injected
                && s.MainItemId == e.Item.Id)
            {
                _sessions.Remove(e.Session.Id);
                _logger.LogDebug(
                    "Pre-Roll: Session {Id} reset after main item finished.", e.Session.Id);
            }
        }
    }
}
