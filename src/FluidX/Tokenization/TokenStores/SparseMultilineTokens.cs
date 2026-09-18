using FluidX.TextBuffers;
using System.Runtime.InteropServices;

namespace FluidX.Tokenization.TokenStores;

/// <summary>
/// Represents sparse tokens over a contiguous range of lines.
/// </summary>
public class SparseMultilineTokens
{
    private readonly SparseMultilineTokenStorage _tokens;

    /// <summary>
    /// (Inclusive) start line index for these tokens.
    /// </summary>
    public int StartLineIndex { get; private set; }

    /// <summary>
    /// (Inclusive) end line index for these tokens.
    /// </summary>
    public int EndLineIndex => StartLineIndex + _tokens.MaxDeltaLine;
    public bool IsEmpty => _tokens.IsEmpty;

    public SparseMultilineTokens(int startLineIndex, SparseMultilineTokenStorage tokens)
    {
        StartLineIndex = startLineIndex;
        _tokens = tokens;
    }

    public override string ToString()
        => _tokens.ToString(StartLineIndex);

    public SparseLineToken[]? GetLineTokens(int lineIndex)
    {
        if (StartLineIndex <= lineIndex && lineIndex <= EndLineIndex)
            return _tokens.GetLineTokens(lineIndex - StartLineIndex);
        return null;
    }

    public TextRange? GetRange()
    {
        if (_tokens.Range is not TextRange deltaRange)
            return null;
        return new(
            StartLineIndex + deltaRange.StartLineIndex,
            deltaRange.StartColumnIndex,
            StartLineIndex + deltaRange.EndLineIndex,
            deltaRange.EndColumnIndex
        );
    }

    public void RemoveTokens(TextRange range)
    {
        int startLineIndex = range.StartLineIndex - StartLineIndex;
        int endLineIndex = range.EndLineIndex - StartLineIndex;

        StartLineIndex += _tokens.RemoveTokens(
            startLineIndex,
            range.StartColumnIndex,
            endLineIndex,
            range.EndColumnIndex
        );
    }

    public (SparseMultilineTokens, SparseMultilineTokens) Split(TextRange range)
    {
        // split tokens to two:
        // a) all the tokens before `range`
        // b) all the tokens after `range`
        int startLineIndex = range.StartLineIndex - StartLineIndex;
        int endLineIndex = range.EndLineIndex - StartLineIndex;

        var (a, b, bDeltaLine) = _tokens.Split(
            startLineIndex,
            range.StartColumnIndex,
            endLineIndex,
            range.EndColumnIndex
        );
        return (
            new SparseMultilineTokens(StartLineIndex, a),
            new SparseMultilineTokens(StartLineIndex + bDeltaLine, b)
        );
    }

    public void ApplyEdit(TextRange range, string text)
    {
        var (eolCount, firstlineLength, lastLineLength, _) = EOLCounter.CountEOL(text);
        AcceptEdit(range, eolCount, firstlineLength, lastLineLength, text.Length > 0 ? text[0] : '\0');
    }

    public void AcceptEdit(TextRange range, int eolCount, int firstLineLength, int lastLineLength, char firstCharCode)
    {
        AcceptDeleteRange(range);
        AcceptInsertText(
            new TextPosition(range.StartLineIndex, range.StartColumnIndex),
            eolCount,
            firstLineLength,
            lastLineLength,
            firstCharCode
        );
    }

    private void AcceptDeleteRange(TextRange range)
    {
        if (range.IsEmpty)
            return; // Nothing to delete

        int firstLineIndex = range.StartLineIndex - StartLineIndex;
        int lastLineIndex = range.EndLineIndex - StartLineIndex;

        if (lastLineIndex < 0)
        {
            // this deletion occurs entirely before this block, so we only need to adjust line indices
            int deletedLinesCount = lastLineIndex - firstLineIndex;
            StartLineIndex -= deletedLinesCount;
            return;
        }

        int tokenMaxDeltaLine = _tokens.MaxDeltaLine;

        if (firstLineIndex >= tokenMaxDeltaLine + 1)
            return; // this deletion occurs entirely after this block, so there is nothing to do

        if (firstLineIndex < 0 && lastLineIndex >= tokenMaxDeltaLine + 1)
        {
            // this deletion completely encompasses this block
            StartLineIndex = 0;
            _tokens.Clear();
            return;
        }

        if (firstLineIndex < 0)
        {
            int deletedBefore = -firstLineIndex;
            StartLineIndex -= deletedBefore;

            _tokens.AcceptDeleteRange(range.StartColumnIndex, 0, 0, lastLineIndex, range.EndColumnIndex);
        }
        else
        {
            _tokens.AcceptDeleteRange(0, firstLineIndex, range.StartColumnIndex, lastLineIndex, range.EndColumnIndex);
        }
    }

    private void AcceptInsertText(TextPosition position, int eolCount, int firstLineLength, int lastLineLength, char firstCharCode)
    {
        if (eolCount == 0 && firstLineLength == 0)
            return; // Nothing to insert

        int lineIndex = position.LineIndex - StartLineIndex;

        if (lineIndex < 0)
        {
            // this insertion occurs before this block, so we only need to adjust line indices
            StartLineIndex += eolCount;
            return;
        }

        int tokenMaxDeltaLine = _tokens.MaxDeltaLine;

        if (lineIndex >= tokenMaxDeltaLine + 1)
        {
            // this insertion occurs after this block, so there is nothing to do
            return;
        }

        _tokens.AcceptInsertText(lineIndex, position.ColumnIndex, eolCount, firstLineLength, lastLineLength, firstCharCode);
    }
}


public class SparseMultilineTokenStorage
{
    private readonly List<SparseLineToken> _tokens;

    public bool IsEmpty => _tokens.Count == 0;
    public int TokenCount => _tokens.Count;
    public int MaxDeltaLine => TokenCount == 0 ? -1 : _tokens[^1].DeltaLine;
    public TextRange? Range => TokenCount == 0 ? null : new(
        0,
        _tokens[0].StartIndex,
        MaxDeltaLine,
        _tokens[^1].EndIndex
    );

    public SparseMultilineTokenStorage(List<SparseLineToken> tokens)
    {
        _tokens = tokens;
    }

    public string ToString(int startLineIndex)
    {
        List<string> pieces = new(TokenCount);
        foreach (var token in _tokens)
        {
            pieces.Add($"{token.DeltaLine + startLineIndex},{token.StartIndex}-{token.EndIndex}");
        }
        return $"[{string.Join(",", pieces)}]";
    }

    public SparseLineToken[]? GetLineTokens(int deltaLine)
    {
        int low = 0;
        int high = TokenCount - 1;
        while(low < high)
        {
            int mid = low + (high - low) / 2;
            int midDeltaLine = _tokens[mid].DeltaLine;

            if (midDeltaLine < deltaLine)
            {
                low = mid + 1;
            }
            else if (midDeltaLine > deltaLine)
            {
                high = mid - 1;
            }
            else
            {
                int min = mid;
                while (min > low && _tokens[min-1].DeltaLine == deltaLine)
                    min--;

                int max = mid;
                while (max < high && _tokens[max + 1].DeltaLine == deltaLine)
                    max++;

                return CollectionsMarshal.AsSpan(_tokens)[min..(max + 1)].ToArray();
            }
        }
        if (_tokens[low].DeltaLine == deltaLine)
            return CollectionsMarshal.AsSpan(_tokens).Slice(low, 1).ToArray();
        return null;
    }

    public void Clear() => _tokens.Clear();

    public int RemoveTokens(int startDeltaLine, int startChar, int endDeltaLine, int endChar)
    {
        var tokens = _tokens;
        int tokenCount = TokenCount;
        int newTokenCount = 0;
        bool hasDeletedTokens = false;
        int firstDeltaLine = 0;
        for (int i = 0; i < tokenCount; i++)
        {
            int tokenDeltaLine = tokens[i].DeltaLine;
            int tokenStartCharacter = tokens[i].StartIndex;
            int tokenEndCharacter = tokens[i].EndIndex;
            LineTokenMetadata tokenMetadata = tokens[i].Metadata;

            if ((tokenDeltaLine > startDeltaLine || (tokenDeltaLine == startDeltaLine && tokenEndCharacter >= startChar))
                && (tokenDeltaLine < endDeltaLine || (tokenDeltaLine == endDeltaLine && tokenStartCharacter <= endChar)))
            {
                hasDeletedTokens = true;
            }
            else
            {
                if (newTokenCount == 0)
                    firstDeltaLine = tokenDeltaLine;
                if (hasDeletedTokens)
                {
                    // must move the token to the left
                    tokens[newTokenCount] = new SparseLineToken(
                        tokenDeltaLine - firstDeltaLine,
                        tokenStartCharacter,
                        tokenEndCharacter,
                        tokenMetadata);
                }
                else if (firstDeltaLine != 0)
                {
                    // must adjust the delta line in place
                    tokens[i] = tokens[i] with { DeltaLine = tokenDeltaLine - firstDeltaLine };
                }
                newTokenCount++;
            }
        }
        if (newTokenCount < _tokens.Count)
            _tokens.RemoveRange(newTokenCount, TokenCount - newTokenCount);
        return firstDeltaLine;
    }

    public (SparseMultilineTokenStorage, SparseMultilineTokenStorage, int) Split(
        int startDeltaLine,
        int startChar,
        int endDeltaLine,
        int endChar)
    {
        var tokens = _tokens;
        int tokenCount = TokenCount;
        List<SparseLineToken> aTokens = [];
        List<SparseLineToken> bTokens = [];
        List<SparseLineToken> destTokens = aTokens;
        int destOffset = 0;
        int destFirstDeltaLine = 0;
        for (int i = 0; i < tokenCount; i++)
        {
            int tokenDeltaLine = tokens[i].DeltaLine;
            int tokenStartCharacter = tokens[i].StartIndex;
            int tokenEndCharacter = tokens[i].EndIndex;
            LineTokenMetadata tokenMetadata = tokens[i].Metadata;

            if (tokenDeltaLine > startDeltaLine || (tokenDeltaLine == startDeltaLine && tokenEndCharacter >= startChar))
            {
                if (tokenDeltaLine < endDeltaLine || (tokenDeltaLine == endDeltaLine && tokenStartCharacter <= endChar))
                {
                    // this token is touching the range
                    continue;
                }
                else
                {
                    // this token is after the range
                    if (destTokens != bTokens)
                    {
                        // this token is the first token after the range
                        destTokens = bTokens;
                        destOffset = 0;
                        destFirstDeltaLine = tokenDeltaLine;
                    }
                }
            }
            destTokens[destOffset++] = new SparseLineToken(
                tokenDeltaLine - destFirstDeltaLine,
                tokenStartCharacter,
                tokenEndCharacter,
                tokenMetadata
            );
        }
        return (
            new SparseMultilineTokenStorage(aTokens),
            new SparseMultilineTokenStorage(bTokens),
            destFirstDeltaLine
        );
    }

    public void AcceptDeleteRange(
        int horizontalShiftForFirstLineTokens,
        int startDeltaLine,
        int startCharacter,
        int endDeltaLine,
        int endCharacter)
    {
        // This is a bit complex, here are the cases I used to think about this:
        //
        // 1. The token starts before the deletion range
        // 1a. The token is completely before the deletion range
        //               -----------
        //                          xxxxxxxxxxx
        // 1b. The token starts before, the deletion range ends after the token
        //               -----------
        //                      xxxxxxxxxxx
        // 1c. The token starts before, the deletion range ends precisely with the token
        //               ---------------
        //                      xxxxxxxx
        // 1d. The token starts before, the deletion range is inside the token
        //               ---------------
        //                    xxxxx
        //
        // 2. The token starts at the same position with the deletion range
        // 2a. The token starts at the same position, and ends inside the deletion range
        //               -------
        //               xxxxxxxxxxx
        // 2b. The token starts at the same position, and ends at the same position as the deletion range
        //               ----------
        //               xxxxxxxxxx
        // 2c. The token starts at the same position, and ends after the deletion range
        //               -------------
        //               xxxxxxx
        //
        // 3. The token starts inside the deletion range
        // 3a. The token is inside the deletion range
        //                -------
        //             xxxxxxxxxxxxx
        // 3b. The token starts inside the deletion range, and ends at the same position as the deletion range
        //                ----------
        //             xxxxxxxxxxxxx
        // 3c. The token starts inside the deletion range, and ends after the deletion range
        //                ------------
        //             xxxxxxxxxxx
        //
        // 4. The token starts after the deletion range
        //                  -----------
        //          xxxxxxxx
        //
        var tokens = _tokens;
        int tokenCount = TokenCount;
        int deletedLineCount = (endDeltaLine - startDeltaLine);
        int newTokenCount = 0;
        bool hasDeletedTokens = false;
        for (int i = 0; i < tokenCount; i++)
        {
            int tokenDeltaLine = tokens[i].DeltaLine;
            int tokenStartCharacter = tokens[i].StartIndex;
            int tokenEndCharacter = tokens[i].EndIndex;
            LineTokenMetadata tokenMetadata = tokens[i].Metadata;

            if (tokenDeltaLine < startDeltaLine || (tokenDeltaLine == startDeltaLine && tokenEndCharacter <= startCharacter))
            {
                // 1a. The token is completely before the deletion range
                // => nothing to do
                newTokenCount++;
                continue;
            }
            else if (tokenDeltaLine == startDeltaLine && tokenStartCharacter < startCharacter)
            {
                // 1b, 1c, 1d
                // => the token survives, but it needs to shrink
                if (tokenDeltaLine == endDeltaLine && tokenEndCharacter > endCharacter)
                {
                    // 1d. The token starts before, the deletion range is inside the token
                    // => the token shrinks by the deletion character count
                    tokenEndCharacter -= (endCharacter - startCharacter);
                }
                else
                {
                    // 1b. The token starts before, the deletion range ends after the token
                    // 1c. The token starts before, the deletion range ends precisely with the token
                    // => the token shrinks its ending to the deletion start
                    tokenEndCharacter = startCharacter;
                }
            }
            else if (tokenDeltaLine == startDeltaLine && tokenStartCharacter == startCharacter)
            {
                // 2a, 2b, 2c
                if (tokenDeltaLine == endDeltaLine && tokenEndCharacter > endCharacter)
                {
                    // 2c. The token starts at the same position, and ends after the deletion range
                    // => the token shrinks by the deletion character count
                    tokenEndCharacter -= (endCharacter - startCharacter);
                }
                else
                {
                    // 2a. The token starts at the same position, and ends inside the deletion range
                    // 2b. The token starts at the same position, and ends at the same position as the deletion range
                    // => the token is deleted
                    hasDeletedTokens = true;
                    continue;
                }
            }
            else if (tokenDeltaLine < endDeltaLine || (tokenDeltaLine == endDeltaLine && tokenStartCharacter < endCharacter))
            {
                // 3a, 3b, 3c
                if (tokenDeltaLine == endDeltaLine && tokenEndCharacter > endCharacter)
                {
                    // 3c. The token starts inside the deletion range, and ends after the deletion range
                    // => the token moves to continue right after the deletion
                    tokenDeltaLine = startDeltaLine;
                    tokenStartCharacter = startCharacter;
                    tokenEndCharacter = tokenStartCharacter + (tokenEndCharacter - endCharacter);
                }
                else
                {
                    // 3a. The token is inside the deletion range
                    // 3b. The token starts inside the deletion range, and ends at the same position as the deletion range
                    // => the token is deleted
                    hasDeletedTokens = true;
                    continue;
                }
            }
            else if (tokenDeltaLine > endDeltaLine)
            {
                // 4. (partial) The token starts after the deletion range, on a line below...
                if (deletedLineCount == 0 && !hasDeletedTokens)
                {
                    // early stop, there is no need to walk all the tokens and do nothing...
                    newTokenCount = tokenCount;
                    break;
                }
                tokenDeltaLine -= deletedLineCount;
            }
            else if (tokenDeltaLine == endDeltaLine && tokenStartCharacter >= endCharacter)
            {
                // 4. (continued) The token starts after the deletion range, on the last line where a deletion occurs
                if (horizontalShiftForFirstLineTokens != 0 && tokenDeltaLine == 0)
                {
                    tokenStartCharacter += horizontalShiftForFirstLineTokens;
                    tokenEndCharacter += horizontalShiftForFirstLineTokens;
                }
                tokenDeltaLine -= deletedLineCount;
                tokenStartCharacter -= (endCharacter - startCharacter);
                tokenEndCharacter -= (endCharacter - startCharacter);
            }
            else
            {
                throw new Exception("Not possible!");
            }

            tokens[newTokenCount] = new SparseLineToken(
                tokenDeltaLine,
                tokenStartCharacter,
                tokenEndCharacter,
                tokenMetadata
            );
            newTokenCount++;
        }
        if (newTokenCount < _tokens.Count)
            _tokens.RemoveRange(newTokenCount, _tokens.Count - newTokenCount);
    }

    public void AcceptInsertText(
        int deltaLine,
        int character,
        int eolCount,
        int firstLineLength,
        int lastLineLength,
        char firstCharCode)
    {
        // Here are the cases I used to think about this:
        //
        // 1. The token is completely before the insertion point
        //            -----------   |
        // 2. The token ends precisely at the insertion point
        //            -----------|
        // 3. The token contains the insertion point
        //            -----|------
        // 4. The token starts precisely at the insertion point
        //            |-----------
        // 5. The token is completely after the insertion point
        //            |   -----------
        //
        bool isInsertingPreciselyOneWordCharacter = (
            eolCount == 0
            && firstLineLength == 1
            && (
                (firstCharCode >= (char)CharCode.Digit0 && firstCharCode <= (char)CharCode.Digit9)
                || (firstCharCode >= (char)CharCode.A && firstCharCode <= (char)CharCode.Z)
                || (firstCharCode >= (char)CharCode.a && firstCharCode <= (char)CharCode.z)
            ));
        var tokens = _tokens;
        int tokenCount = TokenCount;
        for (int i = 0; i < tokenCount; i++)
        {
            int tokenDeltaLine = tokens[i].DeltaLine;
            int tokenStartCharacter = tokens[i].StartIndex;
            int tokenEndCharacter = tokens[i].EndIndex;

            if (tokenDeltaLine < deltaLine || (tokenDeltaLine == deltaLine && tokenEndCharacter < character))
            {
                // 1. The token is completely before the insertion point
                // => nothing to do
                continue;
            }
            else if (tokenDeltaLine == deltaLine && tokenEndCharacter == character)
            {
                // 2. The token ends precisely at the insertion point
                // => expand the end character only if inserting precisely one character that is a word character
                if (isInsertingPreciselyOneWordCharacter)
                {
                    tokenEndCharacter += 1;
                }
                else
                {
                    continue;
                }
            }
            else if (tokenDeltaLine == deltaLine && tokenStartCharacter < character && character < tokenEndCharacter)
            {
                // 3. The token contains the insertion point
                if (eolCount == 0)
                {
                    // => just expand the end character
                    tokenEndCharacter += firstLineLength;
                }
                else
                {
                    // => cut off the token
                    tokenEndCharacter = character;
                }
            }
            else
            {
                // 4. or 5.
                if (tokenDeltaLine == deltaLine && tokenStartCharacter == character)
                {
                    // 4. The token starts precisely at the insertion point
                    // => grow the token (by keeping its start constant) only if inserting precisely one character that is a word character
                    // => otherwise behave as in case 5.
                    if (isInsertingPreciselyOneWordCharacter)
                    {
                        continue;
                    }
                }
                // => the token must move and keep its size constant
                if (tokenDeltaLine == deltaLine)
                {
                    tokenDeltaLine += eolCount;
                    // this token is on the line where the insertion is taking place
                    if (eolCount == 0)
                    {
                        tokenStartCharacter += firstLineLength;
                        tokenEndCharacter += firstLineLength;
                    }
                    else
                    {
                        int tokenLength = tokenEndCharacter - tokenStartCharacter;
                        tokenStartCharacter = lastLineLength + (tokenStartCharacter - character);
                        tokenEndCharacter = tokenStartCharacter + tokenLength;
                    }
                }
                else
                {
                    tokenDeltaLine += eolCount;
                }
            }

            tokens[i] = new SparseLineToken(
                tokenDeltaLine,
                tokenStartCharacter,
                tokenEndCharacter,
                tokens[i].Metadata
            );
        }
    }
}

/// <summary>
/// A token for a sparse range of text.
/// </summary>
/// <param name="DeltaLine">Line index delta of the text range from a reference line</param>
/// <param name="StartIndex">Index of the starting character in its line</param>
/// <param name="EndIndex">Index of the ending character in its line</param>
/// <param name="Metadata">Metadata of the token</param>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public record struct SparseLineToken(int DeltaLine, int StartIndex, int EndIndex, LineTokenMetadata Metadata);
