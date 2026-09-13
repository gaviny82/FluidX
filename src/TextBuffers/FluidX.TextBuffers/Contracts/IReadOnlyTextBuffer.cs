namespace FluidX.TextBuffers;

/// <summary>
/// Represents a read-only view of a text buffer that provides access to the raw text content of a document.
/// </summary>
public interface IReadOnlyTextBuffer : IEquatable<IReadOnlyTextBuffer>
{
    /**
     * Definitions:
     * - Length: string length of a piece of text (Unicode characters outside the BMP have a length of 2).
     * - Line number: number of lines from the start of the document (the first line in the document has line number 1).
     * - Column number: length of characters from the start of the line (the first character in a line has column number 1),
     *                  including line break characters ("\r\n" has length 2, "\n" and "\r" have length 1),
     *                  high surrogate characters occupy 2 columns.
     * - Offset: length of characters from the first character in the document (the first character in the document has offset 0),
     *           including end-of-line sequences in previous lines ("\r\n" has length 2, "\n" and "\r" have length 1).
     * - Character count: number of Unicode characters in a piece of text (Unicode characters outside the BMP have a count of 1).
     */

    #region Document properties

    /// <summary>
    /// Total number of characters in the document, including end-of-line sequences. High surrogate characters occupy a length of 2.
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
    // This replaces GetCharacterCountInRange, GetLineFirstNonWhitespaceColumn, GetLineLastNonWhitespaceColumn, etc.

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
    /// <param name="lineNumber">Line number of the line.</param>
    /// <returns>Content of the line at <paramref name="lineNumber"/>.</returns>
    string GetLineContent(int lineNumber);

    /// <summary>
    /// Get the end-of-line sequence of a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line.</param>
    /// <returns>The end-of-line sequence of the line at <paramref name="lineNumber"/>, either CR, LF, CRLF, or empty for the final line.</returns>
    string GetLineEOL(int lineNumber);

    /// <summary>
    /// Get the length of characters in a line, excluding the end-of-line sequence.
    /// </summary>
    /// <param name="lineNumber">Line number of the line.</param>
    /// <returns>Length of characters in line <paramref name="lineNumber"/>.</returns>
    int GetLineLength(int lineNumber);

    /// <summary>
    /// Get the first non-whitespace column number in a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <returns>Column number of the first non-whitespace character</returns>
    int GetLineFirstNonWhitespaceColumn(int lineNumber);

    /// <summary>
    /// Get the last non-whitespace column number in a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <returns>Column number of the last non-whitespace character</returns>
    int GetLineLastNonWhitespaceColumn(int lineNumber);

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
