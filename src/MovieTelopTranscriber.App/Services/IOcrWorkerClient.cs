using MovieTelopTranscriber.App.Models;

namespace MovieTelopTranscriber.App.Services;

public interface IOcrWorkerClient
{
    string EngineName { get; }

    Task<OcrWorkerResponse> RecognizeAsync(
        OcrWorkerRequest request,
        string ocrDirectory,
        CancellationToken cancellationToken = default);
}
