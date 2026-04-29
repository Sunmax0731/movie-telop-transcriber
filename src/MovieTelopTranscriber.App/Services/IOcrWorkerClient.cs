using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public interface IOcrWorkerClient
{
    string EngineName { get; }

    Task<OcrWorkerExecutionResult> RecognizeAsync(
        OcrWorkerRequest request,
        string ocrDirectory,
        CancellationToken cancellationToken = default);
}
