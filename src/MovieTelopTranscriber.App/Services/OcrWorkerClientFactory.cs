namespace MovieTelopTranscriber.App.Services;

internal static class OcrWorkerClientFactory
{
    private const string EngineEnvironmentVariable = "MOVIE_TELOP_OCR_ENGINE";
    private const string WorkerPathEnvironmentVariable = "MOVIE_TELOP_OCR_WORKER";

    public static IOcrWorkerClient Create()
    {
        var engine = Environment.GetEnvironmentVariable(EngineEnvironmentVariable);
        return ShouldUsePaddleOcr(engine)
            ? new PaddleOcrWorkerClient()
            : new ProcessOcrWorkerClient();
    }

    private static bool ShouldUsePaddleOcr(string? engine)
    {
        if (IsPaddleOcr(engine))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(engine))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable));
    }

    private static bool IsPaddleOcr(string? engine)
    {
        return string.Equals(engine, "paddleocr", StringComparison.OrdinalIgnoreCase)
            || string.Equals(engine, "paddle-ocr", StringComparison.OrdinalIgnoreCase);
    }
}
