using System;
using System.Collections.Generic;
using System.Linq;
using CommandLine;
using CommandLine.Text;

namespace FluidX.TextBeffers.Benchmarks.Utils;

internal sealed class BenchmarkCommandLineOptions
{
    [Option(
        "test-files",
        MetaValue = "SET",
        HelpText = "Comma-separated test files: CheckerTs, SqliteC, BilingualDictionary. Default: all.")]
    public string TestFiles { get; set; }

    [Option(
        "buffer-implementations",
        MetaValue = "SET",
        HelpText = "Comma-separated implementations: LineArray, PieceTree, PersistentPieceTree. Default: all.")]
    public string BufferImplementations { get; set; }

    [Option(
        "benchmark-help",
        HelpText = "Show BenchmarkDotNet's command-line options.")]
    public bool BenchmarkHelp { get; set; }

    [Usage(ApplicationAlias = "FluidX.TextBeffers.Benchmarks")]
    public static IEnumerable<Example> Examples =>
    [
        new(
            "Run the read matrix with one file and two implementations",
            new BenchmarkCommandLineOptions
            {
                TestFiles = "CheckerTs",
                BufferImplementations = "PieceTree,PersistentPieceTree"
            }),
        new(
            "Select all test files and only PieceTree",
            new BenchmarkCommandLineOptions
            {
                TestFiles = "all",
                BufferImplementations = "PieceTree"
            })
    ];
}

internal sealed record BenchmarkSelection(
    IReadOnlySet<TestFileType> TestFiles,
    IReadOnlySet<BufferImplementation> BufferImplementations);

internal static class BenchmarkCommandLine
{
    private static readonly string[] CustomValueOptions =
    [
        "--test-files",
        "--buffer-implementations"
    ];

    private static readonly string[] CustomFlagOptions =
    [
        "--benchmark-help",
        "--help"
    ];

    public static bool TrySplitArguments(
        string[] args,
        out string[] cliArguments,
        out string[] benchmarkArguments)
    {
        var cliArgs = new List<string>();
        var benchmarkArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string valueOption = Array.Find(
                CustomValueOptions,
                option => arg == option || arg.StartsWith(option + "=", StringComparison.Ordinal));

            if (valueOption is not null)
            {
                cliArgs.Add(arg);
                if (arg == valueOption)
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Option '{valueOption}' requires a value.");
                        cliArguments = [];
                        benchmarkArguments = [];
                        return false;
                    }

                    cliArgs.Add(args[++i]);
                }
                continue;
            }

            if (Array.Exists(CustomFlagOptions, option => arg == option))
            {
                cliArgs.Add(arg);
                continue;
            }

            benchmarkArgs.Add(arg);
        }

        cliArguments = [.. cliArgs];
        benchmarkArguments = [.. benchmarkArgs];
        return true;
    }

    public static bool TryCreateSelection(
        BenchmarkCommandLineOptions options,
        out BenchmarkSelection selection)
    {
        if (!TryParseSet(options.TestFiles, "test file", out IReadOnlySet<TestFileType> testFiles)
            || !TryParseSet(
                options.BufferImplementations,
                "buffer implementation",
                out IReadOnlySet<BufferImplementation> implementations))
        {
            selection = null!;
            return false;
        }

        selection = new BenchmarkSelection(testFiles, implementations);
        return true;
    }

    private static bool TryParseSet<T>(
        string value,
        string displayName,
        out IReadOnlySet<T> result)
        where T : struct, Enum
    {
        if (value is null || value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            result = Enum.GetValues<T>().ToHashSet();
            return true;
        }

        string[] values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parsed = new HashSet<T>();
        foreach (string item in values)
        {
            if (!Enum.TryParse(item, ignoreCase: true, out T enumValue) || !Enum.IsDefined(enumValue))
            {
                Console.Error.WriteLine(
                    $"Unknown {displayName} '{item}'. Valid values: {string.Join(", ", Enum.GetNames<T>())}, all.");
                result = null!;
                return false;
            }

            parsed.Add(enumValue);
        }

        if (parsed.Count == 0)
        {
            Console.Error.WriteLine($"At least one {displayName} must be specified.");
            result = null!;
            return false;
        }

        result = parsed;
        return true;
    }
}
