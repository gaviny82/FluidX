namespace FluidX.TextBuffers;

public interface ITextBuffer : IReadOnlyTextBuffer
{
    /// <summary>
    /// Rewrites the buffer's content so that every line break uses the given
    /// end-of-line sequence. Either "\r\n" or "\n".
    /// </summary>
    void NormalizeEOL(string eol);

    /// <summary>
    /// Applies replacement text verbatim. Ranges refer to the pre-edit document.
    /// Callers must supply valid, non-overlapping ranges sorted ascending by
    /// <see cref="TextRange.CompareRangesUsingEnds"/>, preserving input order for ties.
    /// Touching ranges and empty replacements are allowed. Insertions at the same
    /// position appear in array order. Callers must validate before calling;
    /// violations have no guaranteed result or exception.
    /// </summary>
    /// <param name="replacements">The replacement range and text to apply.</param>
    void ApplyEdits(TextReplacement[] replacements);
}
