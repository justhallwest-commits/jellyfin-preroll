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

    // Short window to absorb spurious events fired by SendPlayCommand itself
    private static readonly TimeSpan CommandCooldown = TimeSpan.FromSeconds(10);

    private enum SessionState { Idle, CommandSent, PreRollPlaying, MainItemPlaying }

    private sealed class SessionInfo
    {
        public SessionState State       { get; set; } = SessionState.Idle;
        public Guid         MainItemId  { get; set; }
        public DateTime     CommandSentAt { get; set; }
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
            var info = GetOrCreate(session.Id);

            // ── This item is a pre-roll ──────────────────────────────────
            if (_preRollItemIds.Contains(item.Id))
            {
                if (info.State == SessionState.CommandSent)
                    info.State = SessionState.PreRollPlaying;
                _logger.LogDebug("Pre-Roll: '{Name}' is a pre-roll — now playing.", item.Name);
                return;
            }

            // ── This item is the main item starting after our command ────
            if (info.State == SessionState.CommandSent
                || info.State == SessionState.PreRollPlaying)
            {
                if (info.MainItemId == item.Id)
                {
                    info.State = SessionState.MainItemPlaying;
                    _logger.LogDebug(
                        "Pre-Roll: Main item '{Name}' now playing on session {Id}.",
                        item.Name, session.Id);
                    return;
                }
            }

            // ── Spurious event within command cooldown ───────────────────
            if (info.State == SessionState.CommandSent
                && DateTime.UtcNow - info.CommandSentAt < CommandCooldown)
            {
                _logger.LogDebug(
                    "Pre-Roll: Suppressing spurious event within cooldown on session {Id}.",
                    session.Id);
                return;
            }

            // ── Already playing main item — don't re-inject ──────────────
            if (info.State == SessionState.MainItemPlaying) return;
        }

        // ── Decide whether to inject ─────────────────────────────────────
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

            var info = GetOrCreate(session.Id);
            info.State        = SessionState.CommandSent;
            info.MainItemId   = item.Id;
            info.CommandSentAt = DateTime.UtcNow;
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
            if (!_sessions.TryGetValue(e.Session.Id, out var info)) return;

            // Main item finished — reset to Idle so the next episode gets a pre-roll
            if (info.State == SessionState.MainItemPlaying
                && info.MainItemId == e.Item.Id)
            {
                _sessions.Remove(e.Session.Id);
                _logger.LogDebug(
                    "Pre-Roll: Session {Id} reset — ready for next episode.", e.Session.Id);
                return;
            }

            // Pre-roll stopped unexpectedly (user skipped) — keep state so
            // main item still plays without re-injecting
            if (info.State == SessionState.PreRollPlaying
                && _preRollItemIds.Contains(e.Item.Id))
            {
                _logger.LogDebug(
                    "Pre-Roll: Pre-roll stopped on session {Id} — waiting for main item.", e.Session.Id);
            }
        }
    }

    private SessionInfo GetOrCreate(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var info))
        {
            info = new SessionInfo();
            _sessions[sessionId] = info;
        }
        return info;
    }
}
