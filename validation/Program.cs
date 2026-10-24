using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.TubeArchivistMetadata;
using Jellyfin.Plugin.TubeArchivistMetadata.Providers;
using Jellyfin.Plugin.TubeArchivistMetadata.TubeArchivist;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

static T Construct<T>(params object[] dependencies)
{
    // Exercise the production providers without starting a Jellyfin server or
    // exposing their dependency-injection constructors as public plugin API.
    return (T)Activator.CreateInstance(
        typeof(T), BindingFlags.Instance | BindingFlags.NonPublic,
        null, dependencies, null)!;
}

static TubeArchivistApi Api(StateHandler handler, string baseUrl = "http://archive.test")
{
    return Construct<TubeArchivistApi>(new HttpClient(handler), (Func<string>)(() => baseUrl), NullLogger.Instance);
}

Check(typeof(Plugin).Assembly.GetName().Version!.ToString() == "1.5.0.1", "assembly version 1.5.0.1");
Check(Constants.PluginName == "TubeArchivistMetadata", "plugin name matches catalog identity");

const string videoPath = "/media/youtube/UCmockChannel/testVideo01.mp4";
const string channelPath = "/media/youtube/UCmockChannel";
foreach (var (tags, label, expectedTags) in new (object? Tags, string Label, string[] ExpectedTags)[]
{
    (false, "false", []),
    (null, "null", []),
    (new[] { "science", "archive" }, "array", ["science", "archive"])
})
{
    var api = Api(new StateHandler { Tags = tags });
    var episodeProvider = Construct<EpisodeMetadataProvider>(api, NullLogger.Instance);
    var episode = await episodeProvider.GetMetadata(new EpisodeInfo { Path = videoPath }, CancellationToken.None);
    Check(episode.HasMetadata && episode.Provider == "TubeArchivist" && episode.Item.Name == "Real video title",
        $"video metadata loads with {label} tags");
    Check(episode.Item.Overview == "Video description<br>Second line" && episode.Item.SeriesName == "Real channel title",
        $"video description and channel title survive {label} tags");
    Check(episode.Item.ProductionYear == 2026 && episode.Item.SeasonName == "2026"
        && episode.Item.ParentIndexNumber == 2026 && episode.Item.PremiereDate == StateHandler.Published,
        $"video year, season, and publication date survive {label} tags");
    Check(episode.Item.Path == videoPath && episode.Item.ProviderIds[Constants.ProviderName] == "testVideo01"
        && episode.Item.Tags.SequenceEqual(expectedTags), $"video path, provider ID, and {label} tags are mapped");
    Check(episode.People.Single().Name == "Real channel title"
        && episode.People.Single().ImageUrl == "http://archive.test/cache/channels/thumb.jpg",
        $"video channel artwork is an absolute URL with {label} tags");

    var seriesProvider = Construct<SeriesMetadataProvider>(api, NullLogger.Instance);
    var series = await seriesProvider.GetMetadata(new SeriesInfo { Path = channelPath }, CancellationToken.None);
    Check(series.HasMetadata && series.Provider == "TubeArchivist" && series.Item.Name == "Real channel title",
        $"channel metadata loads with {label} tags");
    Check(series.Item.Overview == "Channel description<br>Second line"
        && series.Item.ProviderIds[Constants.ProviderName] == "UCmockChannel"
        && series.Item.Tags.SequenceEqual(expectedTags), $"channel description, provider ID, and {label} tags are mapped");
    Check(series.People.Single().ImageUrl == "http://archive.test/cache/channels/thumb.jpg",
        $"channel person artwork is an absolute URL with {label} tags");

    var episodeSearch = (await episodeProvider.GetSearchResults(new EpisodeInfo { Path = videoPath }, CancellationToken.None)).Single();
    Check(episodeSearch.Name == "Real video title" && episodeSearch.ProductionYear == 2026
        && episodeSearch.ProviderIds[Constants.ProviderName] == "testVideo01"
        && episodeSearch.ImageUrl == "http://archive.test/cache/videos/thumb.jpg",
        $"video search returns metadata and resolved artwork with {label} tags");
    var seriesSearch = (await seriesProvider.GetSearchResults(new SeriesInfo { Path = channelPath }, CancellationToken.None)).Single();
    Check(seriesSearch.Name == "Real channel title" && seriesSearch.ProviderIds[Constants.ProviderName] == "UCmockChannel"
        && seriesSearch.ImageUrl == "http://archive.test/cache/channels/thumb.jpg",
        $"channel search returns metadata and resolved artwork with {label} tags");
}

foreach (var absoluteImages in new[] { false, true })
{
    var api = Api(new StateHandler { AbsoluteImages = absoluteImages });
    var imagePrefix = absoluteImages ? "https://images.example.test/" : "http://archive.test/cache/";
    var episodeProvider = Construct<EpisodeImageProvider>(api, NullLogger.Instance);
    var episodeImages = (await episodeProvider.GetImages(new Episode { Path = videoPath }, CancellationToken.None)).ToArray();
    Check(episodeImages.Length == 1 && episodeImages[0].Type == ImageType.Primary
        && episodeImages[0].Url == imagePrefix + "videos/thumb.jpg",
        $"episode image provider handles {(absoluteImages ? "absolute" : "relative")} artwork URLs");

    var seriesProvider = Construct<SeriesImageProvider>(api, NullLogger.Instance);
    var series = new Series { Path = channelPath };
    var expectedImages = new Dictionary<ImageType, string>
    {
        [ImageType.Primary] = imagePrefix + "channels/thumb.jpg",
        [ImageType.Art] = imagePrefix + "channels/tvart.jpg",
        [ImageType.Banner] = imagePrefix + "channels/banner.jpg",
        [ImageType.Backdrop] = imagePrefix + "channels/tvart.jpg"
    };
    var seriesImages = (await seriesProvider.GetImages(series, CancellationToken.None)).ToArray();
    Check(seriesProvider.GetSupportedImages(series).ToHashSet().SetEquals(expectedImages.Keys),
        "series provider advertises Primary, Art, Banner, and Backdrop");
    Check(seriesImages.Length == expectedImages.Count
        && seriesImages.All(image => expectedImages.TryGetValue(image.Type, out var expected) && image.Url == expected),
        $"series image provider handles {(absoluteImages ? "absolute" : "relative")} artwork URLs");

    // Search/person artwork must follow the same URL rules as image providers.
    var metadataProvider = Construct<EpisodeMetadataProvider>(api, NullLogger.Instance);
    var metadata = await metadataProvider.GetMetadata(new EpisodeInfo { Path = videoPath }, CancellationToken.None);
    var search = (await metadataProvider.GetSearchResults(new EpisodeInfo { Path = videoPath }, CancellationToken.None)).Single();
    Check(metadata.People.Single().ImageUrl == imagePrefix + "channels/thumb.jpg"
        && search.ImageUrl == imagePrefix + "videos/thumb.jpg",
        $"metadata provider handles {(absoluteImages ? "absolute" : "relative")} artwork URLs");
}

var imageUrlMethod = typeof(TubeArchivistApi).GetMethod("BuildImageUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;
var prefixedApi = Api(new StateHandler(), "http://archive.test/tubearchivist");
foreach (var relativePath in new[] { "cache/thumb.jpg", "/cache/thumb.jpg" })
{
    var resolved = (Uri)imageUrlMethod.Invoke(prefixedApi, [relativePath])!;
    Check(resolved.AbsoluteUri == "http://archive.test/tubearchivist/cache/thumb.jpg",
        $"image path {relativePath} resolves under the configured base path without changing its filename");
}
foreach (var absoluteUrl in new[] { "http://images.example.test/thumb.jpg?size=large", "https://images.example.test/thumb.jpg" })
{
    var resolved = (Uri)imageUrlMethod.Invoke(prefixedApi, [absoluteUrl])!;
    Check(resolved.AbsoluteUri == absoluteUrl, $"absolute image URL is preserved: {absoluteUrl}");
}

// Image downloads must use TubeArchivist credentials only for its own origin.
// Distinct recording handlers make both client selection and headers observable.
using var authenticatedImageHandler = new ImageHandler();
using var anonymousImageHandler = new ImageHandler();
using var authenticatedImageClient = new HttpClient(authenticatedImageHandler);
using var anonymousImageClient = new HttpClient(anonymousImageHandler);
authenticatedImageClient.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Token", "secret-for-test");
var imageApi = Construct<TubeArchivistApi>(
    authenticatedImageClient, (Func<string>)(() => "http://archive.test/tubearchivist"),
    NullLogger.Instance, anonymousImageClient);
var imageDownloadProviders = new (string Name, Func<string, CancellationToken, Task<HttpResponseMessage>> Download)[]
{
    (nameof(EpisodeMetadataProvider), Construct<EpisodeMetadataProvider>(imageApi, NullLogger.Instance).GetImageResponse),
    (nameof(SeriesMetadataProvider), Construct<SeriesMetadataProvider>(imageApi, NullLogger.Instance).GetImageResponse),
    (nameof(EpisodeImageProvider), Construct<EpisodeImageProvider>(imageApi, NullLogger.Instance).GetImageResponse),
    (nameof(SeriesImageProvider), Construct<SeriesImageProvider>(imageApi, NullLogger.Instance).GetImageResponse)
};
foreach (var provider in imageDownloadProviders)
{
    foreach (var (imageUrl, expectedUrl, sameOrigin) in new (string Url, string ExpectedUrl, bool SameOrigin)[]
    {
        ("/cache/private.jpg", "http://archive.test/tubearchivist/cache/private.jpg", true),
        ("http://archive.test/cache/private.jpg", "http://archive.test/cache/private.jpg", true),
        ("https://images.example.test/public.jpg", "https://images.example.test/public.jpg", false),
        ("http://archive.test:8080/public.jpg", "http://archive.test:8080/public.jpg", false),
        ("https://archive.test/public.jpg", "https://archive.test/public.jpg", false)
    })
    {
        var authenticatedCount = authenticatedImageHandler.Requests.Count;
        var anonymousCount = anonymousImageHandler.Requests.Count;
        using var response = await provider.Download(imageUrl, CancellationToken.None);
        var expectedHandler = sameOrigin ? authenticatedImageHandler : anonymousImageHandler;
        var lastRequest = expectedHandler.Requests.LastOrDefault();
        Check(response.StatusCode == HttpStatusCode.OK
            && await response.Content.ReadAsStringAsync() == "mock-image"
            && authenticatedImageHandler.Requests.Count == authenticatedCount + (sameOrigin ? 1 : 0)
            && anonymousImageHandler.Requests.Count == anonymousCount + (sameOrigin ? 0 : 1)
            && lastRequest.Url == expectedUrl
            && lastRequest.Authorization == (sameOrigin ? "Token secret-for-test" : null),
            $"{provider.Name} downloads {imageUrl} with {(sameOrigin ? "TubeArchivist credentials" : "no authorization header")}");
    }
}

foreach (var initial in new[] { false, true })
{
    var handler = new StateHandler { Watched = initial };
    var api = Api(handler);
    var result = await api.SetWatchedStatus("testVideo01", initial);
    Check(result == HttpStatusCode.OK && handler.Posts == 0 && handler.WatchedDate == 100,
        $"unchanged watched={initial} makes no POST and preserves watched_date");
    result = await api.SetWatchedStatus("testVideo01", !initial);
    Check(result == HttpStatusCode.OK && handler.Posts == 1 && handler.Watched == !initial,
        $"watched transition {initial}->{!initial} is sent once");
    await api.SetWatchedStatus("testVideo01", !initial);
    Check(handler.Posts == 1 && handler.WatchedDate == 101, "repeated transition preserves the first watched_date");
}

var concurrent = new StateHandler { Watched = false, DelayReads = true };
var concurrentApi = Api(concurrent);
await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => concurrentApi.SetWatchedStatus("testVideo01", true)))
    .WaitAsync(TimeSpan.FromSeconds(5));
Check(concurrent.Posts == 1 && concurrent.WatchedDate == 101, "12 concurrent watched notifications produce one POST");

foreach (var failRead in new[] { true, false })
{
    var retry = new StateHandler { ThrowNextRead = failRead, ThrowNextPost = !failRead };
    var retryApi = Api(retry);
    try
    {
        await retryApi.SetWatchedStatus("testVideo01", true);
        throw new Exception("Expected mock connection failure");
    }
    catch (HttpRequestException)
    {
    }

    await retryApi.SetWatchedStatus("testVideo01", true).WaitAsync(TimeSpan.FromSeconds(5));
    Check(retry.Posts == 1, $"{(failRead ? "read" : "write")} failure releases the lock for the next watched update");
    var metadata = await retryApi.GetVideo("testVideo01");
    Check(metadata?.Title == "Real video title" && metadata.Player.IsWatched, "metadata still deserializes after a retried watched update");
}

// Excluded events must return before user/library services are consulted. Leave
// those dependencies unset so imported/progress state cannot be processed silently.
var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
typeof(Plugin).GetField("<Logger>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plugin, NullLogger<Plugin>.Instance);
var watchedCallback = typeof(Plugin).GetMethod("OnWatchedStatusChange", BindingFlags.Instance | BindingFlags.NonPublic)!;
foreach (var reason in Enum.GetValues<UserDataSaveReason>())
{
    if (reason is UserDataSaveReason.TogglePlayed or UserDataSaveReason.PlaybackFinished) continue;
    var eventArgs = new UserDataSaveEventArgs { Item = new Episode { Id = Guid.NewGuid(), Name = "Mock video" }, SaveReason = reason };
    watchedCallback.Invoke(plugin, [null, eventArgs]);
    Check(true, $"{reason} event is ignored before user lookup");
}

Console.WriteLine("All regression checks passed; no live service was contacted.");

sealed class ImageHandler : HttpMessageHandler
{
    public List<(string Url, string? Authorization)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get) throw new Exception("Unexpected image request method");
        Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("mock-image") });
    }
}

sealed class StateHandler : HttpMessageHandler
{
    public static readonly DateTime Published = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
    public object? Tags { get; set; } = false;
    public bool AbsoluteImages { get; set; }
    public bool Watched { get; set; }
    public int WatchedDate { get; private set; } = 100;
    public int Posts { get; private set; }
    public bool DelayReads { get; set; }
    public bool ThrowNextRead { get; set; }
    public bool ThrowNextPost { get; set; }

    private string Image(string path) => AbsoluteImages ? "https://images.example.test/" + path : "/cache/" + path;

    private object Channel() => new
    {
        channel_id = "UCmockChannel", channel_name = "Real channel title",
        channel_description = "Channel description\nSecond line", channel_tags = Tags,
        channel_thumb_url = Image("channels/thumb.jpg"), channel_banner_url = Image("channels/banner.jpg"),
        channel_tvart_url = Image("channels/tvart.jpg")
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host != "archive.test") throw new Exception("Unexpected host; all requests must use the mock service");
        if (request.Method == HttpMethod.Get)
        {
            if (ThrowNextRead) { ThrowNextRead = false; throw new HttpRequestException("Mock read failure"); }
            if (DelayReads) await Task.Delay(5, cancellationToken);
            object response = request.RequestUri.AbsolutePath switch
            {
                "/api/video/testVideo01/" => new
                {
                    youtube_id = "testVideo01", title = "Real video title", description = "Video description\nSecond line",
                    published = Published, tags = Tags, channel = Channel(), vid_thumb_url = Image("videos/thumb.jpg"),
                    player = new { watched = Watched, watched_date = WatchedDate, duration = 120, position = 0 }
                },
                "/api/channel/UCmockChannel/" => Channel(),
                _ => throw new Exception("Unexpected API read: " + request.RequestUri.AbsolutePath)
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            };
        }

        if (request.Method != HttpMethod.Post || request.RequestUri.AbsolutePath != "/api/watched/")
            throw new Exception("Unexpected API mutation");
        if (ThrowNextPost) { ThrowNextPost = false; throw new HttpRequestException("Mock write failure"); }
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (body.RootElement.GetProperty("id").GetString() != "testVideo01") throw new Exception("Unexpected watched video ID");
        Watched = body.RootElement.GetProperty("is_watched").GetBoolean();
        Posts++;
        WatchedDate++;
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
