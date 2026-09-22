using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;

namespace FluidX.TextBeffers.Benchmarks.Utils;

internal sealed class BenchmarkSelectionFilter(BenchmarkSelection selection) : IFilter
{
    public bool Predicate(BenchmarkCase benchmarkCase)
    {
        foreach (var parameter in benchmarkCase.Parameters.Items)
        {
            if (parameter.Name == "FileType"
                && parameter.Value is TestFileType fileType
                && !selection.TestFiles.Contains(fileType))
            {
                return false;
            }

            if (parameter.Name == "Implementation"
                && parameter.Value is BufferImplementation implementation
                && !selection.BufferImplementations.Contains(implementation))
            {
                return false;
            }
        }

        return true;
    }
}
