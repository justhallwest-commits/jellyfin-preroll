using MediaBrowser.Model.Plugins;

namespace JellyfinPreroll;

/// <summary>
/// Persisted configuration for the Preroll &amp; Intros plugin.
/// Edited via the Jellyfin Dashboard → Plugins → Preroll &amp; Intros.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The Jellyfin library ID that contains preroll videos.
    /// Empty string means the plugin is effectively disabled.
    /// </summary>
    public string PrerollLibraryId { get; set; } = string.Empty;

    /// <summary>
    /// How many preroll videos to play before each item (1–5).
    /// Videos are chosen at random from the preroll library.
    /// </summary>
    public int PrerollCount { get; set; } = 1;

    /// <summary>Play prerolls before TV show episodes.</summary>
    public bool EnableForTvShows { get; set; } = true;

    /// <summary>Play prerolls before movies.</summary>
    public bool EnableForMovies { get; set; } = true;

    /// <summary>
    /// Limit the maximum duration (in seconds) of each preroll video.
    /// Videos longer than this are skipped. 0 = no limit.
    /// </summary>
    public int MaxDurationSeconds { get; set; } = 0;
}
