using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovieTelopTranscriber.App.Models;
using MovieTelopTranscriber.App.Services;

var options = BenchmarkOptions.Parse(args);
var repoRoot = ResolveRepoRoot();
var catalogPath = Path.GetFullPath(Path.Combine(repoRoot, options.CatalogPath));
var outputJsonPath = options.OutputJsonPath is null
    ? null
    : Path.GetFullPath(Path.Combine(repoRoot, options.OutputJsonPath));

ConfigurePaddleOcrEnvironment(repoRoot);

await using var catalogStream = File.OpenRead(catalogPath);
var catalog = await JsonSerializer.DeserializeAsync(catalogStream, BenchmarkCatalogJsonContext.Default.BenchmarkCatalog)
    ?? throw new InvalidOperationException("Benchmark catalog could not be read.");

var videoService = new OpenCvVideoProcessingService();
var segmentMerger = new TelopSegmentMerger();
var exportWriter = new ExportPackageWriter();
var runLogWriter = new RunLogWriter();
var benchmarkRoot = Path.Combine(repoRoot, "temp", "ocr-performance-benchmark-runs");
Directory.CreateDirectory(benchmarkRoot);

var results = new List<BenchmarkResult>();
foreach (var sample in catalog.Samples)
{
    var frameAnalysisService = new TelopFrameAnalysisService(new PaddleOcrWorkerClient(), new TelopAttributeAnalysisService(), new OcrFrameCandidateSelector());
    var startedAt = DateTimeOffset.Now;
    var videoPath = Path.GetFullPath(Path.Combine(repoRoot, sample.VideoPath));
    var metadata = await videoService.ReadMetadataAsync(videoPath);

    var totalStopwatch = Stopwatch.StartNew();
    var warmupTask = StartBackgroundWarmupAsync(frameAnalysisService, repoRoot);

    var extractionStopwatch = Stopwatch.StartNew();
    var extractionResult = await videoService.ExtractFramesAsync(metadata, sample.FrameIntervalSeconds, null, benchmarkRoot);
    extractionStopwatch.Stop();

    var warmupStopwatch = Stopwatch.StartNew();
    var warmupResult = await warmupTask;
    warmupStopwatch.Stop();

    var ocrStopwatch = Stopwatch.StartNew();
    var analyses = await frameAnalysisService.AnalyzeFramesAsync(extractionResult);
    ocrStopwatch.Stop();
    totalStopwatch.Stop();

    var mergeStopwatch = Stopwatch.StartNew();
    var segments = segmentMerger.Merge(analyses, sample.FrameIntervalSeconds);
    mergeStopwatch.Stop();

    var detectionCount = analyses.Sum(analysis => analysis.Attributes.Detections.Count);
    var errorCount = analyses.Count(analysis => analysis.Ocr.Status == "error");
    var warningCount = analyses.Count(analysis => analysis.Ocr.Status == "warning");

    var exportStopwatch = Stopwatch.StartNew();
    var exportResult = await exportWriter.WriteAsync(
        metadata,
        extractionResult,
        analyses,
        segments,
        Array.Empty<EditOperationRecord>(),
        sample.FrameIntervalSeconds,
        frameAnalysisService.EngineName,
        ocrStopwatch.ElapsedMilliseconds,
        warningCount,
        errorCount);
    exportStopwatch.Stop();

    var performance = BuildPerformanceSummary(
        warmupResult,
        analyses,
        extractionStopwatch.Elapsed.TotalMilliseconds,
        ocrStopwatch.Elapsed.TotalMilliseconds,
        mergeStopwatch.Elapsed.TotalMilliseconds,
        exportStopwatch.Elapsed.TotalMilliseconds,
        0d);

    var logStopwatch = Stopwatch.StartNew();
    var logResult = await runLogWriter.WriteSuccessAsync(
        extractionResult,
        metadata,
        exportResult,
        analyses,
        sample.FrameIntervalSeconds,
        frameAnalysisService.EngineName,
        startedAt,
        DateTimeOffset.Now,
        extractionResult.Frames.Count,
        detectionCount,
        segments.Count,
        warningCount,
        errorCount,
        performance);
    logStopwatch.Stop();

    results.Add(new BenchmarkResult(
        sample.SampleId,
        sample.Category,
        sample.VideoPath,
        metadata.DurationMs,
        extractionResult.Frames.Count,
        detectionCount,
        segments.Count,
        Math.Round(extractionStopwatch.Elapsed.TotalMilliseconds, 1),
        Math.Round(ocrStopwatch.Elapsed.TotalMilliseconds, 1),
        Math.Round(mergeStopwatch.Elapsed.TotalMilliseconds, 1),
        Math.Round(exportStopwatch.Elapsed.TotalMilliseconds, 1),
        Math.Round(logStopwatch.Elapsed.TotalMilliseconds, 1),
        warmupResult.Status,
        Math.Round(warmupResult.TotalMs, 1),
        Math.Round(warmupStopwatch.Elapsed.TotalMilliseconds, 1),
        Math.Round(totalStopwatch.Elapsed.TotalMilliseconds, 1),
        performance.OcrExecutedFrameCount,
        performance.OcrReusedFrameCount,
        Math.Round(performance.OcrSelectionMs, 1),
        Math.Round(performance.OcrWorkerInitializationMs, 1),
        Math.Round(performance.OcrWorkerExecutionMs, 1),
        Math.Round(performance.AttributeAnalysisMs, 1),
        Math.Round(performance.AttributeWriteMs, 1),
        Math.Round(performance.OcrFirstFrameMs, 1),
        Math.Round(performance.OcrAverageFrameMs, 1),
        Math.Round(performance.OcrMaxFrameMs, 1),
        logResult.SummaryPath,
        Path.Combine(logResult.LogsDirectory, "ocr-performance.json")));
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};

var outputJson = JsonSerializer.Serialize(results, jsonOptions);
if (!string.IsNullOrWhiteSpace(outputJsonPath))
{
    var outputDirectory = Path.GetDirectoryName(outputJsonPath);
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    await File.WriteAllTextAsync(outputJsonPath, outputJson);
}

Console.WriteLine(outputJson);

static Task<OcrWorkerWarmupResult> StartBackgroundWarmupAsync(TelopFrameAnalysisService frameAnalysisService, string repoRoot)
{
    var workDirectory = Path.Combine(repoRoot, "temp", "ocr-performance-benchmark-warmup", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDirectory);
    return Task.Run(async () =>
    {
        try
        {
            return await frameAnalysisService.WarmupAsync(workDirectory);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    });
}

static string ResolveRepoRoot()
{
    var candidates = new[]
    {
        new DirectoryInfo(Directory.GetCurrentDirectory()),
        new DirectoryInfo(AppContext.BaseDirectory)
    };

    foreach (var candidate in candidates)
    {
        var current = candidate;
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "MovieTelopTranscriber.App"))
                && Directory.Exists(Path.Combine(current.FullName, "tools"))
                && Directory.Exists(Path.Combine(current.FullName, "test-data")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }
    }

    throw new InvalidOperationException("Repository root could not be resolved.");
}

static void ConfigurePaddleOcrEnvironment(string repoRoot)
{
    Environment.SetEnvironmentVariable("MOVIE_TELOP_OCR_ENGINE", "paddleocr");

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PYTHON")))
    {
        Environment.SetEnvironmentVariable(
            "MOVIE_TELOP_PADDLEOCR_PYTHON",
            Path.Combine(repoRoot, "temp", "ocr-eval", ".venv", "Scripts", "python.exe"));
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SCRIPT")))
    {
        Environment.SetEnvironmentVariable(
            "MOVIE_TELOP_PADDLEOCR_SCRIPT",
            Path.Combine(repoRoot, "tools", "ocr", "paddle_ocr_worker.py"));
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE")))
    {
        Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_DEVICE", "cpu");
    }

    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_LANG", "ja");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_MIN_SCORE", "0.5");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_NORMALIZE_SMALL_KANA", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_PREPROCESS", "true");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_CONTRAST", "1.1");
    Environment.SetEnvironmentVariable("MOVIE_TELOP_PADDLEOCR_SHARPEN", "true");
}

static RunPerformanceSummaryRecord BuildPerformanceSummary(
    OcrWorkerWarmupResult warmupResult,
    IReadOnlyList<FrameAnalysisResult> frameAnalyses,
    double frameExtractionDurationMs,
    double ocrDurationMs,
    double segmentMergeDurationMs,
    double exportWriteDurationMs,
    double logWriteDurationMs)
{
    var framePerformances = frameAnalyses.Select(analysis => analysis.Performance).ToArray();
    var firstFrameMs = framePerformances.FirstOrDefault()?.TotalMs ?? 0d;
    var averageFrameMs = framePerformances.Length == 0
        ? 0d
        : framePerformances.Average(performance => performance.TotalMs);
    var maxFrameMs = framePerformances.Length == 0
        ? 0d
        : framePerformances.Max(performance => performance.TotalMs);

    return new RunPerformanceSummaryRecord(
        warmupResult.Status,
        frameExtractionDurationMs,
        ocrDurationMs,
        segmentMergeDurationMs,
        exportWriteDurationMs,
        logWriteDurationMs,
        warmupResult.TotalMs,
        framePerformances.Count(performance => performance.OcrExecuted),
        framePerformances.Count(performance => !performance.OcrExecuted),
        framePerformances.Sum(performance => performance.SelectionMs),
        framePerformances.Sum(performance => performance.RequestWriteMs),
        framePerformances.Sum(performance => performance.WorkerInitializationMs),
        framePerformances.Sum(performance => performance.WorkerExecutionMs),
        framePerformances.Sum(performance => performance.ResponseReadMs),
        framePerformances.Sum(performance => performance.AttributeAnalysisMs),
        framePerformances.Sum(performance => performance.AttributeWriteMs),
        firstFrameMs,
        averageFrameMs,
        maxFrameMs);
}

sealed record BenchmarkOptions(string CatalogPath, string? OutputJsonPath)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var catalogPath = Path.Combine("test-data", "benchmark_suite", "benchmark_samples.json");
        string? outputJsonPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--catalog":
                    catalogPath = args[++index];
                    break;
                case "--output-json":
                    outputJsonPath = args[++index];
                    break;
            }
        }

        return new BenchmarkOptions(catalogPath, outputJsonPath);
    }
}

public sealed record BenchmarkCatalog(string SchemaVersion, string SuiteId, string GeneratedFrom, IReadOnlyList<BenchmarkSample> Samples);

public sealed record BenchmarkSample(
    string SampleId,
    string Category,
    string VideoPath,
    double DurationSeconds,
    double FrameIntervalSeconds,
    bool Redistributable,
    string Description,
    string? GeneratedFrom = null,
    int? RepeatCount = null);

public sealed record BenchmarkResult(
    string SampleId,
    string Category,
    string VideoPath,
    long DurationMs,
    int FrameCount,
    int DetectionCount,
    int SegmentCount,
    double FrameExtractionMs,
    double OcrTotalMs,
    double SegmentMergeMs,
    double ExportWriteMs,
    double LogWriteMs,
    string OcrWarmupStatus,
    double OcrWarmupMs,
    double WarmupStageMs,
    double TotalWaitMs,
    int OcrExecutedFrameCount,
    int OcrReusedFrameCount,
    double OcrSelectionMs,
    double OcrWorkerInitializationMs,
    double OcrWorkerExecutionMs,
    double AttributeAnalysisMs,
    double AttributeWriteMs,
    double OcrFirstFrameMs,
    double OcrAverageFrameMs,
    double OcrMaxFrameMs,
    string SummaryPath,
    string PerformancePath);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(BenchmarkCatalog))]
internal sealed partial class BenchmarkCatalogJsonContext : JsonSerializerContext
{
}
