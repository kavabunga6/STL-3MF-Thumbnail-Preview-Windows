using System.Text.Json;

namespace Explorer3DPreview.Benchmarks;

internal sealed record RenderCase(string Name, string File, bool ExpectedSuccess, int[] Sizes);
internal sealed record InputStamp(long Length, long LastWriteTicks, string Sha256, bool ReadOnly);
internal sealed record WorkerRequest(string Assembly, string Input, int Size, string Mode,
    bool ExpectedSuccess, int Warmups, int Iterations, string Image, string Result);
internal sealed record WorkerResult(bool Passed, string? Error, double ColdMs, double MedianMs,
    double P95Ms, double[] SamplesMs, long PeakWorkingSetBytes, int ForegroundPixels);
internal sealed record ComparisonRow(string Case, string Mode, int Size, WorkerResult Baseline,
    WorkerResult Candidate, string Status, bool Confirmed);
internal sealed record BenchmarkReport(int SchemaVersion, string BaselineLabel, string CandidateLabel,
    string Runtime, string OperatingSystem, int Warmups, int Iterations, int WorkerTimeoutSeconds,
    double RegressionPercent, double NoiseFloorMs, double MaxRenderMs,
    Dictionary<string, InputStamp> Inputs, List<ComparisonRow> Results, List<string> Errors);

internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}

internal static class Comparison
{
    internal const double RegressionPercent = 30;
    internal const double NoiseFloorMs = 20;
    internal const double MaxRenderMs = 5000;

    internal static bool IsSlower(WorkerResult baseline, WorkerResult candidate) =>
        baseline.Passed && candidate.Passed &&
        candidate.MedianMs > baseline.MedianMs * (1 + RegressionPercent / 100) &&
        candidate.MedianMs - baseline.MedianMs > NoiseFloorMs;

    internal static string Status(WorkerResult baseline, WorkerResult candidate, bool confirmed)
    {
        if (!candidate.Passed) return "FAIL";
        if (candidate.MedianMs > MaxRenderMs || candidate.ColdMs > MaxRenderMs) return "TOO SLOW";
        if (!baseline.Passed) return "BASELINE FAILED";
        if (confirmed && IsSlower(baseline, candidate)) return "REGRESSION";
        return "PASS";
    }

    internal static bool IsFailure(ComparisonRow row) =>
        row.Status is "FAIL" or "TOO SLOW" or "REGRESSION";

    internal static double Percentile(double[] values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)];
    }
}
