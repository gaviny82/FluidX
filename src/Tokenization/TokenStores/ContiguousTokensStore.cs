using System.Diagnostics.CodeAnalysis;

namespace FluidX.Tokenization.TokenStores;

/// <summary>
/// Represents contiguous tokens in a text model.
/// </summary>
public class ContiguousTokensStore
{
    private readonly List<LineToken[]?> _lineTokens = [];
    private readonly ILanguageIdCodec _languageIdCodec;

    public bool HasTokens => _lineTokens.Count > 0;

    public ContiguousTokensStore(ILanguageIdCodec languageIdCodec)
    {
        _languageIdCodec = languageIdCodec;
    }

    public void Flush()
    {
        _lineTokens.Clear();
    }

    public LineTokens GetTokens(string topLevelLanguageId, int lineIndex, string lineText)
    {
        LineToken[]? rawLineTokens = null;
        if (lineIndex< _lineTokens.Count)
            rawLineTokens = _lineTokens[lineIndex];
        if (rawLineTokens is not null && rawLineTokens != ContiguousTokensEditing.EmptyLineTokens)
            return new LineTokens(rawLineTokens, lineText, _languageIdCodec);

        LineToken[] lineTokens = [
            new LineToken
            {
                EndOffset = lineText.Length,
                Metadata = GetDefaultMetadata(_languageIdCodec.EncodeLanguageId(topLevelLanguageId))
            }
        ];
        return new LineTokens(lineTokens, lineText, _languageIdCodec);
    }

    private static LineToken[] MassageTokens(LanguageId topLevelLanguageId, int lineTextLength, LineToken[]? tokens)
    {
        if (lineTextLength == 0)
        {
            bool hasDifferentLanguageId = false;
            if (tokens is not null && tokens.Length > 1)
                hasDifferentLanguageId = tokens[1].Metadata.LanguageId != topLevelLanguageId;

            if (!hasDifferentLanguageId)
                return ContiguousTokensEditing.EmptyLineTokens;
        }

        if (tokens is null || tokens.Length == 0)
        {
            return [
                new LineToken()
                {
                    EndOffset = lineTextLength,
                    Metadata = GetDefaultMetadata(topLevelLanguageId)
                }
            ];
        }

        // Ensure the last token covers the end of the text
        tokens[^1] = tokens[^1] with { EndOffset = lineTextLength };
        return tokens;
    }

    private void EnsureLine(int lineIndex)
    {
        _lineTokens.Capacity = Math.Max(_lineTokens.Capacity, lineIndex + 1);
        while (lineIndex > _lineTokens.Count)
        {
            _lineTokens.Add(null);
        }
    }

    private void DeleteLines(int start, int deleteCount)
    {
        if (deleteCount == 0)
            return;
        if (start + deleteCount > _lineTokens.Count)
            deleteCount = _lineTokens.Count - start;
        _lineTokens.RemoveRange(start, deleteCount);
    }

    private void InsertLines(int insertAt, int insertCount)
    {
        if (insertCount == 0)
            return;
        _lineTokens.Capacity = Math.Max(_lineTokens.Capacity, _lineTokens.Count + insertCount);
        for (int i = 0; i < insertCount; i++)
        {
            _lineTokens.Insert(insertAt, null);
        }
    }

    public bool SetTokens(string topLevelLanguageid, int lineIndex, int lineTextLength, LineToken[]? tokens, bool checkEquality)
    {
        tokens = MassageTokens(
            _languageIdCodec.EncodeLanguageId(topLevelLanguageid),
            lineTextLength,
            tokens
        );

        EnsureLine(lineIndex);
        var oldTokens = _lineTokens[lineIndex];
        _lineTokens[lineIndex] = tokens;

        if (checkEquality)
        {
            return Equals(oldTokens, tokens);
        }
        return false;
    }

    private static bool Equals(LineToken[]? a, LineToken[]? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a.Length != b.Length)
            return false;
        return a.SequenceEqual(b);
    }

    public void AcceptEdit(TextRange range, int eolCount, int firstLineLength)
    {
        AcceptDeleteRange(range);
        AcceptInsertText(range.StartPosition, eolCount, firstLineLength);
    }

    private void AcceptDeleteRange(TextRange range)
    {
        int firstLineIndex = range.StartLineNumber - 1;
        if (firstLineIndex >= _lineTokens.Count)
            return;

        if (range.StartLineNumber == range.EndLineNumber)
        {
            if (range.StartColumn == range.EndColumn)
                return; // Nothing to delete

            _lineTokens[firstLineIndex] = ContiguousTokensEditing.Delete(
                _lineTokens[firstLineIndex],
                range.StartColumn - 1,
                range.EndColumn - 1
            );
            return;
        }

        _lineTokens[firstLineIndex] = ContiguousTokensEditing.DeleteEnding(_lineTokens[firstLineIndex], range.StartColumn - 1);

        int lastLineIndex = range.EndLineNumber - 1;
        LineToken[]? lastLineTokens = null;
        if (lastLineIndex < _lineTokens.Count)
            lastLineTokens = ContiguousTokensEditing.DeleteBeginning(_lineTokens[lastLineIndex], range.EndColumn - 1);

        // Take remaining text on last line and append it to remaining text on first line
        _lineTokens[firstLineIndex] = ContiguousTokensEditing.Append(_lineTokens[firstLineIndex], lastLineTokens);

        // Delete middle lines
        DeleteLines(range.StartLineNumber, range.EndLineNumber - range.StartLineNumber);
    }

    private void AcceptInsertText(TextPosition position, int eolCount, int firstLineLength)
    {
        if (eolCount == 0 && firstLineLength == 0)
            return; // Nothing to insert

        int lineIndex = position.LineNumber - 1;
        if (lineIndex >= _lineTokens.Count)
            return;

        if (eolCount == 0)
        {
            // Inserting text on one line
            _lineTokens[lineIndex] = ContiguousTokensEditing.Insert(_lineTokens[lineIndex], position.Column - 1, firstLineLength);
            return;
        }

        _lineTokens[lineIndex] = ContiguousTokensEditing.DeleteEnding(_lineTokens[lineIndex], position.Column - 1);
        _lineTokens[lineIndex] = ContiguousTokensEditing.Insert(_lineTokens[lineIndex], position.Column - 1, firstLineLength);

        InsertLines(position.LineNumber, eolCount);
    }

    //TODO: public LineTokenChangeRange[] SetMultilineTokens(ContiguousMultilineTokens tokens, ITextModel textModel)

    private static LineTokenMetadata GetDefaultMetadata(LanguageId topLevelLanguageId) => new LineTokenMetadata
    {
        LanguageId = topLevelLanguageId,
        TokenType = StandardTokenType.Other,
        FontStyle = FontStyle.None,
        Foreground = ColorId.DefaultForeground,
        Background = ColorId.DefaultBackground,
        ContainsBalancedBrackets = true // If there is no grammar, we just take a guess and try to match brackets.
    };
}

public record struct LineTokenChangeRange(int FromLineNumber, int ToLineNumber);

internal static class ContiguousTokensEditing
{
    public static readonly LineToken[] EmptyLineTokens = [];

    [return: NotNullIfNotNull(nameof(lineTokens))]
    public static LineToken[]? DeleteBeginning(LineToken[]? lineTokens, int toChIndex)
    {
        if (lineTokens is null || lineTokens == EmptyLineTokens)
            return lineTokens;
        return Delete(lineTokens, 0, toChIndex);
    }

    [return: NotNullIfNotNull(nameof(lineTokens))]
    public static LineToken[]? DeleteEnding(LineToken[]? lineTokens, int fromChIndex)
    {
        if (lineTokens is null || lineTokens == EmptyLineTokens)
            return lineTokens;
        return Delete(lineTokens, fromChIndex, lineTokens[^1].EndOffset);
    }

    [return: NotNullIfNotNull(nameof(lineTokens))]
    public static LineToken[]? Delete(LineToken[]? lineTokens, int fromChIndex, int toChIndex)
    {
        if (lineTokens is null || lineTokens == EmptyLineTokens || fromChIndex == toChIndex)
            return lineTokens;

        // special case: deleting everything
        if (fromChIndex == 0 && lineTokens[^1].EndOffset == toChIndex)
            return EmptyLineTokens;

        int fromTokenIndex = LineTokens.FindIndexInTokensArray(lineTokens, fromChIndex);
        int fromTokenStartOffset = fromTokenIndex > 0 ? lineTokens[fromTokenIndex - 1].EndOffset : 0;
        int fromTokenEndOffset = lineTokens[fromTokenIndex].EndOffset;

        if (toChIndex < fromTokenEndOffset)
        {
            // the delete range is inside a single token
            int delta_ = toChIndex - fromChIndex;
            for (int i = fromTokenIndex; i < lineTokens.Length; i++)
            {
                lineTokens[i] = new LineToken(lineTokens[i].EndOffset - delta_, lineTokens[i].Metadata);
            }
            return lineTokens;
        }

        int dest;
        int lastEnd;
        if (fromTokenStartOffset != fromChIndex)
        {
            lineTokens[fromTokenIndex] = lineTokens[fromTokenIndex] with { EndOffset = fromChIndex };
            dest = fromTokenIndex + 1;
            lastEnd = fromChIndex;
        }
        else
        {
            dest = fromTokenIndex;
            lastEnd = fromTokenStartOffset;
        }

        int delta = toChIndex - fromChIndex;
        for (int tokenIndex = fromTokenIndex + 1; tokenIndex < lineTokens.Length; tokenIndex++)
        {
            int tokenEndOffset = lineTokens[tokenIndex].EndOffset - delta;
            if (tokenEndOffset > lastEnd)
            {
                lineTokens[dest++] = lineTokens[tokenIndex] with { EndOffset = tokenEndOffset };
                lastEnd = tokenEndOffset;
            }
        }

        if (dest == lineTokens.Length)
            return lineTokens; // nothing to trim

        return lineTokens[0..dest];
    }

    [return: NotNullIfNotNull(nameof(lineTokens))]
    public static LineToken[]? Append(LineToken[]? lineTokens, LineToken[]? otherTokens)
    {
        if (otherTokens == EmptyLineTokens)
            return lineTokens;
        if (lineTokens == EmptyLineTokens)
            return otherTokens;
        if (lineTokens is null)
            return lineTokens;
        if (otherTokens is null)
            return null; // cannot determine combined line length...

        var result = new LineToken[lineTokens.Length + otherTokens.Length];
        lineTokens.CopyTo((Array)result, 0);
        int dest = lineTokens.Length;
        int delta = lineTokens[^1].EndOffset;
        for (int i = 0; i < otherTokens.Length; i++)
        {
            result[dest++] = otherTokens[i] with { EndOffset = otherTokens[i].EndOffset + delta };
        }
        return result;
    }

    [return: NotNullIfNotNull(nameof(lineTokens))]
    public static LineToken[]? Insert(LineToken[]? lineTokens, int chIndex, int textLength)
    {
        if (lineTokens is null || lineTokens == EmptyLineTokens)
            return lineTokens; // nothing to do

        var tokens = lineTokens;
        int tokensCount = tokens.Length;

        int fromTokenIndex = LineTokens.FindIndexInTokensArray(tokens, chIndex);
        if (fromTokenIndex > 0)
        {
            int fromTokenStartOffset = tokens[fromTokenIndex - 1].EndOffset;
            if (fromTokenStartOffset == chIndex)
                fromTokenIndex--;
        }
        for (int tokenIndex = fromTokenIndex; tokenIndex < tokensCount; tokenIndex++)
        {
            tokens[tokenIndex] = tokens[tokenIndex] with { EndOffset = tokens[tokenIndex].EndOffset + textLength };
        }
        return lineTokens;
    }
}