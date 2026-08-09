using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBeffers.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class RandomEditAfterEditsBenchmark
{
    private const int PreEditCount = 1000;
    private const int EditCount = 1000;
    private const int EditLength = 10;

    [ParamsSource(nameof(FileTypes))]
    public TestFileType FileType { get; set; }
    public static IEnumerable<TestFileType> FileTypes => BenchmarkParams.FileTypes;

    [ParamsSource(nameof(Impls))]
    public BufferImplementation Implementation { get; set; }
    public static IEnumerable<BufferImplementation> Impls => BenchmarkParams.BufferImpls;

    private string _fileText = null!;
    private PreGeneratedEdit[] _preEdits = null!;
    private PreGeneratedEdit[] _edits = null!;
    private ITextBuffer _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileText = TestFileHelper.LoadFileText(FileType);

        var random = new Random(42);
        _preEdits = EditHelper.PreGenerateRandomEdits(_fileText, random, PreEditCount, EditLength);
        _edits = EditHelper.PreGenerateRandomEdits(_fileText, random, EditCount, EditLength);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        EditHelper.ApplyEdits(_buffer, _preEdits);
    }

    [Benchmark]
    public void ApplyEditsAfterEdits()
    {
        EditHelper.ApplyEdits(_buffer, _edits);
    }
}
