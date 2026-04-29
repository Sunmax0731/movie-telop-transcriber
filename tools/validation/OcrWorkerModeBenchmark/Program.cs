using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

var repoRoot = ResolveRepoRoot();
var options = BenchmarkOptions.Parse(args, repoRoot);
ConfigurePaddleOcrEnvironment(options, repoRoot);

var videoService = new OpenCvVideoProcessingService();
Directory.CreateDirectory(options.BenchmarkRoot);

var metadata = await videoService.ReadMetadataAsync(options.VideoPath);
var extraction = await videoService.ExtractFramesAsync(metadata, 1.0d, null, options.BenchmarkRoot);
var targetFrames = extraction.Frames.Take(options.FrameCount).ToArray();
if (targetFrames.Length == 0)
{
    throw new InvalidOperationException("No frames were extracted for benchmark.");
}

var probe = await NvidaSmiProbe.TryCollectOnceAsync();
var results = new List<BenchmarkResult>();
foreach (var workerCount in options.WorkerCounts)
{
    results.Add(await BenchmarkAsync(options, targetFrames, workerCount));
}

var report = new BenchmarkReport(
    DateTimeOffset.Now,
    probe,
    new BenchmarkEnvironment(
        options.Device,
        Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON") ?? string.Empty,
        Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT") ?? string.Empty,
        targetFrames.Length,
        options.VideoPath,
        options.BenchmarkRoot),
    results);

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true
}));

static async Task<BenchmarkResult> BenchmarkAsync(
    BenchmarkOptions options,
    IReadOnlyList<ExtractedFrameRecord> frames,
    int workerCount)
{
    var clients = Enumerable.Range(0, workerCount)
        .Select(_ => new PaddleOcrWorkerClient())
        .ToArray();

    try
    {
        var benchmarkDirectory = Path.Combine(
            options.BenchmarkRoot,
            $"{workerCount:D2}-workers",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(benchmarkDirectory);

        await using var sampler = NvidaSmiSampler.IsAvailable()
            ? new NvidaSmiSampler(TimeSpan.FromMilliseconds(500))
            : null;

        if (sampler is not null)
        {
            await sampler.StartAsync();
        }

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

        if (sampler is not null)
        {
            await sampler.StopAsync();
        }

        foreach (var partition in partitions)
        {
            perFrameResults.AddRange(partition);
        }

        var ordered = perFrameResults.OrderBy(result => result.TimestampMs).ToArray();
        return new BenchmarkResult(
            workerCount,
            frames.Count,
            Math.Round(wallClock.Elapsed.TotalMilliseconds, 1),
            Math.Round(warmups.Sum(item => item.TotalMs), 1),
            Math.Round(ordered.Sum(item => item.TotalMs), 1),
            Math.Round(ordered.Sum(item => item.WorkerExecutionMs), 1),
            Math.Round(ordered.Average(item => item.TotalMs), 1),
            Math.Round(ordered.Max(item => item.TotalMs), 1),
            ordered.Count(item => string.Equals(item.Status, "success", StringComparison.OrdinalIgnoreCase)),
            ordered.Count(item => string.Equals(item.Status, "error", StringComparison.OrdinalIgnoreCase)),
            sampler?.BuildSummary());
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
            $"issue158-{workerIndex + 1:D2}-{frame.FrameIndex:D6}-{frame.TimestampMs:D8}ms",
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

static void ConfigurePaddleOcrEnvironment(BenchmarkOptions options, string repoRoot)
{
    Environment.SetEnvironmentVariable("MOVIE_TELOP_OCR_ENGINE", "paddleocr");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON", options.PythonPath);
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT", Path.Combine(repoRoot, "tools", "ocr", "paddle_ocr_worker.py"));
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE", options.Device);
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_LANG", "ja");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_MIN_SCORE", "0.5");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PREPROCESS", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_CONTRAST", "1.1");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SHARPEN", "true");
}

sealed record BenchmarkOptions(
    string PythonPath,
    string Device,
    string VideoPath,
    int FrameCount,
    string BenchmarkRoot,
    IReadOnlyList<int> WorkerCounts)
{
    public static BenchmarkOptions Parse(string[] args, string repoRoot)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            values[key] = value;
        }

        var pythonPath = values.TryGetValue("python", out var configuredPython)
            ? configuredPython
            : Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON");
        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            pythonPath = Path.Combine(repoRoot, "temp", "ocr-eval-gpu", ".venv", "Scripts", "python.exe");
        }

        var device = values.TryGetValue("device", out var configuredDevice)
            ? configuredDevice
            : Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE");
        if (string.IsNullOrWhiteSpace(device))
        {
            device = "gpu:0";
        }

        var videoPath = values.TryGetValue("video", out var configuredVideo)
            ? configuredVideo
            : Path.Combine(repoRoot, "test-data", "benchmark_suite", "sample_basic_telop_60s.mp4");

        var frameCount = values.TryGetValue("frames", out var configuredFrames)
            && int.TryParse(configuredFrames, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFrames)
            && parsedFrames > 0
            ? parsedFrames
            : 60;

        var benchmarkRoot = values.TryGetValue("output", out var configuredOutput)
            ? configuredOutput
            : Path.Combine(repoRoot, "temp", "issue158-gpu-worker-bench");

        var workerCounts = values.TryGetValue("workers", out var configuredWorkers)
            ? configuredWorkers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
                .Where(value => value > 0)
                .Distinct()
                .Order()
                .ToArray()
            : [1, 2];

        return new BenchmarkOptions(
            Path.GetFullPath(pythonPath),
            device,
            Path.GetFullPath(videoPath),
            frameCount,
            Path.GetFullPath(benchmarkRoot),
            workerCounts);
    }
}

sealed record BenchmarkReport(
    DateTimeOffset MeasuredAt,
    NvidaSmiProbe? Probe,
    BenchmarkEnvironment Environment,
    IReadOnlyList<BenchmarkResult> Results);

sealed record BenchmarkEnvironment(
    string Device,
    string PythonPath,
    string WorkerScriptPath,
    int FrameCount,
    string VideoPath,
    string BenchmarkRoot);

sealed record BenchmarkResult(
    int WorkerCount,
    int FrameCount,
    double WallClockMs,
    double WarmupTotalMs,
    double OcrTotalMs,
    double WorkerExecutionTotalMs,
    double AverageFrameMs,
    double MaxFrameMs,
    int SuccessCount,
    int ErrorCount,
    NvidaSmiSummary? GpuSummary);

sealed record FrameModeResult(
    int FrameIndex,
    long TimestampMs,
    string Status,
    double TotalMs,
    double WorkerExecutionMs);

sealed record NvidaSmiProbe(
    string DriverVersion,
    string CudaVersion,
    IReadOnlyList<NvidaSmiGpuInfo> Gpus)
{
    public static async Task<NvidaSmiProbe?> TryCollectOnceAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--query-gpu=name,memory.total");
        startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var gpus = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                return new NvidaSmiGpuInfo(
                    parts.ElementAtOrDefault(0) ?? string.Empty,
                    BenchmarkParsing.ParseDouble(parts.ElementAtOrDefault(1)));
            })
            .ToArray();

        var banner = await ReadNvidiaSmiBannerAsync();
        return new NvidaSmiProbe(banner.DriverVersion, banner.CudaVersion, gpus);
    }

    private static async Task<(string DriverVersion, string CudaVersion)> ReadNvidiaSmiBannerAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (string.Empty, string.Empty);
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var line = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(text => text.Contains("Driver Version:", StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return (string.Empty, string.Empty);
        }

        var driver = ExtractBannerValue(line, "Driver Version:");
        var cuda = ExtractBannerValue(line, "CUDA Version:");
        return (driver, cuda);
    }

    private static string ExtractBannerValue(string line, string label)
    {
        var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var value = line[(index + label.Length)..];
        var nextSeparator = value.IndexOf("  ", StringComparison.Ordinal);
        if (nextSeparator >= 0)
        {
            value = value[..nextSeparator];
        }

        return value.Trim();
    }
}

sealed record NvidaSmiGpuInfo(
    string Name,
    double? MemoryTotalMiB);

sealed record NvidaSmiSample(
    DateTimeOffset Timestamp,
    double? UtilizationPercent,
    double? MemoryUsedMiB,
    double? MemoryTotalMiB);

sealed record NvidaSmiSummary(
    int SampleCount,
    double? MaxUtilizationPercent,
    double? AverageUtilizationPercent,
    double? MaxMemoryUsedMiB,
    double? AverageMemoryUsedMiB,
    double? MemoryTotalMiB,
    IReadOnlyList<NvidaSmiSample> Samples);

sealed class NvidaSmiSampler : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly List<NvidaSmiSample> _samples = [];
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;

    public NvidaSmiSampler(TimeSpan interval)
    {
        _interval = interval;
    }

    public static bool IsAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("nvidia-smi");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    public Task StartAsync()
    {
        _task = Task.Run(SampleLoopAsync);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_task is not null)
        {
            await _task;
        }
    }

    public NvidaSmiSummary BuildSummary()
    {
        var utilizationValues = _samples
            .Where(sample => sample.UtilizationPercent.HasValue)
            .Select(sample => sample.UtilizationPercent!.Value)
            .ToArray();
        var memoryValues = _samples
            .Where(sample => sample.MemoryUsedMiB.HasValue)
            .Select(sample => sample.MemoryUsedMiB!.Value)
            .ToArray();

        return new NvidaSmiSummary(
            _samples.Count,
            utilizationValues.Length > 0 ? Math.Round(utilizationValues.Max(), 1) : null,
            utilizationValues.Length > 0 ? Math.Round(utilizationValues.Average(), 1) : null,
            memoryValues.Length > 0 ? Math.Round(memoryValues.Max(), 1) : null,
            memoryValues.Length > 0 ? Math.Round(memoryValues.Average(), 1) : null,
            _samples.LastOrDefault(sample => sample.MemoryTotalMiB.HasValue)?.MemoryTotalMiB,
            _samples.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    private async Task SampleLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var sample = await CollectSampleAsync();
                if (sample is not null)
                {
                    _samples.Add(sample);
                }
            }
            catch
            {
            }

            try
            {
                await Task.Delay(_interval, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<NvidaSmiSample?> CollectSampleAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--query-gpu=utilization.gpu,memory.used,memory.total");
        startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var firstLine = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (firstLine is null)
        {
            return null;
        }

        var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
        return new NvidaSmiSample(
            DateTimeOffset.Now,
            BenchmarkParsing.ParseDouble(parts.ElementAtOrDefault(0)),
            BenchmarkParsing.ParseDouble(parts.ElementAtOrDefault(1)),
            BenchmarkParsing.ParseDouble(parts.ElementAtOrDefault(2)));
    }
}

static class BenchmarkParsing
{
    public static double? ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
