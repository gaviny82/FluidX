using System;
using FluidX.TextBuffers;
using FluidX.TextBuffers.LineArray;
using FluidX.TextBuffers.PieceTree;

namespace FluidX.TextBeffers.Benchmarks.Utils;

public enum BufferImplementation
{
    PieceTree,
    LineArray
}

public static class BufferFactory
{
    public static ITextBuffer CreateBuffer(BufferImplementation implementation, string text)
    {
        return implementation switch
        {
            BufferImplementation.PieceTree => CreatePieceTreeBuffer(text),
            BufferImplementation.LineArray => CreateLineArrayBuffer(text),
            _ => throw new ArgumentOutOfRangeException(nameof(implementation))
        };
    }

    public static PieceTreeTextBuffer CreatePieceTreeBuffer(string text)
    {
        return PieceTreeTextBuffer.Create(text, DefaultEndOfLine.LF);
    }

    public static LineArrayTextBuffer CreateLineArrayBuffer(string text)
    {
        return new LineArrayTextBuffer(text);
    }
}
