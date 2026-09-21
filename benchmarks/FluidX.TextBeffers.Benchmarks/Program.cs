using System;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using CommandLine;
using FluidX.TextBeffers.Benchmarks.Utils;

if (!BenchmarkCommandLine.TrySplitArguments(args, out string[] cliArgs, out string[] benchmarkArgs))
    return 1;

var parser = new Parser(settings =>
{
    settings.AutoVersion = false;
    settings.CaseInsensitiveEnumValues = true;
    settings.HelpWriter = Console.Error;
});

int exitCode = parser.ParseArguments<BenchmarkCommandLineOptions>(cliArgs)
    .MapResult(
        options => RunBenchmarks(options, benchmarkArgs),
        errors => errors.Any(error => error is HelpRequestedError) ? 0 : 1);

return exitCode;

static int RunBenchmarks(BenchmarkCommandLineOptions options, string[] benchmarkArgs)
{
    if (!BenchmarkCommandLine.TryCreateSelection(options, out BenchmarkSelection selection))
        return 1;

    if (options.BenchmarkHelp)
        benchmarkArgs = [.. benchmarkArgs, "--help"];

    // Keep quick runs as the default, while allowing --job Default for noisy cases.
    // Job attributes on benchmark classes would otherwise add a second job.
    if (!Array.Exists(benchmarkArgs, arg => arg is "--job" or "-j" || arg.StartsWith("--job=")))
        benchmarkArgs = [.. benchmarkArgs, "--job", "Short"];

    var config = ManualConfig.Create(DefaultConfig.Instance)
        .AddFilter(new BenchmarkSelectionFilter(selection));

    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchmarkArgs, config);
    return 0;
}
