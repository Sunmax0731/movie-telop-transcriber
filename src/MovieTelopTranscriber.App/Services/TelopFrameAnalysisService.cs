using MovieTelopTranscriber.App.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MovieTelopTranscriber.App.Services;

public sealed class TelopFrameAnalysisService
{
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

    public Task<OcrWorkerWarmupResult> WarmupAsync(
        FrameExtractionResult frameExtractionResult,
        CancellationToken cancellationToken = default)
    {
        var ocrDirectory = Path.Combine(frameExtractionResult.RunDirectory, "ocr");
        return _ocrWorkerClient.WarmupAsync(ocrDirectory, cancellationToken);
    }

    public Task<OcrWorkerWarmupResult> WarmupAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var ocrDirectory = Path.Combine(workDirectory, "ocr");
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
            var frameStopwatch = Stopwatch.StartNew();
            if (selectionDecision.ShouldRunOcr || previousAnalysis is null)
            {
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
                currentAnalysis = new FrameAnalysisResult(frame, ocrResult.Response, attributeResult, performance);
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
}
