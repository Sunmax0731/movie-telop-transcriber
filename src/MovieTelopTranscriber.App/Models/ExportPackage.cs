using System.Collections.Generic;

namespace MovieTelopTranscriber.App.Models;

public sealed record ExportPackage(
    string SchemaVersion,
    VideoMetadata SourceVideo,
    ProcessingSettingsRecord ProcessingSettings,
    IReadOnlyList<FrameExportRecord> Frames,
    IReadOnlyList<SegmentRecord> Segments,
    IReadOnlyList<EditOperationRecord> Edits,
    RunMetadataRecord RunMetadata);
