namespace EditorTextBuffers.Contracts;

// `lineNumber` is 1 based.
public interface IReadOnlyTextBuffer : IEquatable<IReadOnlyTextBuffer>
{
    event EventHandler? OnDpiChangeContent;

    #region Document properties

    /// <summary>
    /// The Byte Order Mark of the document (either <see cref="string.Empty"/> or U+FEFF)
    /// </summary>
    string BOM { get; }

    /// <summary>
    /// The End of Line sequence of the document (either CRLF, CR or LF)
    /// </summary>
    string EOL { get; }

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
    /// Get the offset of a position from the start of the document.
    /// </summary>
    /// <param name="lineNumber">Line number (1-based) of the position</param>
    /// <param name="column">Column number (1-based) of the position</param>
    /// <returns>Offset from the start of the document.</returns>
    int GetOffsetAt(int lineNumber, int column);

    /// <summary>
    /// Get the <see cref="Position"/> at an <paramref name="offset"/> from the start of the document
    /// </summary>
    /// <param name="offset">offset from the start of the document</param>
    /// <returns><see cref="Position"/> corresponding to <paramref name="offset"/>.</returns>
    Position GetPositionAt(int offset);

    /// <summary>
    /// Get the <see cref="Range"/> of given <paramref name="length"/> at an <paramref name="offset"/> from the start of the document.
    /// </summary>
    /// <param name="offset">Offset from the start of the document</param>
    /// <param name="length">Length of the <see cref="Range"/></param>
    /// <returns><see cref="Range"/> of <paramref name="length"/> from the given <paramref name="offset"/>.</returns>
    Range GetRangeAt(int offset, int length);

    #endregion

    #region Text operations

    string GetValueInRange(Range range, EndOfLinePreference eol);
    ITextSnapshot CreateSnapshot(bool preserveBOM);
    int GetValueLengthInRange(Range range, EndOfLinePreference eol);
    int GetCharacterCountInRange(Range range, EndOfLinePreference eol);
    int Length { get; }
    int LineCount { get; }
    IReadOnlyList<string> GetLinesContent();
    string GetLineContent(int lineNumber);
    char GetLineCharCode(int lineNumber, int index);
    char GetCharCode(int offset);
    int GetLineLength(int lineNumber);
    int GetLineMinColumn(int lineNumber);
    int GetLineMaxColumn(int lineNumber);
    int GetLineFirstNonWhitespaceColumn(int lineNumber);
    int GetLineLastNonWhitespaceColumn(int lineNumber);

    #endregion

    // TODO: Implement searchable text buffer

    //FindMatch[] FindMatchesLineByLine(
    //    Range searchRange,
    //    SearchData searchData,
    //    bool captureMatches,
    //    int limitResultCount);
}

//public class SearchData
//{
//    public required Regex Regex { get; init; }
//    public required WordCharacterClassifier? WordSeparators { get; init; }
//    public required string? SimpleSearch { get; init; }
//}

//public class FindMatch
//{
//    public required Range Range { get; init; }
//    public required string[] Matches { get; init; }
//}
