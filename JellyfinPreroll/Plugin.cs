using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinPreroll;

/// <summary>
/// Preroll & Intros plugin — plays videos from a designated library
/// before TV shows and/or movies on every Jellyfin client.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Singleton so other classes can read configuration easily.</summary>
    public static Plugin? Instance { get; private set; }

    public override string Name => "Preroll & Intros";

    public override string Description =>
        "Play videos from a selected library as prerolls (studio logos, bumpers, intros) before TV shows and movies.";

    public override Guid Id => new("f4c1d2a3-8b5e-4f6d-9a7c-3e2b1d0f8a9c");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
