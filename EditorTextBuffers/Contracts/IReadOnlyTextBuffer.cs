namespace EditorTextBuffers.Contracts;

// `lineNumber` is 1 based.
public interface IReadOnlyTextBuffer : IEquatable<IReadOnlyTextBuffer>
{
    event EventHandler? OnDpiChangeContent;

    // Document properties
    string BOM { get; }
    string EOL { get; }
    bool MightContainRTL { get; }
    bool MightContainUnusualLineTerminators { get; }
    void ResetMightContainUnusualLineTerminators();
    bool MightContainNonBasicASCII { get; }

    // Position and Range
    int GetOffsetAt(int lineNumber, int column);
    Position GetPositionAt(int offset);
    Range GetRangeAt(int offset, int length);

    // Text operations
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
