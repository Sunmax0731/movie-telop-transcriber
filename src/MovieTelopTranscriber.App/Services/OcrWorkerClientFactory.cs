namespace MovieTelopTranscriber.App.Services;

internal static class OcrWorkerClientFactory
{
    private const string EngineEnvironmentVariable = "MOVIE_TELOP_OCR_ENGINE";

    public static IOcrWorkerClient Create()
    {
        var engine = Environment.GetEnvironmentVariable(EngineEnvironmentVariable);
        return IsPaddleOcr(engine)
            ? new PaddleOcrWorkerClient()
            : new ProcessOcrWorkerClient();
    }

    private static bool IsPaddleOcr(string? engine)
    {
        return string.Equals(engine, "paddleocr", StringComparison.OrdinalIgnoreCase)
            || string.Equals(engine, "paddle-ocr", StringComparison.OrdinalIgnoreCase);
    }
}
