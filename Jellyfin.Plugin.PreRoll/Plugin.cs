using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PreRoll;

/// <summary>
/// Pre-Roll Videos plugin for Jellyfin.
/// Plays videos from a chosen library before TV episodes and/or movies.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plugin's stable GUID — do not change this.</summary>
    public static readonly Guid StaticId = new("a4b2c3d4-e5f6-7890-abcd-ef1234567891");

    /// <inheritdoc />
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Singleton reference set during construction.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Pre-Roll Videos";

    /// <inheritdoc />
    public override Guid Id => StaticId;

    /// <inheritdoc />
   public override string Description =>
    "Plays videos from a selected library before TV episodes or movies — " +
    "works on all clients including Fire TV. Perfect for studio logos, " +
    "custom commercials, or broadcast intros.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
