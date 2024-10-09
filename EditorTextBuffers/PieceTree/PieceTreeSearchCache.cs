namespace EditorTextBuffers.PieceTree;

// TODO: Needs check
internal class PieceTreeSearchCache
{
    private readonly int _limit;
    private List<CacheEntry> _cache;

    public PieceTreeSearchCache(int limit)
    {
        _limit = limit;
        _cache = new List<CacheEntry>();
    }

    public CacheEntry? Get(int offset)
    {
        for (int i = _cache.Count - 1; i >= 0; i--)
        {
            CacheEntry nodePos = _cache[i];
            if (nodePos.NodeStartOffset <= offset && nodePos.NodeStartOffset + nodePos.Node.Piece.Length >= offset)
            {
                return nodePos;
            }
        }
        return null;
    }

    public (TreeNode node, int nodeStartOffset, int nodeStartLineNumber)? Get2(int lineNumber)
    {
        for (int i = _cache.Count - 1; i >= 0; i--)
        {
            CacheEntry nodePos = _cache[i];
            if (nodePos.NodeStartLineNumber.HasValue &&
                nodePos.NodeStartLineNumber.Value < lineNumber &&
                nodePos.NodeStartLineNumber.Value + nodePos.Node.Piece.LineFeedCount >= lineNumber)
            {
                return (nodePos.Node, nodePos.NodeStartOffset, nodePos.NodeStartLineNumber.Value);
            }
        }
        return null;
    }

    public void Set(CacheEntry nodePosition)
    {
        if (_cache.Count >= _limit)
        {
            _cache.RemoveAt(0);
        }
        _cache.Add(nodePosition);
    }

    public void Validate(int offset)
    {
        bool hasInvalidVal = false;
        List<CacheEntry?> tmp = new List<CacheEntry?>(_cache);

        for (int i = 0; i < tmp.Count; i++)
        {
            CacheEntry nodePos = tmp[i]!;
            if (nodePos.Node.Parent == null || nodePos.NodeStartOffset >= offset)
            {
                tmp[i] = null;
                hasInvalidVal = true;
            }
        }

        if (hasInvalidVal)
        {
            List<CacheEntry> newArr = new List<CacheEntry>();
            foreach (var entry in tmp)
            {
                if (entry != null)
                {
                    newArr.Add(entry);
                }
            }
            _cache = newArr;
        }
    }
}
