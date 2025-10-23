using FluidX.TextModels;

namespace FluidX.TextBuffers;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    void SetEOL(string eol); // either "\r\n" or "\n"

    void ApplyEdits(ReadOnlySpan<TextReplacement> operations);
}
