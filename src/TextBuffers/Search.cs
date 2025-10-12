using System.Text.RegularExpressions;

namespace FluidX.TextBuffers;

/// <summary>
/// A fast character classifier that uses a compact array for ASCII values.
/// </summary>
public class CharacterClassifier<TClass>
{
    /// <summary>
    /// Maintain a compact (fully initialized ASCII map for quickly classifying ASCII characters - used more often in code).
    /// </summary>
    protected readonly TClass[] _asciiMap;

    /// <summary>
    /// The entire map (sparse array).
    /// </summary>
    protected readonly Dictionary<char, TClass> _map = [];

    protected readonly TClass _defaultValue;

    public TClass this[char ch]
    {
        get
        {
            if (ch >= 0 && ch < 256)
                return _asciiMap[ch];
            else
                return _map.TryGetValue(ch, out var value) ? value : _defaultValue;
        }
        set
        {
            if (ch >= 0 && ch < 256)
                _asciiMap[ch] = value;
            else
                _map[ch] = value;
        }
    }

    public CharacterClassifier(TClass defaultValue)
    {
        _defaultValue = defaultValue;
        _asciiMap = new TClass[256];
        Array.Fill(_asciiMap, defaultValue);
    }

    public void Clear()
    {
        Array.Fill(_asciiMap, _defaultValue);
        _map.Clear();
    }
}

public enum WordCharacterClass
{
    Regular = 0,
    Whitespace = 1,
    WordSeparator = 2
}

public class WordCharacterClassifier : CharacterClassifier<WordCharacterClass>
{
    public WordCharacterClassifier(ReadOnlySpan<char> wordSeparators) : base(WordCharacterClass.Regular)
    {
        foreach (char separator in wordSeparators)
            this[separator] = WordCharacterClass.WordSeparator;
        this[' '] = WordCharacterClass.Whitespace;
        this['\t'] = WordCharacterClass.Whitespace;
    }
}

public class SearchData
{
    /// <summary>
    /// The regex to search for. Always defined.
    /// </summary>
    public Regex Regex { get; }

    /// <summary>
    /// The word separator classifier.
    /// </summary>
    public WordCharacterClassifier? WordSeparators { get; }

    /// <summary>
    /// The simple string to search for (if possible).
    /// </summary>
    public string? SimpleSearch { get; }

    public SearchData(Regex regex, WordCharacterClassifier? wordSeparators, string? simpleSearch)
    {
        Regex = regex;
        WordSeparators = wordSeparators;
        SimpleSearch = simpleSearch;
    }
}

public class FindMatch
{
    public Range Range { get; }
    public string[]? Matches { get; }

    public FindMatch(Range range, string[]? matches)
    {
        Range = range;
        Matches = matches;
    }
}

internal static class SearchUtils
{
    public static bool IsValidMatch(WordCharacterClassifier wordSeparators, string text, int textLength, int matchStartIndex, int matchLength)
    {
        return LeftIsWordBoundary(wordSeparators, text, textLength, matchStartIndex, matchLength)
            && RightIsWordBoundary(wordSeparators, text, textLength, matchStartIndex, matchLength);
    }

    private static bool LeftIsWordBoundary(WordCharacterClassifier wordSeparators, string text, int textLength, int matchStartIndex, int matchLength)
    {
        if (matchStartIndex == 0) // Match starts at start of string
            return true;

        char charBefore = text[matchStartIndex - 1];
        if (wordSeparators[charBefore] != WordCharacterClass.Regular)
            return true;

        if (charBefore == '\r' || charBefore == '\n')
            return true;

        if (matchLength > 0)
        {
            char firstCharInMatch = text[matchStartIndex];
            if (wordSeparators[firstCharInMatch] != WordCharacterClass.Regular)
                return true;
        }

        return false;
    }

    private static bool RightIsWordBoundary(WordCharacterClassifier wordSeparators, string text, int textLength, int matchStartIndex, int matchLength)
    {
        if (matchStartIndex + matchLength == textLength) // Match ends at end of string
            return true;

        char charAfter = text[matchStartIndex + matchLength];
        if (wordSeparators[charAfter] != WordCharacterClass.Regular)
            return true;

        if (charAfter == '\r' || charAfter == '\n')
            return true;

        if (matchLength > 0)
        {
            char lastCharInMatch = text[matchStartIndex + matchLength - 1];
            if (wordSeparators[lastCharInMatch] != WordCharacterClass.Regular)
                return true;
        }

        return false;
    }

    public static FindMatch CreateFindMatch(Range range, Match[] rawMatches, bool captureMatches)
    {
        if (!captureMatches)
            return new FindMatch(range, null);

        string[] matches = new string[rawMatches.Length];
        for (int i = 0; i < rawMatches.Length; i++)
        {
            matches[i] = rawMatches[i].Value;
        }
        return new FindMatch(range, matches);
    }
}

/// <summary>
/// A regex searcher that can respect word boundaries using a WordCharacterClassifier.
/// </summary>
public class Searcher
{
    internal readonly WordCharacterClassifier? _wordSeparators;
    private readonly Regex _searchRegex;
    private int _prevMatchStartIndex;
    private int _prevMatchLength;

    public Searcher(WordCharacterClassifier? wordSeparators, Regex searchRegex)
    {
        _wordSeparators = wordSeparators;
        _searchRegex = searchRegex;
        _prevMatchStartIndex = -1;
        _prevMatchLength = 0;
    }

    public void Reset(int lastIndex)
    {
        _prevMatchStartIndex = -1;
        _prevMatchLength = 0;
    }

    public Match? Next(string text)
    {
        int textLength = text.Length;
        Match? match = null;

        do
        {
            if (_prevMatchStartIndex + _prevMatchLength == textLength)
            {
                // Reached end of string
                return null;
            }

            match = _searchRegex.Match(text, _prevMatchStartIndex + Math.Max(_prevMatchLength, 0));
            if (!match.Success)
                return null;

            int matchStartIndex = match.Index;
            int matchLength = match.Length;

            // Prevent infinite loop if regex matches the same span repeatedly
            if (matchStartIndex == _prevMatchStartIndex && matchLength == _prevMatchLength)
            {
                if (matchLength == 0)
                {
                    // the search result is an empty string and won't advance `regex.lastIndex`, so `regex.exec` will stuck here
                    // we attempt to recover from that by advancing by two if surrogate pair found and by one otherwise

                    // Advance by 1 (or 2 if surrogate pair)
                    if (char.IsSurrogatePair(text, matchStartIndex))
                        _prevMatchStartIndex += 2;
                    else
                        _prevMatchStartIndex += 1;
                    continue;
                }
                // Exit early if the regex matches the same range twice
                return null;
            }

            _prevMatchStartIndex = matchStartIndex;
            _prevMatchLength = matchLength;

            if (_wordSeparators is null ||
                SearchUtils.IsValidMatch(_wordSeparators, text, textLength, matchStartIndex, matchLength))
            {
                return match;
            }

            // Otherwise, keep searching
        } while (match is null);

        return null;
    }
}
