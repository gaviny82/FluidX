namespace EditorTextBuffers.Contracts;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    void SetEOL(string eol); // either "\r\n" or "\n"

    ApplyEditsResult ApplyEdits(
        ValidAnnotatedEditOperation[] rawOperations,
        bool recordTrimAutoWhitespace,
        bool computeUndoEdits);
}
