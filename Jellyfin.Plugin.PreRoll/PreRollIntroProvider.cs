using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Implements <see cref="IIntroProvider"/> to inject pre-roll videos
/// (studio logos, commercials, broadcast intros) before TV episodes or movies.
/// </summary>
public class PreRollIntroProvider : IIntroProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollIntroProvider> _logger;

    // Shared random instance — thread-safe in .NET 6+
    private static readonly Random _random = Random.Shared;

    /// <inheritdoc />
    public string Name => "Pre-Roll Videos";

    /// <summary>Initializes the provider via DI.</summary>
    public PreRollIntroProvider(
        ILibraryManager libraryManager,
        ILogger<PreRollIntroProvider> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        var config = Plugin.Instance?.Configuration;

        if (config is null)
        {
            _logger.LogWarning("Pre-Roll: Plugin.Instance is null — skipping.");
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        // Determine whether this item type should get a pre-roll
        bool shouldApply = item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };

        if (!shouldApply)
            return Task.FromResult(Enumerable.Empty<IntroInfo>());

        if (string.IsNullOrWhiteSpace(config.PreRollLibraryId))
        {
            _logger.LogDebug("Pre-Roll: No library configured.");
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        if (!Guid.TryParse(config.PreRollLibraryId, out var libraryId))
        {
            _logger.LogWarning("Pre-Roll: Configured library ID '{Id}' is not a valid GUID.", config.PreRollLibraryId);
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        // Query the pre-roll library for video items
        var query = new InternalItemsQuery(user)
        {
            ParentId      = libraryId,
            MediaTypes    = [MediaType.Video],
            Recursive     = true,
            IsVirtualItem = false
        };

        var videos = _libraryManager.GetItemList(query);

        if (videos.Count == 0)
        {
            _logger.LogDebug("Pre-Roll: Library {Id} contains no video items.", libraryId);
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }

        int max = Math.Clamp(config.MaxPreRolls, 1, 10);

        IEnumerable<BaseItem> selected = config.RandomOrder
            ? videos.OrderBy(_ => _random.Next()).Take(max)
            : videos.Take(max);

        var intros = selected.Select(v =>
        {
            _logger.LogDebug("Pre-Roll: Injecting '{Name}' (Id={Id}) before '{Item}'.",
                v.Name, v.Id, item.Name);
            return new IntroInfo { ItemId = v.Id };
        });

        return Task.FromResult(intros);
    }
}
