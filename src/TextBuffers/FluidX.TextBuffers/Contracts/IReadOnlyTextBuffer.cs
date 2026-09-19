namespace FluidX.TextBuffers;

/// <summary>
/// Represents a read-only view of a text buffer that provides access to the raw text content of a document.
/// </summary>
public interface IReadOnlyTextBuffer : IEquatable<IReadOnlyTextBuffer>
{
    /**
     * Definitions:
     * - Length: string length of a piece of text (Unicode characters outside the BMP have a length of 2).
     * - Line index: number of lines from the start of the document (the first line in the document has line index 0).
     * - Column index: length of characters from the start of the line (the first character in a line has column index 0),
     *                  including line break characters ("\r\n" has length 2, "\n" and "\r" have length 1),
     *                  a surrogate pair occupies two UTF-16 column indices.
     * - Offset: length of characters from the first character in the document (the first character in the document has offset 0),
     *           including end-of-line sequences in previous lines ("\r\n" has length 2, "\n" and "\r" have length 1).
     * - Character count: number of Unicode characters in a piece of text (Unicode characters outside the BMP have a count of 1).
     * - Line breaks are derived solely from the current raw text. CRLF is recognized greedily as one line break;
     *   a CR or LF not participating in CRLF is recognized as its own line break. Implementations do not retain
     *   logical line identities across edits.
     * - Every UTF-16 boundary is addressable by coordinate conversion and ranges, including the boundary between
     *   CR and LF in a CRLF sequence. <see cref="ITextBuffer.ApplyEdits(TextReplacement[])"/> places an additional
     *   restriction on edit endpoints at that boundary.
     */

    #region Document properties

    /// <summary>
    /// Total number of characters in the document, including end-of-line sequences. A surrogate pair has length 2.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Number of lines in the document.
    /// </summary>
    int LineCount { get; }

    #endregion

    #region Position, Range and Offset Conversions

    /// <summary>
    /// Get the length of characters from the first character in the document of a character at the
    /// specified position, including end-of-line sequences in previous lines ("\r\n" has length 2,
    /// "\n" and "\r" have length 1).
    /// </summary>
    /// <param name="position">Position of the specified character.</param>
    /// <returns>Length of characters from the first character in the document.</returns>
    int GetOffsetAt(TextPosition position);

    /// <summary>
    /// Get the <see cref="TextPosition"/> corresponding to an <paramref name="offset"/> in the document.
    /// </summary>
    /// <param name="offset">Length of characters from the first character in the document (the first character in the document has offset 0),
    /// including end-of-line sequences in previous lines("\r\n" has length 2, "\n" and "\r" have length 1).</param>
    /// <returns><see cref="TextPosition"/> of the character at the given <paramref name="offset"/>.</returns>
    TextPosition GetPositionAt(int offset);

    /// <summary>
    /// Get the <see cref="TextRange"/> of corresponding to a text of a given <paramref name="length"/> at a given <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">Offset of the first character to be included in the range.</param>
    /// <param name="length">Length of the text to be included from the given <paramref name="offset"/>.</param>
    /// <returns>The <see cref="TextRange"/> of the text specified.</returns>
    TextRange GetRangeAt(int offset, int length);

    #endregion

    #region Text operations

    // TODO: Consider adding a general API that iterates over a range of text with zero allocation and compute a property from the text range.
    // This replaces GetCharacterCountInRange, GetLineFirstNonWhitespaceColumnIndex, GetLineLastNonWhitespaceColumnIndex, etc.

    /// <summary>
    /// Get the text in a <see cref="TextRange"/>.
    /// </summary>
    /// <param name="range">Range of the text.</param>
    /// <returns>Text in the specified <paramref name="range"/>.</returns>
    string GetTextInRange(TextRange range);

    /// <summary>
    /// Get the length of text in a <see cref="TextRange"/>.
    /// </summary>
    /// <param name="range">Range of the text.</param>
    /// <returns>Length of text in <paramref name="range"/>.</returns>
    int GetTextLengthInRange(TextRange range);

    /// <summary>
    /// Get all lines in the document as a list of <see langword="string"/>.
    /// The end-of-line sequence is not included in the content of each line.
    /// </summary>
    /// <returns>A read-only view of the list of lines.</returns>
    IReadOnlyList<string> GetLinesContent();

    /// <summary>
    /// Get the content of a line, excluding the end-of-line sequence.
    /// </summary>
    /// <param name="lineIndex">Line index of the line.</param>
    /// <returns>Content of the line at <paramref name="lineIndex"/>.</returns>
    string GetLineContent(int lineIndex);

    /// <summary>
    /// Get the end-of-line sequence of a line.
    /// </summary>
    /// <param name="lineIndex">Line index of the line.</param>
    /// <returns>The end-of-line sequence of the line at <paramref name="lineIndex"/>, either CR, LF, CRLF, or empty for the final line.</returns>
    string GetLineEOL(int lineIndex);

    /// <summary>
    /// Get the length of characters in a line, excluding the end-of-line sequence.
    /// </summary>
    /// <param name="lineIndex">Line index of the line.</param>
    /// <returns>Length of characters in line <paramref name="lineIndex"/>.</returns>
    int GetLineLength(int lineIndex);

    /// <summary>
    /// Get the first non-whitespace column index in a line.
    /// </summary>
    /// <param name="lineIndex">Line index of the line</param>
    /// <returns>Zero-based index of the first non-whitespace character, or -1 if none exists.</returns>
    int GetLineFirstNonWhitespaceColumnIndex(int lineIndex);

    /// <summary>
    /// Get the exclusive end column index of the last non-whitespace character in a line.
    /// </summary>
    /// <param name="lineIndex">Line index of the line</param>
    /// <returns>Exclusive zero-based end index of the last non-whitespace character, or -1 if none exists.</returns>
    int GetLineLastNonWhitespaceColumnIndex(int lineIndex);

    /// <summary>
    /// Get the <see langword="char"/> at a given <see cref="TextPosition"/>.
    /// </summary>
    /// <param name="position">Position of the character.</param>
    /// <returns>The <see langword="char"/> at the given <paramref name="position"/>.</returns>
    char GetChar(TextPosition position);

    /// <summary>
    /// Get the <see langword="char"/> at a given <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">Offset of the character.</param>
    /// <returns>The <see langword="char"/> at the given <paramref name="offset"/>.</returns>
    char GetChar(int offset);

    #endregion

    // TODO: Review

    ITextSnapshot CreateSnapshot(bool preserveBOM);

    // TODO: Review

    IReadOnlyList<FindMatch> FindMatchesLineByLine(
        TextRange searchRange,
        SearchData searchData,
        bool captureMatches,
        int limitResultCount);
}
