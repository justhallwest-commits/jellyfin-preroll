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

    // Only used to absorb the burst of events from SendPlayCommand itself
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromSeconds(10);

    private sealed class SessionInfo
    {
        public Guid     MainItemId    { get; set; }
        public bool     CommandSent   { get; set; }
        public DateTime CommandSentAt { get; set; }
    }

    private readonly Dictionary<string, SessionInfo> _sessions
        = new(StringComparer.OrdinalIgnoreCase);

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
        _sessionManager.PlaybackStart += OnPlaybackStart;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
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
            // Never inject before a pre-roll item
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: '{Name}' is a pre-roll — skipping.", item.Name);
                return;
            }

            if (_sessions.TryGetValue(session.Id, out var info))
            {
                // Within the short command cooldown — absorb spurious events
                if (info.CommandSent
                    && DateTime.UtcNow - info.CommandSentAt < CommandCooldown)
                {
                    _logger.LogDebug(
                        "Pre-Roll: Session {Id} in command cooldown — suppressing.", session.Id);
                    return;
                }

                // Same main item starting after pre-roll finished — let it play
                if (info.CommandSent && info.MainItemId == item.Id)
                {
                    _logger.LogDebug(
                        "Pre-Roll: Main item '{Name}' starting after pre-roll on session {Id}.",
                        item.Name, session.Id);
                    // Clear so the NEXT episode is eligible
                    _sessions.Remove(session.Id);
                    return;
                }

                // Different item — user picked something new, reset and fall through
                _logger.LogDebug(
                    "Pre-Roll: New item on session {Id} — resetting state.", session.Id);
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

            _sessions[session.Id] = new SessionInfo
            {
                MainItemId    = item.Id,
                CommandSent   = true,
                CommandSentAt = DateTime.UtcNow
            };
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
}
