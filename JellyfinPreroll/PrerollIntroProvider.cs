using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace JellyfinPreroll;

/// <summary>
/// Server-side intro provider.  Jellyfin calls <see cref="GetIntros"/> every
/// time a client starts playback.  Because this runs on the server and returns
/// items through the standard API, it works on <b>every</b> client — web,
/// Fire TV, Roku, iOS, Android, etc.
/// </summary>
public class PrerollIntroProvider : IIntroProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PrerollIntroProvider> _logger;
    private static readonly Random _rng = new();

    public PrerollIntroProvider(
        ILibraryManager libraryManager,
        ILogger<PrerollIntroProvider> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Preroll & Intros";

    /// <inheritdoc />
    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        try
        {
            return Task.FromResult(GetIntrosInternal(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preroll plugin failed to provide intros");
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }
    }

    private IEnumerable<IntroInfo> GetIntrosInternal(BaseItem item)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
            return Enumerable.Empty<IntroInfo>();

        // ── Guard: is the plugin configured? ──────────────────────────
        if (string.IsNullOrWhiteSpace(config.PrerollLibraryId))
        {
            _logger.LogDebug("Preroll library not configured — skipping");
            return Enumerable.Empty<IntroInfo>();
        }

        // ── Guard: should we add prerolls for this item type? ─────────
        if (!ShouldAddPreroll(item, config))
            return Enumerable.Empty<IntroInfo>();

        // ── Fetch candidate videos from the preroll library ───────────
        var prerollVideos = GetPrerollVideos(config);
        if (prerollVideos.Count == 0)
        {
            _logger.LogWarning("Preroll library {Id} has no usable videos", config.PrerollLibraryId);
            return Enumerable.Empty<IntroInfo>();
        }

        // ── Pick random videos ────────────────────────────────────────
        int count = Math.Clamp(config.PrerollCount, 1, 5);
        count = Math.Min(count, prerollVideos.Count);

        var selected = prerollVideos
            .OrderBy(_ => _rng.Next())
            .Take(count);

        _logger.LogInformation(
            "Injecting {Count} preroll(s) before \"{ItemName}\"",
            count, item.Name);

        return selected.Select(v => new IntroInfo { ItemId = v.Id });
    }

    /// <summary>
    /// Decides whether the currently-playing item should get prerolls.
    /// </summary>
    private static bool ShouldAddPreroll(BaseItem item, PluginConfiguration config)
    {
        return item switch
        {
            Episode   => config.EnableForTvShows,
            Movie     => config.EnableForMovies,
            _         => false
        };
    }

    /// <summary>
    /// Returns all videos in the configured preroll library, optionally
    /// filtered by a maximum duration.
    /// </summary>
    private List<BaseItem> GetPrerollVideos(PluginConfiguration config)
    {
        if (!Guid.TryParse(config.PrerollLibraryId, out var libraryId))
            return new List<BaseItem>();

        var query = new InternalItemsQuery
        {
            ParentId = libraryId,
            IncludeItemTypes = new[] { BaseItemKind.Video, BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true
        };

        var results = _libraryManager.GetItemsResult(query);
        var videos = results.Items.ToList();

        // Optional duration filter
        if (config.MaxDurationSeconds > 0)
        {
            var maxTicks = TimeSpan.FromSeconds(config.MaxDurationSeconds).Ticks;
            videos = videos
                .Where(v => v.RunTimeTicks.HasValue && v.RunTimeTicks.Value <= maxTicks)
                .ToList();
        }

        return videos;
    }
}
