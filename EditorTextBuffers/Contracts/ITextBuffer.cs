namespace EditorTextBuffers.Contracts;

// TODO: Implement editable text buffer

public interface ITextBuffer : IReadOnlyTextBuffer, IDisposable
{
    void SetEOL(string eol); // either "\r\n" or "\n"
    ApplyEditsResult ApplyEdits(
        ValidAnnotatedEditOperation[] rawOperations,
        bool recordTrimAutoWhitespace,
        bool computeUndoEdits);
}
