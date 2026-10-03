using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using FluidX.TextBuffers;
using FluidX.TextBuffers.LineArray;
using FluidX.TextBuffers.PieceTree;
using FluidX.TextBeffers.Benchmarks.Utils;
using System.Text;

namespace FluidX.TextBeffers.Benchmarks.Load;

[MemoryDiagnoser]
public class LoadFileBenchmark
{
    [ParamsAllValues]
    public TestFileType FileType { get; set; }

    [ParamsAllValues]
    public BufferImplementation Implementation { get; set; }

    private byte[] _fileData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileData = TestFileHelper.LoadFileBytes(FileType);
    }

    [Benchmark]
    public async Task<IReadOnlyTextBuffer> LoadFile()
    {
        using var stream = new MemoryStream(_fileData);
        if (Implementation == BufferImplementation.PieceTree)
        {
            return await PieceTreeTextBuffer.CreateAsync(stream, EndOfLine.LF);
        }

        using var reader = new StreamReader(stream);
        string text = await reader.ReadToEndAsync();
        return BufferFactory.CreateBuffer(Implementation, text);
    }
}
