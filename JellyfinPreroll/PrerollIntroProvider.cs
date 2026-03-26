using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace JellyfinPreroll;

/// <summary>
/// Server-side intro provider. Jellyfin calls GetIntros every time a client
/// starts playback. Because this runs on the server, it works on every
/// client — web, Fire TV, Roku, iOS, Android, etc.
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
    public string Name => "Pre-Roll Videos";

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

        if (string.IsNullOrWhiteSpace(config.PrerollLibraryId))
        {
            _logger.LogDebug("Preroll library not configured — skipping");
            return Enumerable.Empty<IntroInfo>();
        }

        if (!ShouldAddPreroll(item, config))
            return Enumerable.Empty<IntroInfo>();

        var prerollVideos = GetPrerollVideos(config);
        if (prerollVideos.Count == 0)
        {
            _logger.LogWarning("Preroll library {Id} has no usable videos", config.PrerollLibraryId);
            return Enumerable.Empty<IntroInfo>();
        }

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

    private static bool ShouldAddPreroll(BaseItem item, PluginConfiguration config)
    {
        return item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };
    }

    private List<BaseItem> GetPrerollVideos(PluginConfiguration config)
    {
        if (!Guid.TryParse(config.PrerollLibraryId, out var libraryId))
            return new List<BaseItem>();

        var query = new InternalItemsQuery
        {
            ParentId = libraryId,
            IsVirtualItem = false,
            Recursive = true
        };

        var results = _libraryManager.GetItemsResult(query);

        var videos = results.Items
            .Where(v => v is Video or Movie)
            .ToList();

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
