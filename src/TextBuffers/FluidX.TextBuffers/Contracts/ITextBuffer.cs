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
    /// After applying all replacements to the raw UTF-16 content, line structure is
    /// derived again from the final text. CRLF is recognized as one line break even
    /// when the CR and LF became adjacent across an edit boundary. A replacement's
    /// start or end must not be positioned between the CR and LF characters of an
    /// existing CRLF sequence. Such input has undefined behavior and must be rejected
    /// by the caller. Replacement text may contain arbitrary CR, LF, and CRLF sequences.
    /// </summary>
    /// <param name="replacements">The replacement range and text to apply.</param>
    void ApplyEdits(TextReplacement[] replacements);
}
