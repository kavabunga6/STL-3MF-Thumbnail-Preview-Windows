using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Explorer3DPreview.Benchmarks;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

if (args.Length == 1 && args[0] == "--self-test")
    return SelfTests.Run();
if (args.Length == 2 && args[0] == "--worker")
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(File.ReadAllText(args[1]), Json.Options)!;
    var result = RenderWorker.Run(request);
    File.WriteAllText(request.Result, JsonSerializer.Serialize(result, Json.Options));
    return result.Passed ? 0 : 1;
}

try { return Run(args); }
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static int Run(string[] args)
{
    if (args.Length < 3 || args.Length > 5)
    {
        Console.Error.WriteLine("Usage: dotnet Explorer3DPreview.Benchmarks.dll <baseline.dll> <candidate.dll> <output-directory> [baseline-label] [candidate-label]");
        return 2;
    }
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Run on Windows x64 with .NET 6 Desktop Runtime.");
    var baselineAssembly = Path.GetFullPath(args[0]);
    var candidateAssembly = Path.GetFullPath(args[1]);
    if (!File.Exists(baselineAssembly) || !File.Exists(candidateAssembly))
        throw new FileNotFoundException("Build both renderer versions before running the benchmark.");
    var output = Path.GetFullPath(args[2]);
    var runDirectory = Path.Combine(output, "runs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
    var inputs = Path.Combine(runDirectory, "inputs");
    var images = Path.Combine(runDirectory, "images");
    var raw = Path.Combine(runDirectory, "raw");
    Directory.CreateDirectory(images);
    Directory.CreateDirectory(raw);
    Console.WriteLine("Generating deterministic, read-only fixtures (including 750,000-triangle STL)...");
    var cases = Fixtures.Create(inputs);
    var snapshot = Fixtures.Snapshot(inputs);
    const int warmups = 2, iterations = 5, workerTimeoutSeconds = 45;
    var rows = new List<ComparisonRow>();
    var errors = new List<string>();
    var report = new BenchmarkReport(1, args.Length > 3 ? args[3] : "baseline",
        args.Length > 4 ? args[4] : "candidate", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        System.Runtime.InteropServices.RuntimeInformation.OSDescription, warmups, iterations, workerTimeoutSeconds,
        Comparison.RegressionPercent, Comparison.NoiseFloorMs, Comparison.MaxRenderMs, snapshot, rows, errors);
    var index = 0;
    foreach (var test in cases)
        foreach (var size in test.Sizes)
            foreach (var mode in new[] { "file", "stream" })
            {
                var key = $"{test.Name}-{mode}-{size}";
                WorkerResult Measure(string label, string assembly, int attempt)
                {
                    var request = new WorkerRequest(assembly, test.File, size, mode, test.ExpectedSuccess,
                        warmups, iterations, Path.Combine(images, $"{key}-{label}-{attempt}.png"),
                        Path.Combine(raw, $"{key}-{label}-{attempt}.json"));
                    var result = Launch(request, workerTimeoutSeconds);
                    // Also runs after crashes and forced termination; catches missing and new files.
                    Fixtures.AssertUnchanged(snapshot, inputs);
                    return result;
                }

                WorkerResult baseline, candidate;
                try
                {
                    // Alternate which version runs first to reduce systematic cache/load bias.
                    if (index++ % 2 == 0)
                    {
                        baseline = Measure("baseline", baselineAssembly, 1);
                        candidate = Measure("candidate", candidateAssembly, 1);
                    }
                    else
                    {
                        candidate = Measure("candidate", candidateAssembly, 1);
                        baseline = Measure("baseline", baselineAssembly, 1);
                    }
                    var confirmed = false;
                    if (Comparison.IsSlower(baseline, candidate))
                    {
                        Console.WriteLine($"{key}: possible slowdown; repeating in reverse order...");
                        // Repeat with the opposite order. Fail only if both independent comparisons agree.
                        WorkerResult repeatBase, repeatCandidate;
                        if (index % 2 == 1)
                        {
                            repeatCandidate = Measure("candidate", candidateAssembly, 2);
                            repeatBase = Measure("baseline", baselineAssembly, 2);
                        }
                        else
                        {
                            repeatBase = Measure("baseline", baselineAssembly, 2);
                            repeatCandidate = Measure("candidate", candidateAssembly, 2);
                        }
                        confirmed = Comparison.IsSlower(repeatBase, repeatCandidate);
                        // A failed confirmation must never turn into a passing performance result.
                        if (!repeatCandidate.Passed) candidate = repeatCandidate;
                        else if (confirmed) { baseline = repeatBase; candidate = repeatCandidate; }
                        else if (!repeatBase.Passed) baseline = repeatBase;
                    }
                    var status = Comparison.Status(baseline, candidate, confirmed);
                    rows.Add(new ComparisonRow(test.Name, mode, size, baseline, candidate, status, confirmed));
                    Console.WriteLine($"{key}: {status}; base={baseline.MedianMs:F2} ms; candidate={candidate.MedianMs:F2} ms");
                }
                catch (Exception error)
                {
                    errors.Add($"{key}: {error.Message}");
                    WriteReport();
                    return 1; // Stop immediately on any input modification or coordinator error.
                }
                WriteReport();
            }

    WriteReport();
    return errors.Count == 0 && !rows.Any(Comparison.IsFailure) ? 0 : 1;

    void WriteReport()
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, Json.Options));
        Reports.Write(output, runDirectory, report);
    }
}

static WorkerResult Launch(WorkerRequest request, int timeoutSeconds)
{
    var requestFile = Path.ChangeExtension(request.Result, ".request.json");
    File.WriteAllText(requestFile, JsonSerializer.Serialize(request, Json.Options));
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the current .NET process.");
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--worker");
    start.ArgumentList.Add(requestFile);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the render worker.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutSeconds * 1000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
        return Failure($"Worker exceeded {timeoutSeconds} seconds and was terminated.");
    }
    // Output from a renderer can be useful when a worker terminates without a JSON result.
    Task.WaitAll(new Task[] { stdout, stderr }, 5000);
    if (!File.Exists(request.Result))
        return Failure($"Worker exited {process.ExitCode} without a result. {(stderr.IsCompletedSuccessfully ? stderr.Result.Trim() : "No completed stderr output.")}");
    try
    {
        var result = JsonSerializer.Deserialize<WorkerResult>(File.ReadAllText(request.Result), Json.Options)
            ?? throw new InvalidDataException("Empty worker result.");
        return process.ExitCode == 0 || !result.Passed ? result : Failure($"Worker exited {process.ExitCode} after reporting success.");
    }
    catch (Exception error) { return Failure("Invalid worker result: " + error.Message); }

    static WorkerResult Failure(string message) => new(false, message, 0, 0, 0, Array.Empty<double>(), 0, 0);
}
