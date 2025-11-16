using FluidX.TextModels;

namespace FluidX.Tokenization.TokenStores;

/// <summary>
/// Represents sparse tokens in a text model.
/// </summary>
public class SparseTokensStore
{
    private List<SparseMultilineTokens> _pieces;
    private readonly ILanguageIdCodec _languageIdCodec;
    public bool IsComplete { get; private set; }

    public bool IsEmpty => _pieces.Count == 0;

    public SparseTokensStore(ILanguageIdCodec languageIdCodec)
    {
        _pieces = [];
        IsComplete = false;
        _languageIdCodec = languageIdCodec;
    }

    public void Flush()
    {
        _pieces.Clear();
        IsComplete = false;
    }

    public void Set(List<SparseMultilineTokens>? pieces, bool isComplete, TextModel? textModel)
    {
        // TODO: Use ITextModel
        _pieces = pieces ?? [];
        IsComplete = isComplete;
        if (textModel is not null)
        {
            // TODO: reportIfInvalid
        }
    }

    public FluidX.TextBuffers.Range SetPartial(FluidX.TextBuffers.Range _range, List<SparseMultilineTokens> pieces)
    {
        // console.log(`setPartial ${_range} ${pieces.map(p => p.toString()).join(', ')}`);

        var range = _range;
        if (pieces.Count > 0)
        {
            var _firstRange = pieces[0].GetRange();
            var _lastRange = pieces[^1].GetRange();
            if (_firstRange is null || _lastRange is null)
            {
                return _range;
            }
            range = _range.PlusRange(_firstRange).PlusRange(_lastRange);
        }

        int? insertPosition = null;
        for (int i = 0, len = _pieces.Count; i < len; i++)
        {
            var piece = _pieces[i];
            if (piece.EndLineNumber < range.StartLineNumber)
            {
                // this piece is before the range
                continue;
            }

            if (piece.StartLineNumber > range.EndLineNumber)
            {
                // this piece is after the range, so mark the spot before this piece
                // as a good insertion position and stop looping
                insertPosition = insertPosition ?? i;
                break;
            }

            // this piece might intersect with the range
            piece.RemoveTokens(range);

            if (piece.IsEmpty)
            {
                // remove the piece if it became empty
                _pieces.RemoveAt(i);
                i--;
                len--;
                continue;
            }

            if (piece.EndLineNumber < range.StartLineNumber)
            {
                // after removal, this piece is before the range
                continue;
            }

            if (piece.StartLineNumber > range.EndLineNumber)
            {
                // after removal, this piece is after the range
                insertPosition = insertPosition ?? i;
                continue;
            }

            // after removal, this piece contains the range
            var (a, b) = piece.Split(range);
            if (a.IsEmpty)
            {
                // this piece is actually after the range
                insertPosition = insertPosition ?? i;
                continue;
            }
            if (b.IsEmpty)
            {
                // this piece is actually before the range
                continue;
            }
            // Replace _pieces[i] with [a, b]
            _pieces[i] = a;
            _pieces.Insert(i + 1, b);
            i++;
            len++;

            insertPosition ??= i;
        }

        insertPosition ??= _pieces.Count;

        if (pieces.Count > 0)
        {
            _pieces.InsertRange((int)insertPosition, pieces);
        }

        // console.log(`I HAVE ${this._pieces.length} pieces`);
        // console.log(`${this._pieces.map(p => p.toString()).join('\n')}`);

        return range;
    }

    public LineTokens AddSparseTokens(int lineNumber, LineTokens aTokens)
    {
        if (aTokens.TextLength == 0)
            return aTokens; // Don't do anything for empty lines

        var pieces = _pieces;

        if (pieces.Count == 0)
            return aTokens;

        int pieceIndex = FindFirstPieceWithLine(pieces, lineNumber);
        var bTokens = pieces[pieceIndex].GetLineTokens(lineNumber);

        if (bTokens is null)
            return aTokens;

        int aLen = aTokens.Count;
        int bLen = bTokens.Length;

        int aIndex = 0;
        List<LineToken> result = [];
        int lastEndOffset = 0;

        void emitToken(int endOffset, LineTokenMetadata metadata)
        {
            if (endOffset == lastEndOffset)
                return;
            lastEndOffset = endOffset;
            result.Add(new(endOffset, metadata));
        }

        for (int bIndex = 0; bIndex < bLen; bIndex++)
        {
            // bTokens is not validated yet, but aTokens is. We want to make sure that the LineTokens we return
            // are valid, so we clamp the ranges to ensure that.
            int bStartCharacter = Math.Min(bTokens[bIndex].StartIndex, aTokens.TextLength);
            int bEndCharacter = Math.Min(bTokens[bIndex].EndIndex, aTokens.TextLength);
            uint bMetadata = bTokens[bIndex].Metadata.RawValue;

            uint bMask =
                ((bMetadata & MetadataConsts.SEMANTIC_USE_ITALIC) != 0 ? MetadataConsts.ITALIC_MASK : 0)
                | ((bMetadata & MetadataConsts.SEMANTIC_USE_BOLD) != 0 ? MetadataConsts.BOLD_MASK : 0)
                | ((bMetadata & MetadataConsts.SEMANTIC_USE_UNDERLINE) != 0 ? MetadataConsts.UNDERLINE_MASK : 0)
                | ((bMetadata & MetadataConsts.SEMANTIC_USE_STRIKETHROUGH) != 0 ? MetadataConsts.STRIKETHROUGH_MASK : 0)
                | ((bMetadata & MetadataConsts.SEMANTIC_USE_FOREGROUND) != 0 ? MetadataConsts.FOREGROUND_MASK : 0)
                | ((bMetadata & MetadataConsts.SEMANTIC_USE_BACKGROUND) != 0 ? MetadataConsts.BACKGROUND_MASK : 0);
            uint aMask = ~bMask;

            // push any token from `a` that is before `b`
            while (aIndex < aLen && aTokens.GetEndOffset(aIndex) <= bStartCharacter)
            {
                emitToken(aTokens.GetEndOffset(aIndex), aTokens.GetMetadata(aIndex));
                aIndex++;
            }

            // push the token from `a` if it intersects the token from `b`
            if (aIndex < aLen && aTokens.GetStartOffset(aIndex) < bStartCharacter)
            {
                emitToken(bStartCharacter, aTokens.GetMetadata(aIndex));
            }

            // skip any tokens from `a` that are contained inside `b`
            while (aIndex < aLen && aTokens.GetEndOffset(aIndex) < bEndCharacter)
            {
                emitToken(aTokens.GetEndOffset(aIndex), new((aTokens.GetMetadata(aIndex).RawValue & aMask) | (bMetadata & bMask)));
                aIndex++;
            }

            if (aIndex < aLen)
            {
                emitToken(bEndCharacter, new((aTokens.GetMetadata(aIndex).RawValue & aMask) | (bMetadata & bMask)));
                if (aTokens.GetEndOffset(aIndex) == bEndCharacter)
                {
                    // `a` ends exactly at the same spot as `b`!
                    aIndex++;
                }
            }
            else
            {
                int aMergeIndex = Math.Min(Math.Max(0, aIndex - 1), aLen - 1);

                // push the token from `b`
                emitToken(bEndCharacter, new((aTokens.GetMetadata(aMergeIndex).RawValue & aMask) | (bMetadata & bMask)));
            }
        }

        // push the remaining tokens from `a`
        while (aIndex < aLen)
        {
            emitToken(aTokens.GetEndOffset(aIndex), aTokens.GetMetadata(aIndex));
            aIndex++;
        }

        return new LineTokens(result.ToArray(), aTokens.LineContent, _languageIdCodec);
    }

    private static int FindFirstPieceWithLine(List<SparseMultilineTokens> pieces, int lineNumber)
    {
        int low = 0;
        int high = pieces.Count - 1;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (pieces[mid].EndLineNumber < lineNumber)
            {
                low = mid + 1;
            }
            else if (pieces[mid].StartLineNumber > lineNumber)
            {
                high = mid - 1;
            }
            else
            {
                while (mid > low
                    && pieces[mid - 1].StartLineNumber <= lineNumber
                    && lineNumber <= pieces[mid - 1].EndLineNumber)
                {
                    mid--;
                }
                return mid;
            }
        }
        return low;
    }

    public void AcceptEdit(
        FluidX.TextBuffers.Range range,
        int eolCount,
        int firstLineLength,
        int lastLineLength,
        char firstCharCode)
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            piece.AcceptEdit(range, eolCount, firstLineLength, lastLineLength, firstCharCode);

            if (piece.IsEmpty)
            {
                // Remove empty pieces
                _pieces.RemoveAt(i);
                i--;
            }
        }
    }
}
