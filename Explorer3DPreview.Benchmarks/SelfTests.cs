namespace Explorer3DPreview.Benchmarks;

internal static class SelfTests
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Benchmark self-test failed: " + name);
            checks++;
        }
        WorkerResult Success(double milliseconds) => new(true, null, milliseconds, milliseconds, milliseconds,
            new[] { milliseconds }, 1024, 100);
        var failure = new WorkerResult(false, "worker failed", 0, 0, 0, Array.Empty<double>(), 0, 0);
        Check(!Comparison.IsSlower(Success(100), Success(125)), "normal runner noise");
        Check(!Comparison.IsSlower(Success(1), Success(5)), "absolute noise floor");
        Check(Comparison.IsSlower(Success(100), Success(140)), "real regression");
        Check(Comparison.Status(Success(100), Success(140), false) == "PASS", "confirmation required");
        Check(Comparison.Status(Success(100), Success(140), true) == "REGRESSION", "confirmed regression fails");
        Check(Comparison.Status(Success(100), failure, true) == "FAIL", "worker failure cannot be masked");
        Check(Comparison.Status(failure, Success(100), false) == "BASELINE FAILED", "baseline failure is visible");
        Check(Comparison.Status(Success(100), Success(6000), false) == "TOO SLOW", "absolute time limit");
        Check(Comparison.Percentile(new double[] { 10, 1, 5, 3, 2 }, 0.5) == 3, "median ignores an outlier");
        Check(Comparison.Percentile(new double[] { 10, 1, 5, 3, 2 }, 0.95) == 10, "p95");
        var directory = Path.Combine(Path.GetTempPath(), "Explorer3DPreview.BenchmarkSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "sentinel.txt");
        File.WriteAllText(file, "original");
        var snapshot = Fixtures.Snapshot(directory);
        Fixtures.AssertUnchanged(snapshot, directory);
        Check(true, "unchanged files accepted");
        File.WriteAllText(file, "changed");
        try { Fixtures.AssertUnchanged(snapshot, directory); Check(false, "changed file detected"); }
        catch (IOException) { checks++; }
        File.WriteAllText(file, "original");
        snapshot = Fixtures.Snapshot(directory);
        File.WriteAllText(Path.Combine(directory, "unexpected-sidecar.txt"), "new");
        try { Fixtures.AssertUnchanged(snapshot, directory); Check(false, "sidecar detected"); }
        catch (IOException) { checks++; }
        try { Fixtures.Create(directory); Check(false, "refuse populated fixture directory"); }
        catch (IOException) { checks++; }
        Console.WriteLine($"Benchmark self-tests passed ({checks} checks).");
        return 0;
    }
}
