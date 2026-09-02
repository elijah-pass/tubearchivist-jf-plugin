using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TubeArchivistMetadata.TubeArchivist;
using Jellyfin.Plugin.TubeArchivistMetadata.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TubeArchivistMetadata.Tasks
{
    /// <summary>
    /// Task to sync TubeArchivist playback progresses to Jellyfin.
    /// </summary>
    public class TAToJellyfinProgressSyncTask : IScheduledTask
    {
        private readonly ILogger<Plugin> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="TAToJellyfinProgressSyncTask"/> class.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        public TAToJellyfinProgressSyncTask(ILogger<Plugin> logger, ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;
        }

        /// <inheritdoc/>
        public string Name => "TAToJellyfinProgressSyncTask";

        /// <inheritdoc/>
        public string Description => "This tasks syncs TubeArchivist playback progresses to Jellyfin";

        /// <inheritdoc/>
        public string Category => "TubeArchivistMetadata";

        /// <inheritdoc/>
        public string Key => "TAToJellyfinProgressSyncTask";

        /// <inheritdoc/>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            progress.Report(0);
            if (Plugin.Instance!.Configuration.TAJFProgressSync)
            {
                var start = DateTime.Now;
                _logger.LogInformation("Starting TubeArchivist->Jellyfin playback progresses synchronization.");
                var taApi = TubeArchivistApi.GetInstance();
                var videosCount = 0;
                foreach (var jfUsername in Plugin.Instance!.Configuration.GetJFUsernamesToArray())
                {
                    var user = _userManager.GetUserByName(jfUsername);
                    if (user == null)
                    {
                        _logger.LogInformation("{Message}", $"Jellyfin user with username {jfUsername} not found");
                        continue;
                    }

                    var items = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        Name = Plugin.Instance?.Configuration.CollectionTitle,
                        IncludeItemTypes = new[] { BaseItemKind.CollectionFolder }
                    });

                    var collectionItem = items.Count > 0 ? items[0] : null;

                    if (collectionItem == null)
                    {
                        var message = $"Collection '{Plugin.Instance?.Configuration.CollectionTitle}' not found.";
                        _logger.LogCritical("{Message}", message);
                    }
                    else
                    {
                        var collection = (CollectionFolder)collectionItem;
                        var channels = collection.GetChildren(user, false, new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { BaseItemKind.Series }
                        });
                        _logger.LogDebug("Analyzing collection {Id} with name {Name}", collectionItem.Id, collectionItem.Name);
                        _logger.LogDebug("Found {Message} channels", channels.Count);

                        foreach (Series channel in channels)
                        {
                            var years = channel.GetChildren(user, false, new InternalItemsQuery
                            {
                                IncludeItemTypes = new[] { BaseItemKind.Season }
                            });
                            _logger.LogDebug("Found {Years} years in channel {ChannelName}", years.Count, channel.Name);

                            foreach (Season year in years)
                            {
                                var videos = year.GetChildren(user, false, new InternalItemsQuery
                                {
                                    IncludeItemTypes = new[] { BaseItemKind.Episode }
                                });
                                _logger.LogDebug("Found {Videos} videos in year {YearName} of the channel {ChannelName}", videos.Count, year.Name, channel.Name);

                                videosCount += videos.Count;
                            }
                        }
                    }
                }

                _logger.LogDebug("Found a total of {VideosCount} videos", videosCount);

                var processedVideosCount = 0;
                foreach (var jfUsername in Plugin.Instance!.Configuration.GetJFUsernamesToArray())
                {
                    var user = _userManager.GetUserByName(jfUsername);
                    if (user == null)
                    {
                        _logger.LogDebug("{Message}", $"Jellyfin user with username {jfUsername} not found");
                        continue;
                    }

                    var items = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        Name = Plugin.Instance?.Configuration.CollectionTitle,
                        IncludeItemTypes = new[] { BaseItemKind.CollectionFolder }
                    });

                    var collectionItem = items.Count > 0 ? items[0] : null;

                    if (collectionItem == null)
                    {
                        var message = $"Collection '{Plugin.Instance?.Configuration.CollectionTitle}' not found.";
                        _logger.LogCritical("{Message}", message);
                    }
                    else
                    {
                        var collection = (CollectionFolder)collectionItem;
                        var channels = collection.GetChildren(user, false, new InternalItemsQuery
                        {
                            IncludeItemTypes = new[] { BaseItemKind.Series }
                        });

                        foreach (Series channel in channels)
                        {
                            var years = channel.GetChildren(user, false, new InternalItemsQuery
                            {
                                IncludeItemTypes = new[] { BaseItemKind.Season }
                            });

                            foreach (Season year in years)
                            {
                                var videos = year.GetChildren(user, false, new InternalItemsQuery
                                {
                                    IncludeItemTypes = new[] { BaseItemKind.Episode }
                                });

                                foreach (Episode video in videos)
                                {
                                    var playbackProgress = await taApi.GetProgress(Utils.GetVideoNameFromPath(video.Path)).ConfigureAwait(true);
                                    if (playbackProgress != null)
                                    {
                                        var userItemData = _userDataManager.GetUserData(user, video);
                                        var playbackPositionTicks = playbackProgress.Position * TimeSpan.TicksPerSecond;
                                        var taVideoInfo = await taApi.GetVideo(Utils.GetVideoNameFromPath(video.Path)).ConfigureAwait(true);
                                        var isProgressChanged = userItemData?.PlaybackPositionTicks != playbackPositionTicks;
                                        var isWatchedChanged = taVideoInfo != null && userItemData?.Played != taVideoInfo.Player.IsWatched;
                                        if (!isProgressChanged && !isWatchedChanged)
                                        {
                                            _logger.LogDebug(
                                                "Skipping unchanged TubeArchivist->Jellyfin user data for video {VideoName} and user {Username}",
                                                video.Name,
                                                jfUsername);
                                            processedVideosCount++;
                                            progress.Report(processedVideosCount * 100 / videosCount);
                                            continue;
                                        }

                                        var userUpdateData = new UpdateUserItemDataDto
                                        {
                                            PlaybackPositionTicks = playbackPositionTicks
                                        };

                                        if (taVideoInfo != null)
                                        {
                                            userUpdateData.Played = taVideoInfo.Player.IsWatched;
                                        }

                                        _userDataManager.SaveUserData(user, video, userUpdateData, UserDataSaveReason.UpdateUserData);
                                        _logger.LogInformation("Playback progress for video {VideoName} set to {Progress} seconds for user {Username}.", video.Name, playbackProgress.Position, jfUsername);
                                        _logger.LogInformation("Watched status for video {VideoName} set to {WatchedStatus} for user {Username}.", video.Name, taVideoInfo?.Player.IsWatched, jfUsername);

                                        processedVideosCount++;
                                        progress.Report(processedVideosCount * 100 / videosCount);
                                    }
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation("Time elapsed: {Time}", DateTime.Now - start);
            }
            else
            {
                _logger.LogInformation("TubeArchivist->Jellyfin playback synchronization is currently disabled.");
            }

            progress.Report(100);
        }

        /// <inheritdoc/>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromSeconds(Plugin.Instance!.Configuration.TAJFProgressTaskInterval).Ticks
                },
            ];
        }
    }
}
