using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class SnapshotBenchmark
{
    [Params(0, 1000, 5000)]
    public int EditCount { get; set; }

    [ParamsSource(nameof(FileTypes))]
    public TestFileType FileType { get; set; }
    public static IEnumerable<TestFileType> FileTypes => BenchmarkParams.FileTypes;

    [ParamsSource(nameof(Impls))]
    public BufferImplementation Implementation { get; set; }
    // LineArray impl is too slow for large files, so we only benchmark PieceTree here.
    public static IEnumerable<BufferImplementation> Impls => [BufferImplementation.PieceTree];

    private string _fileText = null!;
    private ITextBuffer _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileText = TestFileHelper.LoadFileText(FileType);
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);

        if (EditCount > 0)
        {
            var random = new Random(42);
            var edits = EditHelper.PreGenerateRandomEdits(_fileText, random, EditCount);
            EditHelper.ApplyEdits(_buffer, edits);
        }
    }

    [Benchmark]
    public ITextSnapshot CreateSnapshot()
    {
        return _buffer.CreateSnapshot(preserveBOM: false);
    }
}
