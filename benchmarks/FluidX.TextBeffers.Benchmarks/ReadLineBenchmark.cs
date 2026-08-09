using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBeffers.Benchmarks.Utils;
using FluidX.TextBuffers.LineArray;

namespace FluidX.TextBeffers.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class ReadLineBenchmark
{
    private const int EditCount = 1000;
    private const int EditLength = 10;

    [ParamsSource(nameof(FileTypes))]
    public TestFileType FileType { get; set; }
    public static IEnumerable<TestFileType> FileTypes => BenchmarkParams.FileTypes;

    [ParamsSource(nameof(Impls))]
    public BufferImplementation Implementation { get; set; }
    // The LineArray impl is not benchmarked as it is too slow to apply the edits.
    public static IEnumerable<BufferImplementation> Impls => BenchmarkParams.BufferImpls;

    private readonly string _fileText;
    private readonly PreGeneratedEdit[] _randomEdits;
    private readonly PreGeneratedEdit[] _sequentialEdits;
    private ITextBuffer _buffer = null!;
    private int _lineNumber;

    // Cache of line array implementation to avoid re-creating it for each benchmark iteration, as it is slow to create.
    private static LineArrayTextBuffer s_lineArrayBufferCacheSingleEdit = null!;
    private static LineArrayTextBuffer s_lineArrayBufferCache1000RandomEdits = null!;
    private static LineArrayTextBuffer s_lineArrayBufferCache1000SequentialEdits = null!;

    // Setup for all benchmarks
    public ReadLineBenchmark()
    {
        _fileText = TestFileHelper.LoadFileText(FileType);

        var random = new Random(42);
        _randomEdits = EditHelper.PreGenerateRandomEdits(_fileText, random, EditCount, EditLength);

        random = new Random(42);
        _sequentialEdits = EditHelper.PreGenerateSequentialEdits(_fileText, random, EditCount);
    }

    // Setup for each benchmark

    [GlobalSetup(Target = nameof(ReadLineAfterSingleEdit))]
    public void SetupForSingleEdit()
    {
        if (Implementation != BufferImplementation.LineArray)
            return;
        Console.WriteLine("Setup for single edit only");
        s_lineArrayBufferCacheSingleEdit = (LineArrayTextBuffer)BufferFactory.CreateBuffer(BufferImplementation.LineArray, _fileText);
        EditHelper.ApplyEdits(s_lineArrayBufferCacheSingleEdit, [_randomEdits[0]]);
    }

    [GlobalSetup(Target = nameof(ReadLineAfter1000RandomEdits))]
    public void SetupFor1000RandomEdits()
    {
        if (Implementation != BufferImplementation.LineArray)
            return;
        Console.WriteLine("Setup for 1000 random edits only");
        s_lineArrayBufferCache1000RandomEdits = (LineArrayTextBuffer)BufferFactory.CreateBuffer(BufferImplementation.LineArray, _fileText);
        EditHelper.ApplyEdits(s_lineArrayBufferCache1000RandomEdits, _randomEdits);
    }

    [GlobalSetup(Target = nameof(ReadLineAfter1000SequentialEdits))]
    public void SetupFor1000SequentialEdits()
    {
        if (Implementation != BufferImplementation.LineArray)
            return;
        Console.WriteLine("Setup for 1000 sequential edits only");
        s_lineArrayBufferCache1000SequentialEdits = (LineArrayTextBuffer)BufferFactory.CreateBuffer(BufferImplementation.LineArray, _fileText);
        EditHelper.ApplyEdits(s_lineArrayBufferCache1000SequentialEdits, _sequentialEdits);
    }

    // Iteration setup: resets the text buffer to clear any cache that affects the benchmark results.

    [IterationSetup(Target = nameof(ReadLine))]
    public void IterationSetupRead()
    {
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        _lineNumber = _buffer.LineCount / 2;
    }

    [IterationSetup(Target = nameof(ReadLineAfterSingleEdit))]
    public void IterationSetupSingle()
    {
        if (Implementation == BufferImplementation.LineArray)
        {
            _buffer = s_lineArrayBufferCacheSingleEdit;
            return;
        }
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        EditHelper.ApplyEdits(_buffer, [_randomEdits[0]]);
        _lineNumber = _buffer.LineCount / 2;
    }

    [IterationSetup(Target = nameof(ReadLineAfter1000RandomEdits))]
    public void IterationSetupRandom()
    {
        if (Implementation == BufferImplementation.LineArray)
        {
            _buffer = s_lineArrayBufferCache1000RandomEdits;
            return;
        }
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        EditHelper.ApplyEdits(_buffer, _randomEdits);
        _lineNumber = _buffer.LineCount / 2;
    }

    [IterationSetup(Target = nameof(ReadLineAfter1000SequentialEdits))]
    public void IterationSetupSequential()
    {
        if (Implementation == BufferImplementation.LineArray)
        {
            _buffer = s_lineArrayBufferCache1000SequentialEdits;
            return;
        }
        _buffer = BufferFactory.CreateBuffer(Implementation, _fileText);
        EditHelper.ApplyEdits(_buffer, _sequentialEdits);
        _lineNumber = _buffer.LineCount / 2;
    }

    // Benchmarks

    [Benchmark]
    public string ReadLine()
    {
        return _buffer.GetLineContent(_lineNumber);
    }

    [Benchmark]
    public string ReadLineAfterSingleEdit()
    {
        return _buffer.GetLineContent(_lineNumber);
    }

    [Benchmark]
    public string ReadLineAfter1000RandomEdits()
    {
        return _buffer.GetLineContent(_lineNumber);
    }

    [Benchmark]
    public string ReadLineAfter1000SequentialEdits()
    {
        return _buffer.GetLineContent(_lineNumber);
    }
}
