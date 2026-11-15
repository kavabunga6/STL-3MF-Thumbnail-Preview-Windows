using System.Net;
using System.Text;

namespace Explorer3DPreview.Benchmarks;

internal static class Reports
{
    internal static void Write(string output, string runDirectory, BenchmarkReport report)
    {
        var markdown = new StringBuilder("# Render regression check\n\n");
        markdown.AppendLine($"Baseline: \u0060{report.BaselineLabel}\u0060. Candidate: \u0060{report.CandidateLabel}\u0060.");
        markdown.AppendLine($"\nSame Windows runner, {report.Warmups} warmups and {report.Iterations} measured renders per worker.");
        markdown.AppendLine($"A slowdown fails only after confirmation: >{report.RegressionPercent}% **and** >{report.NoiseFloorMs} ms.");
        markdown.AppendLine($"Single-render limit: {report.MaxRenderMs} ms. Worker timeout: {report.WorkerTimeoutSeconds} s.");
        markdown.AppendLine("\n| Case | Input | Size | Base median | PR median | Change | Result |");
        markdown.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var row in report.Results)
        {
            var change = row.Baseline.Passed && row.Candidate.Passed && row.Baseline.MedianMs > 0
                ? $"{(row.Candidate.MedianMs / row.Baseline.MedianMs - 1) * 100:+0.0;-0.0;0.0}%" : "—";
            markdown.AppendLine($"| {row.Case} | {row.Mode} | {row.Size} | {Time(row.Baseline)} | {Time(row.Candidate)} | {change} | {row.Status} |");
        }
        var failures = report.Results.Where(Comparison.IsFailure).ToArray();
        markdown.AppendLine($"\n{report.Results.Count} cases completed; {failures.Length} failed; {report.Errors.Count} input/coordinator errors.");
        foreach (var row in report.Results.Where(r => !r.Baseline.Passed || !r.Candidate.Passed))
            markdown.AppendLine($"\n- \u0060{row.Case}/{row.Mode}/{row.Size}\u0060: baseline: {Clean(row.Baseline.Error ?? "OK")}; candidate: {Clean(row.Candidate.Error ?? "OK")}.");
        foreach (var error in report.Errors) markdown.AppendLine("\n- **Input/coordinator error:** " + Clean(error));
        markdown.AppendLine("\nPNG previews and a side-by-side index.html are in the workflow artifact. Changed appearance needs human review; pixel equality is deliberately not required.");
        markdown.AppendLine("\nTimings include initialization, file/stream opening, parsing, rendering and HBITMAP creation. PNG encoding, fixture generation and input hashing are outside the measured region.");
        File.WriteAllText(Path.Combine(output, "summary.md"), markdown.ToString());

        var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Render comparison</title>" +
            "<style>body{font:15px system-ui;margin:24px;background:#f3f5f8;color:#172331}article{background:white;padding:16px;margin:18px 0;border-radius:8px}" +
            ".images{display:flex;gap:24px;flex-wrap:wrap}figure{margin:0}img{width:256px;height:256px;object-fit:contain;border:1px solid #ccd3dd}" +
            "pre{white-space:pre-wrap}h2{font-size:18px}</style><body><h1>Render comparison</h1>");
        html.Append("<p>Baseline: ").Append(E(report.BaselineLabel)).Append(" · Candidate: ").Append(E(report.CandidateLabel)).Append("</p>");
        html.Append("<p>Inspect geometry and orientation here. Timing data and failure details are in summary.md and report.json.</p>");
        foreach (var row in report.Results)
        {
            html.Append("<article><h2>").Append(E($"{row.Case} / {row.Mode} / {row.Size}px — {row.Status}")).Append("</h2><div class=\"images\">");
            Preview("baseline", row.Baseline);
            Preview("candidate", row.Candidate);
            html.Append("</div></article>");
            void Preview(string label, WorkerResult result)
            {
                html.Append("<figure><figcaption>").Append(E($"{label}: {Time(result)}")).Append("</figcaption>");
                var prefix = $"{row.Case}-{row.Mode}-{row.Size}-{label}-";
                var image = new[] { 2, 1 }.Select(attempt => Path.Combine(runDirectory, "images", prefix + attempt + ".png")).FirstOrDefault(File.Exists);
                if (image is not null)
                    html.Append("<img alt=\"").Append(E(row.Case + " " + label)).Append("\" src=\"")
                        .Append(E(Path.GetRelativePath(output, image).Replace('\\','/'))).Append("\">");
                else html.Append("<p>").Append(E(result.Error ?? "Expected rejection; no image.")).Append("</p>");
                html.Append("</figure>");
            }
        }
        foreach (var error in report.Errors) html.Append("<pre>").Append(E(error)).Append("</pre>");
        html.Append("</body></html>");
        File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);
    private static string Clean(string? text) => (text ?? "").Replace("\r"," ").Replace("\n"," ").Replace("|","/");
    private static string Time(WorkerResult result) => result.Passed ? $"{result.MedianMs:F2} ms" : "FAILED";
}
