namespace FluidX.TextBuffers;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    void SetEOL(string eol); // either "\r\n" or "\n"

    /// <summary>
    /// Applies a set of non-overlapping text replacements to the buffer.
    /// </summary>
    /// <param name="replacements">The text changes to apply.</param>
    /// <param name="computeUndoEdits">Whether to include inverse replacements in the result.</param>
    /// <returns>Result of the set of edit operations.</returns>
    ApplyEditsResult ApplyEdits(
        TextReplacement[] replacements,
        bool computeUndoEdits);
}
