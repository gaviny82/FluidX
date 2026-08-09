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
    [Params(0, 1000, 2000)]
    public int EditCount { get; set; }

    [ParamsSource(nameof(FileTypes))]
    public TestFileType FileType { get; set; }
    public static IEnumerable<TestFileType> FileTypes => BenchmarkParams.FileTypes;

    [ParamsSource(nameof(Impls))]
    public BufferImplementation Implementation { get; set; }
    public static IEnumerable<BufferImplementation> Impls => BenchmarkParams.BufferImpls;

    private readonly string _fileText;
    private readonly PreGeneratedEdit[] _edits;
    private ITextBuffer _buffer = null!;

    public SnapshotBenchmark()
    {
        _fileText = TestFileHelper.LoadFileText(FileType);
        var random = new Random(42);
        _edits = EditHelper.PreGenerateRandomEdits(_fileText, random, EditCount);

        var buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        if (EditCount > 0)
            EditHelper.ApplyEdits(_buffer, _edits);
        _buffer = buffer;
    }

    [Benchmark]
    public ITextSnapshot CreateSnapshot()
    {
        return _buffer.CreateSnapshot(preserveBOM: false);
    }
}
