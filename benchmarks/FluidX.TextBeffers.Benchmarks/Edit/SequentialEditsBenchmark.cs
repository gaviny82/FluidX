using System;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks.Edit;

[ShortRunJob]
[MemoryDiagnoser]
public class SequentialEditsBenchmark
{
    [Params(100, 1000)]
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
        _edits = EditHelper.PreGenerateSequentialEdits(_fileText, random, EditCount);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
    }

    [Benchmark]
    public void ApplySequentialEdits()
    {
        EditHelper.ApplyEdits(_buffer, _edits);
    }
}
