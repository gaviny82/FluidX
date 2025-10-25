namespace FluidX.TextBuffers;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    void SetEOL(string eol); // either "\r\n" or "\n"

    ApplyEditsResult ApplyEdits(
        EditOperation[] rawOperations,
        bool recordTrimAutoWhitespace,
        bool computeUndoEdits);
}
