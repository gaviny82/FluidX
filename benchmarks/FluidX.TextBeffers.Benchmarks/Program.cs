using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using BenchmarkDotNet.Running;
using FluidX.TextBeffers.Benchmarks.Utils;

// Run all benchmarks: dotnet run -c Release --filter "*"
// Run a specific benchmark: dotnet run -c Release --filter "*ReadLineBenchmark*"
// List benchmarks: dotnet run -c Release --list flat

EnsureTestFiles(Environment.CurrentDirectory);
var _ = BenchmarkRunner.Run(typeof(Program).Assembly);

static void EnsureTestFiles(string baseDir)
{
    TestFileHelper.BaseDir = baseDir;

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

    foreach (var (_, url, filename) in TestFileHelper.GetFileMap())
    {
        var filePath = Path.Combine(baseDir, filename);
        if (File.Exists(filePath))
            continue;

        Console.WriteLine($"Downloading {filename} ...");
        var sw = Stopwatch.StartNew();

        var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        response.Content.CopyToAsync(fs).GetAwaiter().GetResult();

        Console.WriteLine($"Downloaded {filename} ({new FileInfo(filePath).Length} bytes) in {sw.Elapsed.TotalSeconds:F1}s");
    }
}
