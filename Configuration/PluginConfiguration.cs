using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediaTracker.Configuration;

public class User
{
    public User()
    {
        id = "";
        apiToken = "";
    }

    public string id { get; set; }
    public string apiToken { get; set; }
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        users = Array.Empty<User>();
        mediaTrackerUrl = "http://localhost:7481";
    }

    /// <summary>
    /// Gets or sets a API token.
    /// </summary>
    public User[] users { get; set; }

    /// <summary>
    /// Gets or sets a MediaTracker URL.
    /// </summary>
    public string mediaTrackerUrl { get; set; }


    public string? GetApiToken(Guid userId)
    {
        foreach (var user in this.users)
        {
            if (Guid.Parse(user.id) == userId)
            {
                return user.apiToken;
            }
        }

        return null;
    }
}
