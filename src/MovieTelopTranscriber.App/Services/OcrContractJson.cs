using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

internal static class OcrContractJson
{
    public static JsonTypeInfo<AttributeAnalysisResult> AttributeAnalysisResult => OcrJsonSerializerContext.Default.AttributeAnalysisResult;

    public static JsonTypeInfo<ExportPackage> ExportPackage => OcrJsonSerializerContext.Default.ExportPackage;

    public static JsonTypeInfo<OcrWorkerRequest> OcrWorkerRequest => OcrJsonSerializerContext.Default.OcrWorkerRequest;

    public static JsonTypeInfo<OcrWorkerResponse> OcrWorkerResponse => OcrJsonSerializerContext.Default.OcrWorkerResponse;

    public static JsonTypeInfo<OcrFramePerformanceRecord[]> OcrFramePerformanceRecords => OcrJsonSerializerContext.Default.OcrFramePerformanceRecordArray;

    public static JsonTypeInfo<PaddleOcrWorkerAck> PaddleOcrWorkerAck => OcrJsonSerializerContext.Default.PaddleOcrWorkerAck;

    public static JsonTypeInfo<RunSummaryRecord> RunSummaryRecord => OcrJsonSerializerContext.Default.RunSummaryRecord;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(AttributeAnalysisResult))]
[JsonSerializable(typeof(EditOperationRecord))]
[JsonSerializable(typeof(ExportPackage))]
[JsonSerializable(typeof(FrameExportRecord))]
[JsonSerializable(typeof(OcrBoundingPoint))]
[JsonSerializable(typeof(OcrFramePerformanceRecord))]
[JsonSerializable(typeof(OcrFramePerformanceRecord[]))]
[JsonSerializable(typeof(OcrDetectionRecord))]
[JsonSerializable(typeof(OcrWorkerExecutionResult))]
[JsonSerializable(typeof(OcrWorkerRequest))]
[JsonSerializable(typeof(OcrWorkerResponse))]
[JsonSerializable(typeof(PaddleOcrWorkerAck))]
[JsonSerializable(typeof(ProcessingError))]
[JsonSerializable(typeof(ProcessingSettingsRecord))]
[JsonSerializable(typeof(RunMetadataRecord))]
[JsonSerializable(typeof(RunPerformanceSummaryRecord))]
[JsonSerializable(typeof(RunSummaryRecord))]
[JsonSerializable(typeof(SegmentRecord))]
[JsonSerializable(typeof(TelopAttributeRecord))]
[JsonSerializable(typeof(VideoMetadata))]
internal sealed partial class OcrJsonSerializerContext : JsonSerializerContext
{
}
