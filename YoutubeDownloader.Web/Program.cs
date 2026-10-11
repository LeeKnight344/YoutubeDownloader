using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using YoutubeDownloader.Core.Downloading;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/download", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    var url = form["url"].ToString();
    var format = form["format"].ToString().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(url))
        return Results.Content("Enter a YouTube video URL.", "text/plain", statusCode: 400);

    if (format is not ("mp3" or "mp4"))
        return Results.Content("Choose MP3 or MP4.", "text/plain", statusCode: 400);

    var container = format == "mp3" ? Container.Mp3 : Container.Mp4;

    VideoId videoId;
    try
    {
        videoId = VideoId.Parse(url);
    }
    catch (Exception exception) when (exception is FormatException or ArgumentException)
    {
        return Results.Content("Enter a valid YouTube video URL.", "text/plain", statusCode: 400);
    }

    using var youtube = new YoutubeClient();
    var video = await youtube.Videos.GetAsync(videoId, cancellationToken);
    using var downloader = new VideoDownloader();
    var option = await downloader.GetBestDownloadOptionAsync(
        videoId,
        new VideoDownloadPreference(container, VideoQualityPreference.Highest),
        cancellationToken: cancellationToken
    );

    var outputDirectory = Path.Combine(Path.GetTempPath(), "youtube-downloader");
    Directory.CreateDirectory(outputDirectory);
    var outputPath = Path.Combine(outputDirectory, $"{video.Id}-{Guid.NewGuid():N}.{format}");

    try
    {
        await downloader.DownloadVideoAsync(
            outputPath,
            video,
            option,
            includeSubtitles: false,
            ffmpegPath: "ffmpeg",
            cancellationToken: cancellationToken
        );
    }
    catch
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        throw;
    }

    var safeTitle = new string(
        video.Title
            .Select(character =>
                char.IsControl(character)
                || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
                    ? '_'
                    : character
            )
            .ToArray()
    );
    if (safeTitle.Length > 120)
        safeTitle = safeTitle[..120];
    if (string.IsNullOrWhiteSpace(safeTitle))
        safeTitle = "video";

    var fileStream = new FileStream(
        outputPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        81920,
        FileOptions.DeleteOnClose
    );

    return Results.File(
        fileStream,
        format == "mp3" ? "audio/mpeg" : "video/mp4",
        fileDownloadName: $"{safeTitle}.{format}"
    );
});

app.Run();