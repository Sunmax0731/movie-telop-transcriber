namespace MovieTelopTranscriber.App.Models;

public sealed record ExportWriteResult(
    string OutputDirectory,
    string JsonPath,
    string SegmentsCsvPath,
    string FramesCsvPath,
    string SrtPath,
    string VttPath,
    string AssPath);
