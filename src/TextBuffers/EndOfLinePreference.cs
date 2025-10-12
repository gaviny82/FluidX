namespace FluidX.TextBuffers;

public enum EndOfLinePreference
{
    /**
    * Use the end of line character identified in the text buffer.
    */
    TextDefined = 0,
    /**
    * Use line feed (\n) as the end of line character.
    */
    LF = 1,
    /**
    * Use carriage return and line feed (\r\n) as the end of line character.
    */
    CRLF = 2
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
