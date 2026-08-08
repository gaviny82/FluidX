using System.Collections.Generic;

namespace FluidX.TextBeffers.Benchmarks.Utils;

public static class BenchmarkParams
{
    public static IEnumerable<TestFileType> FileTypes => [TestFileType.CheckerTs, TestFileType.SqliteC, TestFileType.BilingualDictionary];
    public static IEnumerable<BufferImplementation> BufferImpls => [BufferImplementation.PieceTree, BufferImplementation.LineArray];
}
