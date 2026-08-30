namespace FluidX.TextBuffers;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    /// <summary>
    /// Rewrites the buffer's content so that every line break uses the given
    /// end-of-line sequence. Either "\r\n" or "\n".
    /// </summary>
    void NormalizeEOL(string eol);

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
