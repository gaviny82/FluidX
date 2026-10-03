namespace FluidX.TextModels;

/// <summary>
/// Describes the current end-of-line state of a document.
/// </summary>
public enum DocumentEndOfLine
{
    /// <summary>No line endings are present in the document.</summary>
    Unknown,
    CR,
    LF,
    CRLF,
    /// <summary>The document might contain more than one kind of line ending.</summary>
    Mixed
}
