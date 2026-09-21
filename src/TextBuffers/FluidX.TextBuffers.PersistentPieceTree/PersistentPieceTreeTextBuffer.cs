using System.Text;

namespace FluidX.TextBuffers.PersistentPieceTree;

/// <summary>A UTF-16 piece table with immutable, structurally shared red-black nodes.</summary>
/// <remarks>
/// Snapshot capture is O(1) and allocates nothing. Snapshots support concurrent reads,
/// including during source edits. Writers must be externally synchronized.
/// Each edit copies only affected tree paths; retained versions share text and other nodes.
/// </remarks>
public sealed class PersistentPieceTreeTextBuffer : ITextBuffer
{
    private volatile PersistentPieceTreeSnapshot _version;
    private const int EditChunkSize = 1024;
    private TextStorage? _editStorage;

    public PersistentPieceTreeTextBuffer(string text = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        _version = new(Build(text));
    }

    public static PersistentPieceTreeTextBuffer Create(string text) => new(text);

    private static PieceNode? Build(string text) => text.Length == 0 ? null :
        new(false, null, new Piece(new TextStorage(text), 0, text.Length), null);

    public int Length => _version.Length;
    public int LineCount => _version.LineCount;
    public ITextSnapshot CreateSnapshot() => _version;
    public bool Equals(IReadOnlyTextBuffer? other) => ReferenceEquals(this, other) ||
        _version.Equals(other is PersistentPieceTreeTextBuffer buffer ? buffer._version : other);
    public int GetOffsetAt(TextPosition position) => _version.GetOffsetAt(position);
    public TextPosition GetPositionAt(int offset) => _version.GetPositionAt(offset);
    public TextRange GetRangeAt(int offset, int length) => _version.GetRangeAt(offset, length);
    public string GetTextInRange(TextRange range) => _version.GetTextInRange(range);
    public int GetTextLengthInRange(TextRange range) => _version.GetTextLengthInRange(range);
    public IReadOnlyList<string> GetLinesContent() => _version.GetLinesContent();
    public string GetLineContent(int lineIndex) => _version.GetLineContent(lineIndex);
    public string GetLineEOL(int lineIndex) => _version.GetLineEOL(lineIndex);
    public int GetLineLength(int lineIndex) => _version.GetLineLength(lineIndex);
    public int GetLineFirstNonWhitespaceColumnIndex(int lineIndex) => _version.GetLineFirstNonWhitespaceColumnIndex(lineIndex);
    public int GetLineLastNonWhitespaceColumnIndex(int lineIndex) => _version.GetLineLastNonWhitespaceColumnIndex(lineIndex);
    public char GetChar(TextPosition position) => _version.GetChar(position);
    public char GetChar(int offset) => _version.GetChar(offset);
    public IReadOnlyList<FindMatch> FindMatchesLineByLine(TextRange searchRange, SearchData searchData, bool captureMatches, int limitResultCount)
        => _version.FindMatchesLineByLine(searchRange, searchData, captureMatches, limitResultCount);

    public void ApplyEdits(TextReplacement[] replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var original = _version;
        var root = original.Root;
        // All coordinates are resolved against the original version: a later edit can
        // change CRLF pairing even at an earlier, touching edit boundary.
        for (int i = replacements.Length - 1; i >= 0; i--)
        {
            var edit = replacements[i];
            if (edit.IsEmpty) continue;
            int start = original.GetOffsetAt(edit.Range.StartPosition);
            int end = original.GetOffsetAt(edit.Range.EndPosition);
            root = Splice(root, start, end - start, edit.Text);
        }
        if (!ReferenceEquals(root, original.Root)) _version = new(root);
    }

    private PieceNode InsertText(PieceNode? root, int offset, string text)
    {
        Piece inserted;
        if (text.Length > EditChunkSize)
            inserted = new(new TextStorage(text), 0, text.Length);
        else
        {
            if (_editStorage is null || _editStorage.Available < text.Length)
                _editStorage = new(EditChunkSize);
            inserted = _editStorage.Append(text);
        }
        // Typing at the append frontier extends the preceding piece while old
        // versions keep their original slice lengths and newline-index bounds.
        if (offset > 0)
        {
            var (previous, start) = PieceNode.Find(root!, offset - 1);
            if (start + previous.Length == offset && ReferenceEquals(previous.Storage, inserted.Storage)
                && previous.Start + previous.Length == inserted.Start)
            {
                return PieceNode.Replace(root!, start, new(previous.Storage, previous.Start, previous.Length + inserted.Length));
            }
        }
        return PieceNode.Insert(root, inserted, offset);
    }

    // Split only the containing piece; its two slices share the original storage/index.
    private static PieceNode? SplitAt(PieceNode? root, int offset)
    {
        if (root is null || offset == 0 || offset == root.Length) return root;
        var (piece, start) = PieceNode.Find(root, offset);
        if (start == offset) return root;
        int leftLength = offset - start;
        root = PieceNode.Remove(root, start);
        root = PieceNode.Insert(root, piece.Slice(leftLength, piece.Length - leftLength), start);
        return PieceNode.Insert(root, piece.Slice(0, leftLength), start);
    }

    // Replace one range after splitting its two boundaries. The resulting left and right trees are joined
    // by inserting at the known boundary, so callers do not repeat the start-boundar search.
    private PieceNode? Splice(PieceNode? root, int offset, int length, string replacement)
    {
        if (length == 0)
        {
            if (replacement.Length == 0)
                return root;

            // An insertion has no deleted range to establish a piece boundary. Split once so insertion
            // at the middle of a piece is after the prefix rather than before the containing piece.
            return InsertText(SplitAt(root, offset), offset, replacement);
        }
        if (offset == 0 && length == root!.Length)
            return replacement.Length == 0 ? null : InsertText(null, 0, replacement);

        int remaining = length;
        root = SplitAt(root, offset + length);
        root = SplitAt(root, offset);
        while (remaining > 0)
        {
            var (piece, _) = PieceNode.Find(root!, offset);
            remaining -= piece.Length;
            root = PieceNode.Remove(root!, offset);
        }
        return replacement.Length == 0 ? root : InsertText(root, offset, replacement);
    }

    public void NormalizeEOL(string eol)
    {
        if (eol is not ("\n" or "\r\n")) throw new ArgumentException("Invalid EOL value", nameof(eol));
        var version = _version;
        // Normalization rewrites the document once; old versions retain their storage.
        string text = version.TextAt(0, version.Length);
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                result.Append(eol);
            }
            else result.Append(text[i]);
        }
        _version = new(Build(result.ToString()));
        _editStorage = null;
    }
}
