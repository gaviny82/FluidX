using FluidX.Decorations;

namespace FluidX.TextModels;

public class TextModelDecorationTrees
{
    // Decorations that do not show up in the overview ruler.
    private readonly IntervalTree _decorationTree0 = new();

    // Decorations that show up in the overview ruler.
    private readonly IntervalTree _decorationTree1 = new();

    // Decorations that contain injected text.
    private readonly IntervalTree _injectedTextDecorationTree = new();

    public IEnumerable<IModelDecoration> GetAll(
        IDecorationTreesHost host,
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        bool overviewRulerOnly,
        bool onlyMarginDecorations)
    {
        long versionId = host.VersionId;
        var result = Search(filterOwnerId, filterOutValidation, filterFontDecorations, overviewRulerOnly, versionId, onlyMarginDecorations);
        return EnsureNodesHaveRanges(host, result);
    }

    public void EnsureAllNodesHaveRanges(IDecorationTreesHost host)
    {
        GetAll(host, 0, false, false, false, false);
    }

    public IEnumerable<IModelDecoration> GetAllInInterval(
        IDecorationTreesHost host,
        int start,
        int end,
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        bool onlyMarginDecorations)
    {
        long versionId = host.VersionId;
        var result = IntervalSearch(start, end, filterOwnerId, filterOutValidation, filterFontDecorations, versionId, onlyMarginDecorations);
        return EnsureNodesHaveRanges(host, result);
    }

    public IEnumerable<IModelDecoration> GetInjectedTextInInterval(
        IDecorationTreesHost host,
        int start,
        int end,
        int filterOwnerId)
    {
        long versionId = host.VersionId;
        var result = _injectedTextDecorationTree.IntervalSearch(start, end, filterOwnerId, false, false, versionId, false);
        return EnsureNodesHaveRanges(host, result).Where((i) => i.Options.ShowIfCollapsed || i.Range?.IsEmpty == false);
    }

    public IEnumerable<IModelDecoration> GetFontDecorationsInInterval(
        IDecorationTreesHost host,
        int start,
        int end,
        int filterOwnerId)
    {
        long versionId = host.VersionId;
        var decorations = _decorationTree0.IntervalSearch(start, end, filterOwnerId, false, false, versionId, false);
        return EnsureNodesHaveRanges(host, decorations).Where((i) => i.Options.AffectsFont);
    }

    public IEnumerable<IModelDecoration> GetAllInjectedText(IDecorationTreesHost host, int filterOwnerId)
    {
        long versionId = host.VersionId;
        var result = _injectedTextDecorationTree.Search(filterOwnerId, false, false, versionId, false);
        return EnsureNodesHaveRanges(host, result).Where((i) => i.Options.ShowIfCollapsed || i.Range?.IsEmpty == false);
    }

    public IEnumerable<IModelDecoration> GetAllCustomLineHeights(IDecorationTreesHost host, int filterOwnerId)
    {
        long versionId = host.VersionId;
        var result = Search(filterOwnerId, false, false, false, versionId, false);
        return EnsureNodesHaveRanges(host, result).Where((i) => i.Options.LineHeight is not null);
    }

    private IEnumerable<IntervalNode> EnsureNodesHaveRanges(IDecorationTreesHost host, IEnumerable<IntervalNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Range is null)
                node.Range = host.GetRangeAt(node.CachedAbsoluteStart, node.CachedAbsoluteEnd);
        }
        return nodes;
    }

    private IEnumerable<IntervalNode> IntervalSearch(
        int start,
        int end,
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        long cachedVersionId,
        bool onlyMarginDecorations)
    {
        var r0 = _decorationTree0.IntervalSearch(start, end, filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
        var r1 = _decorationTree1.IntervalSearch(start, end, filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
        var r2 = _injectedTextDecorationTree.IntervalSearch(start, end, filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
        return r0.Concat(r1).Concat(r2);
    }

    private IEnumerable<IntervalNode> Search(
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        bool overviewRulerOnly,
        long cachedVersionId,
        bool onlyMarginDecorations)
    {
        if (overviewRulerOnly)
        {
            return _decorationTree1.Search(filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
        }
        else
        {
            var r0 = _decorationTree0.Search(filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
            var r1 = _decorationTree1.Search(filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
            var r2 = _injectedTextDecorationTree.Search(filterOwnerId, filterOutValidation, filterFontDecorations, cachedVersionId, onlyMarginDecorations);
            return r0.Concat(r1).Concat(r2);
        }
    }

    public IEnumerable<IntervalNode> CollectNodesFromOwner(int ownerId)
    {
        var r0 = _decorationTree0.CollectNodesFromOwner(ownerId);
        var r1 = _decorationTree1.CollectNodesFromOwner(ownerId);
        var r2 = _injectedTextDecorationTree.CollectNodesFromOwner(ownerId);
        return r0.Concat(r1).Concat(r2);
    }

    public IEnumerable<IntervalNode> CollectNodesPostOrder()
    {
        var r0 = _decorationTree0.CollectNodesPostOrder();
        var r1 = _decorationTree1.CollectNodesPostOrder();
        var r2 = _injectedTextDecorationTree.CollectNodesPostOrder();
        return r0.Concat(r1).Concat(r2);
    }

    public void Insert(IntervalNode node)
    {
        if (node.IsInjectedText)
            _injectedTextDecorationTree.Insert(node);
        else if (node.IsInOverviewRuler)
            _decorationTree1.Insert(node);
        else
            _decorationTree0.Insert(node);
    }

    public void Delete(IntervalNode node)
    {
        if (node.IsInjectedText)
            _injectedTextDecorationTree.Delete(node);
        else if (node.IsInOverviewRuler)
            _decorationTree1.Delete(node);
        else
            _decorationTree0.Delete(node);
    }

    public TextRange GetNodeRange(IDecorationTreesHost host, IntervalNode node)
    {
        long versionId = host.VersionId;
        if (node.CachedVersionId != versionId)
            ResolveNode(node, versionId);
        if (node.Range is null)
            node.Range = host.GetRangeAt(node.CachedAbsoluteStart, node.CachedAbsoluteEnd);
        return (TextRange)node.Range;
    }

    private void ResolveNode(IntervalNode node, long cachedVersionId)
    {
        if (node.IsInjectedText)
            _injectedTextDecorationTree.ResolveNode(node, cachedVersionId);
        else if (node.IsInOverviewRuler)
            _decorationTree1.ResolveNode(node, cachedVersionId);
        else
            _decorationTree0.ResolveNode(node, cachedVersionId);
    }

    public void AcceptReplace(int offset, int length, int textLength, bool forceMoveMarkers)
    {
        _decorationTree0.AcceptReplace(offset, length, textLength, forceMoveMarkers);
        _decorationTree1.AcceptReplace(offset, length, textLength, forceMoveMarkers);
        _injectedTextDecorationTree.AcceptReplace(offset, length, textLength, forceMoveMarkers);
    }
}

file static class IntervalNodeExtensions
{
    extension(IntervalNode node)
    {
        public bool IsInjectedText => node.Options.After is not null || node.Options.Before is not null;

        public bool IsInOverviewRuler => node.Options.OverviewRuler?.Color is not null;
    }
}

public interface IDecorationTreesHost
{
    long VersionId { get; }
    TextRange GetRangeAt(int start, int end);
}
