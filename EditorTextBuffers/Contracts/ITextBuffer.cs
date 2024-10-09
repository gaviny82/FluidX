namespace EditorTextBuffers.Contracts;

// TODO: Implement editable text buffer

public interface ITextBuffer : IReadOnlyTextBuffer, IDisposable
{
    void SetEOL(string eol); // either "\r\n" or "\n"
    //ApplyEditsResult ApplyEdits(
    //    ValidAnnotatedEditOperation[] rawOperations,
    //    bool recordTrimAutoWhitespace,
    //    bool computeUndoEdits);
}

//public class ApplyEditsResult
//{
//    public required IValidEditOperation[]? ReverseEdits { get; init; }
//    public required IInternalModelContentChange[] Changes { get; init; }
//    public required int[]? TrimAutoWhitespaceLineNumbers { get; init; }
//}

//public interface IValidEditOperation
//{
//    /**
//     * An identifier associated with this single edit operation.
//     * @internal
//     */
//    ISingleEditOperationIdentifier? Identifier { get; }
//    /**
//     * The range to replace. This can be empty to emulate a simple insert.
//     */
//    Range Range { get; }
//    /**
//     * The text to replace with. This can be empty to emulate a simple delete.
//     */
//    string Text { get; }
//    /**
//     * @internal
//     */
//    TextChange TextChange { get; }
//}

//public interface IInternalModelContentChange : IModelContentChange
//{
//    Range Range { get; }
//    bool forceMoveMarkers { get; }
//}

//public class ValidAnnotatedEditOperation
//{
//    public required ISingleEditOperationIdentifier? Identifier { get; init; }
//    public required Range Range { get; init; }
//    public required string? Text { get; init; }
//    public required bool ForceMoveMarkers { get; init; }
//    public required bool IsAutoWhitespaceEdit { get; init; }
//    public required bool IsTracked { get; init; }
//}

//public interface ISingleEditOperationIdentifier
//{
//    /**
//     * Identifier major
//     */
//    int Major { get; }
//    /**
//     * Identifier minor
//     */
//    int Minor { get; }
//}