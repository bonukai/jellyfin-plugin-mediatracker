namespace Jellyfin.Plugin.MediaTracker;

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Jellyfin.Data.Entities;

/// <summary>
/// Class ServerEntryPoint
/// </summary>
public class ServerEntryPoint : IHostedService, IDisposable
{

    private const double minimumProgressToMarkAsSeen = 0.85;
    private readonly PreviousActions progressDictionary;
    private readonly ISessionManager sessionManager;
    private readonly IUserDataManager userDataManager;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<ServerEntryPoint> logger;

    public static ServerEntryPoint? Instance { get; private set; }

    public ServerEntryPoint(
            ISessionManager sessionManager,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            IUserManager userManager,
            IUserDataManager userDataManager)
    {
        this.logger = loggerFactory.CreateLogger<ServerEntryPoint>();
        this.sessionManager = sessionManager;
        this.userDataManager = userDataManager;
        this.httpClientFactory = httpClientFactory;
        Instance = this;

        progressDictionary = new PreviousActions();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.PlaybackStart += PlaybackStart;
        sessionManager.PlaybackStopped += PlaybackStopped;
        sessionManager.PlaybackProgress += PlaybackProgress;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sessionManager.PlaybackStart -= PlaybackStart;
        sessionManager.PlaybackStopped -= PlaybackStopped;
        sessionManager.PlaybackProgress -= PlaybackProgress;

        return Task.CompletedTask;
    }

    private void PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        OnPlaybackChanged(e, e.IsPaused ? "paused" : "playing");
    }

    private void PlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        OnPlaybackChanged(e, "paused");
    }


    private void PlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        OnPlaybackChanged(e, e.IsPaused ? "paused" : "playing");
    }

    public void Dispose()
    {

    }

    private Int64? TryParseInt64(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (Int64.TryParse(value, out Int64 res))
        {
            return res;
        }

        return null;
    }

    private async void OnPlaybackChanged(PlaybackProgressEventArgs e, string action)
    {
        if (e.Users == null || e.Users.Count == 0)
        {
            logger.LogError("Missing users");
            return;
        }

        if (e.Item == null)
        {
            logger.LogError("Missing item");
            return;
        }

        if (e.Item is not Movie && e.Item is not Episode)
        {
            logger.LogDebug("Syncing playback of {0} is not supported by MediaTracer", e.Item.Path);
            return;
        }

        var positionTicks = e.PlaybackPositionTicks;
        var runTimeTicks = e.Item.RunTimeTicks;

        if (positionTicks == null)
        {
            logger.LogError("Missing position");
            return;
        }

        if (runTimeTicks == null)
        {
            logger.LogError("Missing video length");
            return;
        }

        var deviceName = e.DeviceName;
        var progress = (float)positionTicks / runTimeTicks;
        var durationInMilliseconds = (runTimeTicks / TimeSpan.TicksPerSecond) * 1000;

        progressDictionary.Cleanup();

        if (e.Item is Episode)
        {
            if (e.Item is not Episode episode)
            {
                logger.LogError("Missing episode object");
                return;
            }

            if (episode.Series == null)
            {
                logger.LogError("Missing show object");
                return;
            }

            if (episode.Season == null)
            {
                logger.LogError("Missing season object");
                return;
            }

            var seasonNumber = episode.Season.IndexNumber;
            var episodeNumber = episode.IndexNumber;

            if (episodeNumber == null)
            {
                logger.LogError("Missing episode number");
                return;
            }

            if (seasonNumber == null)
            {
                logger.LogError("Missing season number");
                return;
            }

            var imdbId = episode.Series.GetProviderId(MetadataProvider.Imdb);
            var tmdbId = TryParseInt64(episode.Series.GetProviderId(MetadataProvider.Tmdb));

            if (imdbId == null && tmdbId == null)
            {
                logger.LogError("Both imdb and tmdb id are absent");
                return;
            }

            foreach (var user in e.Users)
            {
                if (progressDictionary.ShouldSkipAction(user, episode, (float)progress, action))
                {
                    continue;
                }

                logger.LogInformation("Updating progress for episode of {0} S{1:00}E{2:00} - {3:0.00}% - {4} for user {5}",
                    episode.SeriesName,
                    seasonNumber,
                    episodeNumber,
                    progress * 100,
                    action,
                    user.Username);

                await UpdateProgress(user, new
                {
                    mediaType = "tv",
                    id = new
                    {
                        imdbId,
                        tmdbId
                    },
                    seasonNumber,
                    episodeNumber,
                    action,
                    progress,
                    duration = durationInMilliseconds,
                    device = deviceName
                });

                if (progress > minimumProgressToMarkAsSeen && progressDictionary.CanMarkAsSeen(user, episode))
                {
                    logger.LogInformation("Adding episode of {0} S{1:00}E{2:00} to seen history for user {3}",
                        episode.SeriesName,
                        seasonNumber,
                        episodeNumber,
                        user.Username);

                    await MarkAsSeen(user, new
                    {
                        mediaType = "tv",
                        id = new
                        {
                            imdbId,
                            tmdbId
                        },
                        seasonNumber,
                        episodeNumber,
                        duration = durationInMilliseconds,
                    });
                }
            }
        }
        else if (e.Item is Movie)
        {
            var movie = e.Item as Movie;

            if (movie == null)
            {
                logger.LogError("Missing movie object");
                return;
            }

            var imdbId = movie.GetProviderId(MetadataProvider.Imdb);
            var tmdbId = TryParseInt64(movie.GetProviderId(MetadataProvider.Tmdb));

            if (imdbId == null && tmdbId == null)
            {
                logger.LogError("Both imdb and tmdb id are absent");
                return;
            }

            foreach (var user in e.Users)
            {
                if (progressDictionary.ShouldSkipAction(user, movie, (float)progress, action))
                {
                    continue;
                }

                logger.LogInformation("Updating progress for movie {0} - {1:0.00}% - {2} for user {3}",
                    movie.Name,
                    progress * 100,
                    action,
                    user.Username);

                await UpdateProgress(user, new
                {
                    mediaType = "movie",
                    id = new
                    {
                        imdbId,
                        tmdbId
                    },
                    action,
                    progress,
                    duration = durationInMilliseconds,
                    device = deviceName
                });

                if (progress > minimumProgressToMarkAsSeen && progressDictionary.CanMarkAsSeen(user, movie))
                {
                    logger.LogInformation("Adding movie {0} to seen history for user {1}",
                        movie.Name,
                        user.Username);

                    await MarkAsSeen(user, new
                    {
                        mediaType = "movie",
                        id = new
                        {
                            imdbId,
                            tmdbId
                        },
                        duration = durationInMilliseconds,
                    });
                }
            }
        }
    }

    private async Task UpdateProgress(User user, dynamic payload)
    {
        await MediaTrackerPutAsync(user, payload, "/api/progress/by-external-id");
    }

    private async Task MarkAsSeen(User user, dynamic payload)
    {
        await MediaTrackerPutAsync(user, payload, "/api/seen/by-external-id");
    }

    private async Task MediaTrackerPutAsync(User user, dynamic payload, string path)
    {
        var mediaTrackerUrl = Plugin.Instance?.PluginConfiguration?.mediaTrackerUrl;

        if (mediaTrackerUrl == null)
        {
            logger.LogError("Missing MediaTracker url");
            return;
        }

        var apiToken = Plugin.Instance?.PluginConfiguration?.GetApiToken(user.Id);

        if (apiToken == null)
        {
            logger.LogError("Missing MediaTracker access token for user {1}", user.Username);
            return;
        }

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);

        var content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

        var uri = new Uri(new Uri(mediaTrackerUrl), path + "?token=" + apiToken);

        try
        {
            var response = await httpClientFactory.CreateClient().PutAsync(uri, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                logger.LogError("MediaTracker error: {0}", responseText);
            }

        }
        catch (System.Exception exception)
        {
            logger.LogError("Unexpected error: {0}", exception.Message);
        }
    }
}
