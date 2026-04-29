using MovieTelopTranscriber.App.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopFrameAnalysisService
{
    private readonly IOcrWorkerClient _ocrWorkerClient;
    private readonly TelopAttributeAnalysisService _attributeAnalysisService;

    public TelopFrameAnalysisService()
        : this(OcrWorkerClientFactory.Create(), new TelopAttributeAnalysisService())
    {
    }

    public TelopFrameAnalysisService(
        IOcrWorkerClient ocrWorkerClient,
        TelopAttributeAnalysisService attributeAnalysisService)
    {
        _ocrWorkerClient = ocrWorkerClient;
        _attributeAnalysisService = attributeAnalysisService;
    }

    public string EngineName => _ocrWorkerClient.EngineName;

    public Task<OcrWorkerWarmupResult> WarmupAsync(
        FrameExtractionResult frameExtractionResult,
        CancellationToken cancellationToken = default)
    {
        var ocrDirectory = Path.Combine(frameExtractionResult.RunDirectory, "ocr");
        return _ocrWorkerClient.WarmupAsync(ocrDirectory, cancellationToken);
    }

    public async Task<IReadOnlyList<FrameAnalysisResult>> AnalyzeFramesAsync(
        FrameExtractionResult frameExtractionResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
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

            var frameStopwatch = Stopwatch.StartNew();
            var ocrResult = await _ocrWorkerClient.RecognizeAsync(request, ocrDirectory, cancellationToken);

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
                ocrResult.RequestWriteMs,
                ocrResult.WorkerInitializationMs,
                ocrResult.WorkerExecutionMs,
                ocrResult.ResponseReadMs,
                attributeAnalysisStopwatch.Elapsed.TotalMilliseconds,
                attributeWriteStopwatch.Elapsed.TotalMilliseconds,
                frameStopwatch.Elapsed.TotalMilliseconds);
            results.Add(new FrameAnalysisResult(frame, ocrResult.Response, attributeResult, performance));

            progress?.Report(((double)(i + 1) / frameExtractionResult.Frames.Count) * 100d);
        }

        return results;
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
}
