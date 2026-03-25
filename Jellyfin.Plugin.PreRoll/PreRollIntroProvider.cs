using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreRoll;

public class PreRollIntroProvider : IIntroProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<PreRollIntroProvider> _logger;
    private static readonly Random _random = Random.Shared;

    public string Name => "Pre-Roll Videos";

    public PreRollIntroProvider(
        ILibraryManager libraryManager,
        ILogger<PreRollIntroProvider> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        var config = Plugin.Instance?.Configuration;

        if (config is null)
            return Task.FromResult(Enumerable.Empty<IntroInfo>());

        bool shouldApply = item switch
        {
            Episode => config.EnableForTvShows,
            Movie   => config.EnableForMovies,
            _       => false
        };

        if (!shouldApply)
            return Task.FromResult(Enumerable.Empty<IntroInfo>());

        if (string.IsNullOrWhiteSpace(config.PreRollLibraryId) ||
            !Guid.TryParse(config.PreRollLibraryId, out var libraryId))
            return Task.FromResult(Enumerable.Empty<IntroInfo>());

        var query = new InternalItemsQuery(user)
        {
            ParentId      = libraryId,
            Recursive     = true,
            IsVirtualItem = false
        };

        var videos = _libraryManager.GetItemList(query);

        if (videos.Count == 0)
            return Task.FromResult(Enumerable.Empty<IntroInfo>());

        int max = Math.Clamp(config.MaxPreRolls, 1, 10);

        var selected = config.RandomOrder
            ? videos.OrderBy(_ => _random.Next()).Take(max)
            : videos.Take(max);

        var intros = selected.Select(v => new IntroInfo { ItemId = v.Id });
        return Task.FromResult(intros);
    }
}
