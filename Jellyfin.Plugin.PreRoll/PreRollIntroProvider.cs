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

    // After injecting, suppress re-injection for the SAME item for this many seconds.
    // This blocks the spurious PlaybackStart that fires when our own SendPlayCommand
    // hands the episode back to the client after the pre-roll finishes.
    // Any DIFFERENT item always gets a fresh pre-roll immediately.
    private static readonly TimeSpan SameItemWindow = TimeSpan.FromSeconds(30);

    // Per session: the last item we injected a pre-roll for, and when
    private readonly Dictionary<string, (Guid ItemId, DateTime At)> _lastInjection
        = new(StringComparer.OrdinalIgnoreCase);

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
            // This item lives in the pre-roll library — never inject before it
            if (_preRollItemIds.Contains(item.Id))
            {
                _logger.LogDebug("Pre-Roll: '{Name}' is a pre-roll item — skipping.", item.Name);
                return;
            }

            // Same item started again within the suppression window —
            // this is the episode resuming after our pre-roll finished
            if (_lastInjection.TryGetValue(session.Id, out var last)
                && last.ItemId == item.Id
                && DateTime.UtcNow - last.At < SameItemWindow)
            {
                _logger.LogDebug(
                    "Pre-Roll: Same item '{Name}' within {Window}s window on session {Id} — suppressing.",
                    item.Name, SameItemWindow.TotalSeconds, session.Id);
                return;
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

            // Record this injection before sending the command so any
            // events fired by SendPlayCommand are already suppressed
            _lastInjection[session.Id] = (item.Id, DateTime.UtcNow);
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
                lock (_lock) { _lastInjection.Remove(session.Id); }
            }
        });
    }
}
