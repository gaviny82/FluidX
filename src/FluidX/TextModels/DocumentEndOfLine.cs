namespace FluidX.TextModels;

/// <summary>
/// Represents the current end-of-line sequence of a document.
/// </summary>
public enum DocumentEndOfLine
{
    /// <summary>No line endings are present in the document.</summary>
    Unknown,
    CR,
    LF,
    CRLF,
    /// <summary>More than one kind of line ending is present in the document.</summary>
    Mixed
}
