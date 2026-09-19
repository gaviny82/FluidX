using System;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Snapshot;

[ShortRunJob]
[MemoryDiagnoser]
public class SnapshotBenchmark
{
    [Params(0, 1000, 2000)]
    public int EditCount { get; set; }

    [ParamsAllValues]
    public TestFileType FileType { get; set; }

    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

    private string _fileText = null!;
    private PreGeneratedEdit[] _edits = null!;
    private ITextBuffer _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileText = TestFileHelper.LoadFileText(FileType);
        var random = new Random(42);
        _edits = EditHelper.PreGenerateRandomEdits(_fileText, random, EditCount);

        // Create buffer and apply edits
        var buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        if (EditCount > 0)
            EditHelper.ApplyEdits(buffer, _edits);
        _buffer = buffer;
    }

    [Benchmark]
    public ITextSnapshot CreateSnapshot()
    {
        return _buffer.CreateSnapshot();
    }
}
