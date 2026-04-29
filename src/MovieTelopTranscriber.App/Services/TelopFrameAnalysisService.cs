using MovieTelopTranscriber.App.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopFrameAnalysisService
{
    private const string PaddleDeviceEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_DEVICE";
    private const string PaddleWorkerCountEnvironmentVariable = "MOVIE_TELOP_PADDLEOCR_WORKER_COUNT";
    private readonly IOcrWorkerClient _ocrWorkerClient;
    private readonly TelopAttributeAnalysisService _attributeAnalysisService;
    private readonly OcrFrameCandidateSelector _frameCandidateSelector;

    public TelopFrameAnalysisService()
        : this(OcrWorkerClientFactory.Create(), new TelopAttributeAnalysisService(), new OcrFrameCandidateSelector())
    {
    }

    public TelopFrameAnalysisService(
        IOcrWorkerClient ocrWorkerClient,
        TelopAttributeAnalysisService attributeAnalysisService,
        OcrFrameCandidateSelector frameCandidateSelector)
    {
        _ocrWorkerClient = ocrWorkerClient;
        _attributeAnalysisService = attributeAnalysisService;
        _frameCandidateSelector = frameCandidateSelector;
    }

    public string EngineName => _ocrWorkerClient.EngineName;

    public int ConfiguredWorkerCount => ResolveConfiguredWorkerCount();

    public Task<OcrWorkerWarmupResult> WarmupAsync(
        FrameExtractionResult frameExtractionResult,
        CancellationToken cancellationToken = default)
    {
        var ocrDirectory = Path.Combine(frameExtractionResult.RunDirectory, "ocr");
        return WarmupAsync(ocrDirectory, cancellationToken);
    }

    public Task<OcrWorkerWarmupResult> WarmupAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var ocrDirectory = Path.Combine(workDirectory, "ocr");
        return ResolveConfiguredWorkerCount() <= 1 || !string.Equals(EngineName, "paddleocr", StringComparison.OrdinalIgnoreCase)
            ? _ocrWorkerClient.WarmupAsync(ocrDirectory, cancellationToken)
            : WarmupParallelAsync(ocrDirectory, cancellationToken);
    }

    public async Task<IReadOnlyList<FrameAnalysisResult>> AnalyzeFramesAsync(
        FrameExtractionResult frameExtractionResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workerCount = ResolveConfiguredWorkerCount();
        if (workerCount <= 1 || !string.Equals(EngineName, "paddleocr", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzeFramesSerialAsync(frameExtractionResult, progress, cancellationToken);
        }

        return await AnalyzeFramesParallelAsync(frameExtractionResult, workerCount, progress, cancellationToken);
    }

    private async Task<IReadOnlyList<FrameAnalysisResult>> AnalyzeFramesSerialAsync(
        FrameExtractionResult frameExtractionResult,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ocrDirectory = Path.Combine(frameExtractionResult.RunDirectory, "ocr");
        var attributesDirectory = Path.Combine(frameExtractionResult.RunDirectory, "attributes");
        Directory.CreateDirectory(attributesDirectory);
        var results = new List<FrameAnalysisResult>(frameExtractionResult.Frames.Count);
        if (frameExtractionResult.Frames.Count == 0)
        {
            progress?.Report(100d);
            return results;
        }

        FrameAnalysisResult? previousAnalysis = null;
        ExtractedFrameRecord? previousFrame = null;
        var consecutiveSkippedFrames = 0;
        for (var i = 0; i < frameExtractionResult.Frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = frameExtractionResult.Frames[i];
            var request = new OcrWorkerRequest(
                CreateRequestId(frame),
                frame.FrameIndex,
                frame.TimestampMs,
                frame.ImagePath,
                "ja",
                _ocrWorkerClient.EngineName);

            var selectionDecision = _frameCandidateSelector.Decide(
                frame,
                previousFrame,
                previousAnalysis,
                consecutiveSkippedFrames);

            FrameAnalysisResult currentAnalysis;
            if (selectionDecision.ShouldRunOcr || previousAnalysis is null)
            {
                currentAnalysis = await ExecuteOcrAnalysisAsync(
                    _ocrWorkerClient,
                    frame,
                    request,
                    selectionDecision,
                    ocrDirectory,
                    attributesDirectory,
                    cancellationToken);
                consecutiveSkippedFrames = 0;
            }
            else
            {
                currentAnalysis = ReusePreviousAnalysis(previousAnalysis, frame, request.RequestId, selectionDecision, attributesDirectory);
                consecutiveSkippedFrames++;
            }

            results.Add(currentAnalysis);
            previousAnalysis = currentAnalysis;
            previousFrame = frame;

            progress?.Report(((double)(i + 1) / frameExtractionResult.Frames.Count) * 100d);
        }

        return results;
    }

    private async Task<IReadOnlyList<FrameAnalysisResult>> AnalyzeFramesParallelAsync(
        FrameExtractionResult frameExtractionResult,
        int workerCount,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ocrDirectory = Path.Combine(frameExtractionResult.RunDirectory, "ocr");
        var attributesDirectory = Path.Combine(frameExtractionResult.RunDirectory, "attributes");
        Directory.CreateDirectory(attributesDirectory);

        var results = new List<FrameAnalysisResult>(frameExtractionResult.Frames.Count);
        if (frameExtractionResult.Frames.Count == 0)
        {
            progress?.Report(100d);
            return results;
        }

        var plans = BuildAnalysisPlans(frameExtractionResult.Frames);
        var executedPlans = plans.Where(plan => plan.ShouldRunOcr).ToArray();
        var executedResults = new Dictionary<int, FrameAnalysisResult>();
        var completedCount = 0;

        await using var clients = new AsyncDisposableCollection<PaddleOcrWorkerClient>(
            Enumerable.Range(0, workerCount)
                .Select(_ => new PaddleOcrWorkerClient())
                .ToArray());

        var partitions = executedPlans
            .Select((plan, index) => new { plan, index })
            .GroupBy(item => item.index % workerCount, item => item.plan)
            .Select(group => RunPartitionAsync(group.Key, group.ToArray()))
            .ToArray();
        await Task.WhenAll(partitions);

        FrameAnalysisResult? previousAnalysis = null;
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FrameAnalysisResult currentAnalysis;
            if (executedResults.TryGetValue(plan.Index, out var executedAnalysis))
            {
                currentAnalysis = executedAnalysis;
            }
            else if (previousAnalysis is null || string.Equals(previousAnalysis.Ocr.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                currentAnalysis = await ExecuteOcrAnalysisAsync(
                    clients.Items[0],
                    plan.Frame,
                    plan.Request,
                    plan.SelectionDecision with { Reason = "previous_error_retry" },
                    ocrDirectory,
                    attributesDirectory,
                    cancellationToken);
            }
            else
            {
                currentAnalysis = ReusePreviousAnalysis(
                    previousAnalysis,
                    plan.Frame,
                    plan.Request.RequestId,
                    plan.SelectionDecision,
                    attributesDirectory);
            }

            results.Add(currentAnalysis);
            previousAnalysis = currentAnalysis;
            progress?.Report(((double)Interlocked.Increment(ref completedCount) / frameExtractionResult.Frames.Count) * 100d);
        }

        return results;

        async Task RunPartitionAsync(int workerIndex, IReadOnlyList<FrameAnalysisPlan> partitionPlans)
        {
            var client = clients.Items[workerIndex];
            foreach (var plan in partitionPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var analysis = await ExecuteOcrAnalysisAsync(
                    client,
                    plan.Frame,
                    plan.Request,
                    plan.SelectionDecision,
                    ocrDirectory,
                    attributesDirectory,
                    cancellationToken);
                lock (executedResults)
                {
                    executedResults[plan.Index] = analysis;
                }
            }
        }
    }

    private async Task<FrameAnalysisResult> ExecuteOcrAnalysisAsync(
        IOcrWorkerClient client,
        ExtractedFrameRecord frame,
        OcrWorkerRequest request,
        OcrFrameSelectionDecision selectionDecision,
        string ocrDirectory,
        string attributesDirectory,
        CancellationToken cancellationToken)
    {
        var frameStopwatch = Stopwatch.StartNew();
        var ocrResult = await client.RecognizeAsync(request, ocrDirectory, cancellationToken);

        var attributeAnalysisStopwatch = Stopwatch.StartNew();
        var attributeResult = _attributeAnalysisService.Analyze(ocrResult.Response, frame.ImagePath);
        attributeAnalysisStopwatch.Stop();

        var attributeWriteStopwatch = Stopwatch.StartNew();
        await WriteAttributeResultAsync(attributesDirectory, request.RequestId, attributeResult, cancellationToken);
        attributeWriteStopwatch.Stop();

        frameStopwatch.Stop();
        var performance = new OcrFramePerformanceRecord(
            frame.FrameIndex,
            frame.TimestampMs,
            true,
            selectionDecision.Reason,
            selectionDecision.SelectionMs,
            selectionDecision.RoiDifferenceMean,
            ocrResult.RequestWriteMs,
            ocrResult.WorkerInitializationMs,
            ocrResult.WorkerExecutionMs,
            ocrResult.ResponseReadMs,
            attributeAnalysisStopwatch.Elapsed.TotalMilliseconds,
            attributeWriteStopwatch.Elapsed.TotalMilliseconds,
            frameStopwatch.Elapsed.TotalMilliseconds);
        return new FrameAnalysisResult(frame, ocrResult.Response, attributeResult, performance);
    }

    private IReadOnlyList<FrameAnalysisPlan> BuildAnalysisPlans(IReadOnlyList<ExtractedFrameRecord> frames)
    {
        var plans = new List<FrameAnalysisPlan>(frames.Count);
        var previousFrame = default(ExtractedFrameRecord);
        var hasPreviousAnalysis = false;
        var previousOcrHadError = false;
        var consecutiveSkippedFrames = 0;

        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var request = new OcrWorkerRequest(
                CreateRequestId(frame),
                frame.FrameIndex,
                frame.TimestampMs,
                frame.ImagePath,
                "ja",
                _ocrWorkerClient.EngineName);

            var selectionDecision = _frameCandidateSelector.Decide(
                frame,
                previousFrame,
                hasPreviousAnalysis,
                previousOcrHadError,
                consecutiveSkippedFrames);
            var shouldRunOcr = selectionDecision.ShouldRunOcr || !hasPreviousAnalysis;
            plans.Add(new FrameAnalysisPlan(index, frame, request, selectionDecision, shouldRunOcr));

            hasPreviousAnalysis = true;
            previousOcrHadError = false;
            previousFrame = frame;
            consecutiveSkippedFrames = shouldRunOcr ? 0 : consecutiveSkippedFrames + 1;
        }

        return plans;
    }

    private async Task<OcrWorkerWarmupResult> WarmupParallelAsync(
        string ocrDirectory,
        CancellationToken cancellationToken)
    {
        var workerCount = ResolveConfiguredWorkerCount();
        await using var clients = new AsyncDisposableCollection<PaddleOcrWorkerClient>(
            Enumerable.Range(0, workerCount)
                .Select(_ => new PaddleOcrWorkerClient())
                .ToArray());
        var tasks = clients.Items
            .Select((client, index) => client.WarmupAsync(Path.Combine(ocrDirectory, $"worker-{index + 1:D2}"), cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        return AggregateWarmupResults(results);
    }

    private static OcrWorkerWarmupResult AggregateWarmupResults(IReadOnlyList<OcrWorkerWarmupResult> results)
    {
        if (results.Count == 0)
        {
            return OcrWorkerWarmupResult.Skipped;
        }

        var error = results.FirstOrDefault(result => !result.Succeeded)?.Error;
        var status = error is null ? "success" : "error";
        return new OcrWorkerWarmupResult(
            status,
            results.Sum(item => item.RequestWriteMs),
            results.Sum(item => item.WorkerInitializationMs),
            results.Sum(item => item.WorkerExecutionMs),
            results.Sum(item => item.ResponseReadMs),
            results.Sum(item => item.TotalMs),
            error);
    }

    private int ResolveConfiguredWorkerCount()
    {
        if (!string.Equals(EngineName, "paddleocr", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var device = Environment.GetEnvironmentVariable(PaddleDeviceEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(device)
            || !device.Trim().StartsWith("gpu", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var value = Environment.GetEnvironmentVariable(PaddleWorkerCountEnvironmentVariable);
        return int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, 1, 2)
            : 1;
    }

    private static async Task WriteAttributeResultAsync(
        string attributesDirectory,
        string requestId,
        AttributeAnalysisResult attributeResult,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(attributesDirectory, $"{requestId}.attributes.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, attributeResult, OcrContractJson.AttributeAnalysisResult, cancellationToken);
    }

    private static string CreateRequestId(ExtractedFrameRecord frame)
    {
        return $"ocr-{frame.FrameIndex:D6}-{frame.TimestampMs:D8}ms";
    }

    private static FrameAnalysisResult ReusePreviousAnalysis(
        FrameAnalysisResult previousAnalysis,
        ExtractedFrameRecord frame,
        string requestId,
        OcrFrameSelectionDecision selectionDecision,
        string attributesDirectory)
    {
        var remappedOcrDetections = previousAnalysis.Ocr.Detections
            .Select((detection, index) => detection with
            {
                DetectionId = $"{requestId}-reuse-{index + 1:D2}"
            })
            .ToArray();
        var remappedAttributeDetections = previousAnalysis.Attributes.Detections
            .Select((detection, index) => detection with
            {
                DetectionId = remappedOcrDetections[index].DetectionId
            })
            .ToArray();

        var ocrResponse = previousAnalysis.Ocr with
        {
            RequestId = requestId,
            FrameIndex = frame.FrameIndex,
            TimestampMs = frame.TimestampMs,
            Detections = remappedOcrDetections
        };
        var attributeResult = previousAnalysis.Attributes with
        {
            FrameIndex = frame.FrameIndex,
            TimestampMs = frame.TimestampMs,
            Detections = remappedAttributeDetections
        };

        Directory.CreateDirectory(attributesDirectory);
        var path = Path.Combine(attributesDirectory, $"{requestId}.attributes.json");
        File.WriteAllText(path, JsonSerializer.Serialize(attributeResult, OcrContractJson.AttributeAnalysisResult));

        var performance = new OcrFramePerformanceRecord(
            frame.FrameIndex,
            frame.TimestampMs,
            false,
            selectionDecision.Reason,
            selectionDecision.SelectionMs,
            selectionDecision.RoiDifferenceMean,
            0d,
            0d,
            0d,
            0d,
            0d,
            0d,
            selectionDecision.SelectionMs);
        return new FrameAnalysisResult(frame, ocrResponse, attributeResult, performance);
    }

    private sealed record FrameAnalysisPlan(
        int Index,
        ExtractedFrameRecord Frame,
        OcrWorkerRequest Request,
        OcrFrameSelectionDecision SelectionDecision,
        bool ShouldRunOcr);

    private sealed class AsyncDisposableCollection<T> : IAsyncDisposable
        where T : IAsyncDisposable
    {
        public AsyncDisposableCollection(IReadOnlyList<T> items)
        {
            Items = items;
        }

        public IReadOnlyList<T> Items { get; }

        public async ValueTask DisposeAsync()
        {
            foreach (var item in Items)
            {
                await item.DisposeAsync();
            }
        }
    }
}
