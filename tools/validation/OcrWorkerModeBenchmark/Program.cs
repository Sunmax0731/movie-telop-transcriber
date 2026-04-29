using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using System.Diagnostics;
using System.Text.Json;

var repoRoot = ResolveRepoRoot();
ConfigurePaddleOcrEnvironment(repoRoot);

var videoService = new OpenCvVideoProcessingService();
var benchmarkRoot = Path.Combine(repoRoot, "temp", "issue152-benchmark-runs");
Directory.CreateDirectory(benchmarkRoot);

var videoPath = Path.Combine(repoRoot, "test-data", "basic_telop", "botirist.mp4");
var metadata = await videoService.ReadMetadataAsync(videoPath);
var extraction = await videoService.ExtractFramesAsync(metadata, 1.0d, null, benchmarkRoot);
var targetFrames = extraction.Frames.Take(40).ToArray();

var results = new List<BenchmarkResult>
{
    await BenchmarkAsync("single_worker_serial", targetFrames, workerCount: 1),
    await BenchmarkAsync("two_workers_parallel", targetFrames, workerCount: 2),
    await BenchmarkAsync("three_workers_parallel", targetFrames, workerCount: 3)
};

Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions
{
    WriteIndented = true
}));

static async Task<BenchmarkResult> BenchmarkAsync(
    string modeName,
    IReadOnlyList<ExtractedFrameRecord> frames,
    int workerCount)
{
    var clients = Enumerable.Range(0, workerCount)
        .Select(_ => new PaddleOcrWorkerClient())
        .ToArray();

    try
    {
        var benchmarkDirectory = Path.Combine(
            ResolveRepoRoot(),
            "temp",
            "issue152-worker-bench",
            modeName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(benchmarkDirectory);

        var warmupTasks = clients
            .Select((client, index) => client.WarmupAsync(Path.Combine(benchmarkDirectory, $"worker-{index + 1:D2}")))
            .ToArray();
        var warmups = await Task.WhenAll(warmupTasks);

        var perFrameResults = new List<FrameModeResult>();
        var wallClock = Stopwatch.StartNew();
        var workerTasks = Enumerable.Range(0, workerCount)
            .Select(workerIndex => RunWorkerPartitionAsync(
                clients[workerIndex],
                workerIndex,
                workerCount,
                frames,
                benchmarkDirectory))
            .ToArray();
        var partitions = await Task.WhenAll(workerTasks);
        wallClock.Stop();

        foreach (var partition in partitions)
        {
            perFrameResults.AddRange(partition);
        }

        var ordered = perFrameResults.OrderBy(result => result.TimestampMs).ToArray();
        return new BenchmarkResult(
            modeName,
            workerCount,
            frames.Count,
            Math.Round(wallClock.Elapsed.TotalMilliseconds, 1),
            Math.Round(warmups.Sum(item => item.TotalMs), 1),
            Math.Round(ordered.Sum(item => item.TotalMs), 1),
            Math.Round(ordered.Sum(item => item.WorkerExecutionMs), 1),
            Math.Round(ordered.Average(item => item.TotalMs), 1),
            Math.Round(ordered.Max(item => item.TotalMs), 1),
            ordered.Count(item => string.Equals(item.Status, "success", StringComparison.OrdinalIgnoreCase)),
            ordered.Count(item => string.Equals(item.Status, "error", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }
}

static async Task<IReadOnlyList<FrameModeResult>> RunWorkerPartitionAsync(
    PaddleOcrWorkerClient client,
    int workerIndex,
    int workerCount,
    IReadOnlyList<ExtractedFrameRecord> frames,
    string benchmarkDirectory)
{
    var results = new List<FrameModeResult>();
    var workerDirectory = Path.Combine(benchmarkDirectory, $"worker-{workerIndex + 1:D2}");

    for (var frameIndex = workerIndex; frameIndex < frames.Count; frameIndex += workerCount)
    {
        var frame = frames[frameIndex];
        var request = new OcrWorkerRequest(
            $"issue152-{workerIndex + 1:D2}-{frame.FrameIndex:D6}-{frame.TimestampMs:D8}ms",
            frame.FrameIndex,
            frame.TimestampMs,
            frame.ImagePath,
            "ja",
            client.EngineName);

        var startedAt = Stopwatch.StartNew();
        var response = await client.RecognizeAsync(request, workerDirectory);
        startedAt.Stop();
        results.Add(new FrameModeResult(
            frame.FrameIndex,
            frame.TimestampMs,
            response.Response.Status,
            Math.Round(startedAt.Elapsed.TotalMilliseconds, 1),
            Math.Round(response.WorkerExecutionMs, 1)));
    }

    return results;
}

static string ResolveRepoRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "src"))
            && Directory.Exists(Path.Combine(current.FullName, "tools")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Repository root could not be resolved.");
}

static void ConfigurePaddleOcrEnvironment(string repoRoot)
{
    Environment.SetEnvironmentVariable("MOVIE_TELOP_OCR_ENGINE", "paddleocr");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON", Path.Combine(repoRoot, "temp", "ocr-eval", ".venv", "Scripts", "python.exe"));
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT", Path.Combine(repoRoot, "tools", "ocr", "paddle_ocr_worker.py"));
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE", "cpu");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_LANG", "ja");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_MIN_SCORE", "0.5");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PREPROCESS", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_CONTRAST", "1.1");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SHARPEN", "true");
}

sealed record FrameModeResult(
    int FrameIndex,
    long TimestampMs,
    string Status,
    double TotalMs,
    double WorkerExecutionMs);

sealed record BenchmarkResult(
    string ModeName,
    int WorkerCount,
    int FrameCount,
    double WallClockMs,
    double WarmupTotalMs,
    double OcrTotalMs,
    double WorkerExecutionTotalMs,
    double AverageFrameMs,
    double MaxFrameMs,
    int SuccessCount,
    int ErrorCount);
