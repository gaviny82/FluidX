namespace FluidX.TextBuffers;

public interface IReadOnlyTextBuffer : IEquatable<IReadOnlyTextBuffer>
{
    /**
     * Definitions:
     * - Length: string length of a piece of text (Unicode characters outside the BMP have a length of 2).
     * - Line number: number of lines from the start of the document (the first line in the document has line number 1).
     * - Column number: length of characters from the start of the document (the first character in a line has column number 1),
     *                  including line break characters ("\r\n" occupy 2 columns),
     *                  high surrogate characters occupy 2 columns.
     * - Offset: length of characters from the first character in the document (the first character in the document has offset 0),
     *           including line break characters ("\r\n" has length 2).
     * - Character count: number of Unicode characters in a piece of text (Unicode characters outside the BMP have a count of 1).
     */

    #region Document properties

    /// <summary>
    /// The Byte Order Mark of the document (either <see cref="string.Empty"/> or U+FEFF)
    /// </summary>
    string BOM { get; }

    /// <summary>
    /// The end-of-line sequence of the document (either CRLF, CR or LF)
    /// <para>If the document has mixed end-of-line sequences, the majority one is returned.</para>
    /// </summary>
    string GetEOL();

    /// <summary>
    /// <see langword="true"/> if the document might contain RTL characters
    /// </summary>
    bool MightContainRTL { get; }

    /// <summary>
    /// <see langword="true"/> if the document might contain LINE SEPARATOR (LS) or PARAGRAPH
    /// </summary>
    bool MightContainUnusualLineTerminators { get; }

    /// <summary>
    /// Reset the state of the buffer to indicate that it doesn't contain unusual line terminators
    /// </summary>
    void ResetMightContainUnusualLineTerminators();

    /// <summary>
    /// <see langword="true"/> if the document might contain non-ASCII content
    /// </summary>
    bool MightContainNonBasicASCII { get; }

    #endregion

    #region Position and Range

    /// <summary>
    /// Get the length of characters from the first character in the document of a specified character.
    /// </summary>
    /// <param name="lineNumber">Line number of the specified character</param>
    /// <param name="column">Column number of the specified character</param>
    /// <returns>Length of characters from the first character in the document.</returns>
    int GetOffsetAt(int lineNumber, int column);

    /// <summary>
    /// Get the <see cref="Position"/> at an <paramref name="offset"/> from the start of the document.
    /// </summary>
    /// <param name="offset">Length of characters from the first character in the document</param>
    /// <returns><see cref="Position"/> of the character at the given <paramref name="offset"/>.</returns>
    Position GetPositionAt(int offset);

    /// <summary>
    /// Get the <see cref="Range"/> of text of a given <paramref name="length"/> at a given <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">Length of characters from the first character in the document</param>
    /// <param name="length">Length of the text in the <see cref="Range"/></param>
    /// <returns>The <see cref="Range"/> of the text specified.</returns>
    Range GetRangeAt(int offset, int length);

    #endregion

    #region Text operations

    /// <summary>
    /// Get the text in a <see cref="Range"/> with the specified <see cref="EndOfLinePreference"/>.
    /// </summary>
    /// <param name="range"><see cref="Range"/> of text</param>
    /// <param name="eol">End-of-line preference</param>
    /// <returns>Text in <paramref name="range"/> with the specified <see cref="EndOfLinePreference"/>.<returns>
    string GetValueInRange(Range range, EndOfLinePreference eol = EndOfLinePreference.TextDefined);

    /// <summary>
    /// Get the length of text in a <see cref="Range"/> with the specified <see cref="EndOfLinePreference"/>.
    /// </summary>
    /// <param name="range">Range of text</param>
    /// <param name="eol">End-of-line preference</param>
    /// <returns>Length of text in <paramref name="range"/> with the specified <see cref="EndOfLinePreference"/>.</returns>
    int GetValueLengthInRange(Range range, EndOfLinePreference eol = EndOfLinePreference.TextDefined);

    /// <summary>
    /// Get the number of Unicode characters in a <see cref="Range"/> with the specified <see cref="EndOfLinePreference"/>.
    /// </summary>
    /// <param name="range">Range of text</param>
    /// <param name="eol">End-of-line preference</param>
    /// <returns>Number of characters in <paramref name="range"/> with the specified <see cref="EndOfLinePreference"/>.</returns>
    int GetCharacterCountInRange(Range range, EndOfLinePreference eol);

    /// <summary>
    /// Total length of characters in the document.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Number of lines in the document.
    /// </summary>
    int LineCount { get; }

    /// <summary>
    /// Get all lines in the document.
    /// </summary>
    /// <returns>A list of lines</returns>
    IReadOnlyList<string> GetLinesContent();

    /// <summary>
    /// Get the content of a line, excluding the end-of-line sequence.
    /// </summary>
    /// <param name="lineNumber">Line number of the line (starts from 1)</param>
    /// <returns>Content of the line at <paramref name="lineNumber"/>.</returns>
    string GetLineContent(int lineNumber);

    /// <summary>
    /// Get the <see langword="char"/> at <paramref name="index"/>.
    /// </summary>
    /// <param name="lineNumber">Line number of the character</param>
    /// <param name="index">Offset from the first character of the line</param>
    /// <returns>The <see langword="char"/> at offset <paramref name="index"/> in line <paramref name="lineNumber"/>.</returns>
    char GetLineCharCode(int lineNumber, int index);

    /// <summary>
    /// Get the <see langword="char"/> at <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">Offset from the first character of the document</param>
    /// <returns><see langword="char"/> at <paramref name="offset"/>.</returns>
    char GetCharCode(int offset);

    /// <summary>
    /// Get the length of characters in a line, excluding the end-of-line sequence.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <returns>Length of characters in line <paramref name="lineNumber"/>.</returns>
    int GetLineLength(int lineNumber);

    /// <summary>
    /// Get the minimum column number in a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <returns>Minimum column number of line <paramref name="lineNumber"/>.</returns>
    int GetLineMinColumn(int lineNumber);

    /// <summary>
    /// Get the maximum column number in a line.
    /// </summary>
    /// <param name="lineNumber">Line number of the line</param>
    /// <returns>Maximum column number in line <paramref name="lineNumber"/>.</returns>
    int GetLineMaxColumn(int lineNumber);

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

    #endregion

    ITextSnapshot CreateSnapshot(bool preserveBOM);

    IReadOnlyList<FindMatch> FindMatchesLineByLine(
        Range searchRange,
        SearchData searchData,
        bool captureMatches,
        int limitResultCount);
}
