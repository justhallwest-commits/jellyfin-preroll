using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>Persisted settings for the Pre-Roll Videos plugin.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The ItemId (Guid string) of the Jellyfin library whose videos
    /// will be used as pre-roll content.
    /// </summary>
    public string PreRollLibraryId { get; set; } = string.Empty;

    /// <summary>Play a pre-roll before TV show episodes.</summary>
    public bool EnableForTvShows { get; set; } = true;

    /// <summary>Play a pre-roll before movies.</summary>
    public bool EnableForMovies { get; set; } = false;

    /// <summary>
    /// When true, a random video is picked each time.
    /// When false, videos are returned in library order.
    /// </summary>
    public bool RandomOrder { get; set; } = true;

    /// <summary>How many pre-roll clips to play before the main item (1–10).</summary>
    public int MaxPreRolls { get; set; } = 1;
}
