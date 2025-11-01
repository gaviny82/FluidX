using System.Xml.Linq;

namespace FluidX.Decorations;

public class IntervalTree
{
    private IntervalNode _root = IntervalNode.Sentinel;
    private bool _requestNormalizeDelta = false;

    #region Delta Normalization

    private void NormalizeDeltaIfNecessary()
    {
        if (!_requestNormalizeDelta)
            return;
        _requestNormalizeDelta = false;
        NormalizeDelta();
    }

    private void NormalizeDelta()
    {
        var node = _root;
        int delta = 0;
        while (node != IntervalNode.Sentinel)
        {
            if (node.Left != IntervalNode.Sentinel && !node.Left.IsVisited)
            {
                // go left
                node = node.Left;
                continue;
            }
            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go right
                delta += node.Delta;
                node = node.Right;
                continue;
            }

            // handle current node
            node.Start = delta + node.Start;
            node.End = delta + node.End;
            node.Delta = 0;
            node.MaxEnd = ComputeMaxEnd(node);

            node.IsVisited = true;

            // going up from this node
            node.Left.IsVisited = false;
            node.Right.IsVisited = false;
            if (node == node.Parent.Right)
                delta -= node.Parent.Delta;
            node = node.Parent;
        }
        _root.IsVisited = false;
    }

    #endregion

    #region Editing

    public void AcceptReplace(int offset, int length, int textLength, bool forceMoveMarkers)
    {
        // Our strategy is to remove all directly impacted nodes, and then add them back to the tree.

        // (1) collect all nodes that are intersecting this edit as nodes of interest
        var nodesOfInterest = SearchForEditing(offset, offset + length);

        // (2) remove all nodes that are intersecting this edit
        for (int i = 0, len = nodesOfInterest.Count; i < len; i++)
        {
            var node = nodesOfInterest[i];
            RbTreeDelete(node);
        }
        NormalizeDeltaIfNecessary();

        // (3) edit all tree nodes except the nodes of interest
        NoOverlapReplace(offset, offset + length, textLength);
        NormalizeDeltaIfNecessary();

        // (4) edit the nodes of interest and insert them back in the tree
        for (int i = 0, len = nodesOfInterest.Count; i < len; i++)
        {
            var node = nodesOfInterest[i];
            node.Start = node.CachedAbsoluteStart;
            node.End = node.CachedAbsoluteEnd;
            NodeAcceptEdit(node, offset, (offset + length), textLength, forceMoveMarkers);
            node.MaxEnd = node.End;
            RbTreeInsert(node);
        }
        NormalizeDeltaIfNecessary();
    }

    public void ResolveNode(IntervalNode node, int cachedVersionId)
    {
        IntervalNode initialNode = node;
        int delta = 0;
        while (node != _root)
        {
            if (node == node.Parent.Right)
            {
                delta += node.Parent.Delta;
            }
            node = node.Parent;
        }

        int nodeStart = initialNode.Start + delta;
        int nodeEnd = initialNode.End + delta;
        initialNode.SetCachedOffsets(nodeStart, nodeEnd, cachedVersionId);
    }

    private static bool adjustMarkerBeforeColumn(
        int markerOffset,
        bool markerStickToPreviousCharacter,
        int checkOffset,
        MarkerMoveSemantics moveSemantics)
    {
        if (markerOffset < checkOffset)
            return true;
        if (markerOffset > checkOffset)
            return false;
        if (moveSemantics == MarkerMoveSemantics.ForceMove)
            return false;
        if (moveSemantics == MarkerMoveSemantics.ForceStay)
            return true;
        return markerStickToPreviousCharacter;
    }

    /**
     * This is a lot more complicated than strictly necessary to maintain the same behaviour
     * as when decorations were implemented using two markers.
     */
    private void NodeAcceptEdit(
        IntervalNode node,
        int start,
        int end,
        int textLength,
        bool forceMoveMarkers)
    { 
        var nodeStickiness = node.Stickiness;
        bool startStickToPreviousCharacter =
            nodeStickiness == TrackedRangeStickiness.AlwaysGrowsWhenTypingAtEdges
            || nodeStickiness == TrackedRangeStickiness.GrowsOnlyWhenTypingBefore;
        bool endStickToPreviousCharacter =
            nodeStickiness == TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
            || nodeStickiness == TrackedRangeStickiness.GrowsOnlyWhenTypingBefore;

        int deletingCnt = (end - start);
        int insertingCnt = textLength;
        int commonLength = Math.Min(deletingCnt, insertingCnt);

        int nodeStart = node.Start;
        bool startDone = false;

        int nodeEnd = node.End;
        bool endDone = false;

        if (start <= nodeStart && nodeEnd <= end && node.CollapseOnReplaceEdit) {
            // This edit encompasses the entire decoration range
            // and the decoration has asked to become collapsed
            node.Start = start;
            startDone = true;
            node.End = start;
            endDone = true;
        }

        {
            var moveSemantics = forceMoveMarkers
                ? MarkerMoveSemantics.ForceMove
                : (deletingCnt > 0 ? MarkerMoveSemantics.ForceStay : MarkerMoveSemantics.MarkerDefined);
            if (!startDone && adjustMarkerBeforeColumn(nodeStart, startStickToPreviousCharacter, start, moveSemantics)) {
                startDone = true;
            }
            if (!endDone && adjustMarkerBeforeColumn(nodeEnd, endStickToPreviousCharacter, start, moveSemantics))
            {
                endDone = true;
            }
        }

        if (commonLength > 0 && !forceMoveMarkers)
        {
            var moveSemantics = deletingCnt > insertingCnt
                ? MarkerMoveSemantics.ForceStay
                : MarkerMoveSemantics.MarkerDefined;
            if (!startDone && adjustMarkerBeforeColumn(nodeStart, startStickToPreviousCharacter, start + commonLength, moveSemantics))
            {
                startDone = true;
            }
            if (!endDone && adjustMarkerBeforeColumn(nodeEnd, endStickToPreviousCharacter, start + commonLength, moveSemantics))
            {
                endDone = true;
            }
        }

        {
            var moveSemantics = forceMoveMarkers
                ? MarkerMoveSemantics.ForceMove
                : MarkerMoveSemantics.MarkerDefined;
            if (!startDone && adjustMarkerBeforeColumn(nodeStart, startStickToPreviousCharacter, end, moveSemantics))
            {
                node.Start = start + insertingCnt;
                startDone = true;
            }
            if (!endDone && adjustMarkerBeforeColumn(nodeEnd, endStickToPreviousCharacter, end, moveSemantics))
            {
                node.End = start + insertingCnt;
                endDone = true;
            }
        }

        // Finish
        int deltaColumn = (insertingCnt - deletingCnt);
        if (!startDone)
        {
            node.Start = Math.Max(0, nodeStart + deltaColumn);
        }
        if (!endDone)
        {
            node.End = Math.Max(0, nodeEnd + deltaColumn);
        }

        if (node.Start > node.End)
        {
            node.End = node.Start;
        }
    }

    private List<IntervalNode> SearchForEditing(int start, int end)
    {
        // https://en.wikipedia.org/wiki/Interval_tree#Augmented_tree
        // Now, it is known that two intervals A and B overlap only when both
        // A.low <= B.high and A.high >= B.low. When searching the trees for
        // nodes overlapping with a given interval, you can immediately skip:
        //  a) all nodes to the right of nodes whose low value is past the end of the given interval.
        //  b) all nodes that have their maximum 'high' value below the start of the given interval.
        IntervalNode node = _root;
        int delta = 0;
        int nodeMaxEnd = 0;
        int nodeStart = 0;
        int nodeEnd = 0;
        List<IntervalNode> result = [];
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                if (node == node.Parent.Right)
                {
                    delta -= node.Parent.Delta;
                }
                node = node.Parent;
                continue;
            }

            if (!node.Left.IsVisited)
            {
                // first time seeing this node
                nodeMaxEnd = delta + node.MaxEnd;
                if (nodeMaxEnd < start)
                {
                    // cover case b) from above
                    // there is no need to search this node or its children
                    node.IsVisited = true;
                    continue;
                }

                if (node.Left != IntervalNode.Sentinel)
                {
                    // go left
                    node = node.Left;
                    continue;
                }
            }

            // handle current node
            nodeStart = delta + node.Start;
            if (nodeStart > end)
            {
                // cover case a) from above
                // there is no need to search this node or its right subtree
                node.IsVisited = true;
                continue;
            }

            nodeEnd = delta + node.End;
            if (nodeEnd >= start)
            {
                node.SetCachedOffsets(nodeStart, nodeEnd, 0);
                result.Add(node);
            }
            node.IsVisited = true;

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go right
                delta += node.Delta;
                node = node.Right;
                continue;
            }
        }

        _root.IsVisited = false;

        return result;
    }

    private void NoOverlapReplace(int start, int end, int textLength)
    {
        // https://en.wikipedia.org/wiki/Interval_tree#Augmented_tree
        // Now, it is known that two intervals A and B overlap only when both
        // A.low <= B.high and A.high >= B.low. When searching the trees for
        // nodes overlapping with a given interval, you can immediately skip:
        //  a) all nodes to the right of nodes whose low value is past the end of the given interval.
        //  b) all nodes that have their maximum 'high' value below the start of the given interval.
        var node = _root;
        int delta = 0;
        int nodeMaxEnd = 0;
        int nodeStart = 0;
        int editDelta = (textLength - (end - start));
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                if (node == node.Parent.Right)
                {
                    delta -= node.Parent.Delta;
                }
                node.MaxEnd = ComputeMaxEnd(node);
                node = node.Parent;
                continue;
            }

            if (!node.Left.IsVisited)
            {
                // first time seeing this node
                nodeMaxEnd = delta + node.MaxEnd;
                if (nodeMaxEnd < start)
                {
                    // cover case b) from above
                    // there is no need to search this node or its children
                    node.IsVisited = true;
                    continue;
                }

                if (node.Left != IntervalNode.Sentinel)
                {
                    // go left
                    node = node.Left;
                    continue;
                }
            }

            // handle current node
            nodeStart = delta + node.Start;
            if (nodeStart > end)
            {
                node.Start += editDelta;
                node.End += editDelta;
                node.Delta += editDelta;
                if (node.Delta < Constants.MIN_SAFE_DELTA || node.Delta > Constants.MAX_SAFE_DELTA)
                {
                    _requestNormalizeDelta = true;
                }
                // cover case a) from above
                // there is no need to search this node or its right subtree
                node.IsVisited = true;
                continue;
            }

            node.IsVisited = true;

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go right
                delta += node.Delta;
                node = node.Right;
                continue;
            }
        }

        _root.IsVisited = false;
    }

    #endregion

    #region Searching

    public List<IntervalNode> GetAllInOrder()
        => Search(0, false, false, 0, false);

    /// <summary>
    /// Will not set <see cref="IntervalNode.CachedAbsoluteStart"/> nor <see cref="IntervalNode.CachedAbsoluteEnd"/> on the returned nodes.
    /// </summary>
    /// <returns></returns>
    public List<IntervalNode> CollectNodesFromOwner(int ownerId)
    {
        var node = _root;
        List<IntervalNode> result = [];
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                node = node.Parent;
                continue;
            }

            if (node.Left != IntervalNode.Sentinel && !node.Left.IsVisited)
            {
                // go.Left
                node = node.Left;
                continue;
            }

            // handle current node
            if (node.OwnerId == ownerId)
            {
                result.Add(node);
            }

            node.IsVisited = true;

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go.Right
                node = node.Right;
                continue;
            }
        }

        _root.IsVisited = false;

        return result;
    }

    /// <summary>
    /// Will not set <see cref="IntervalNode.CachedAbsoluteStart"/> nor <see cref="IntervalNode.CachedAbsoluteEnd"/> on the returned nodes.
    /// </summary>
    /// <returns></returns>
    public List<IntervalNode> CollectNodesPostOrder()
    {
        var node = _root;
        List<IntervalNode> result = [];
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                node = node.Parent;
                continue;
            }

            if (node.Left != IntervalNode.Sentinel && !node.Left.IsVisited)
            {
                // go.Left
                node = node.Left;
                continue;
            }

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go.Right
                node = node.Right;
                continue;
            }

            // handle current node
            result.Add(node);
            node.IsVisited = true;
        }

        _root.IsVisited = false;

        return result;
    }

    public List<IntervalNode> Search(
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        int cachedVersionId,
        bool onlyMarginDecorations)
    {
        var node = _root;
        int delta = 0;
        int nodeStart = 0;
        int nodeEnd = 0;
        List<IntervalNode> result = [];
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                if (node == node.Parent.Right)
                {
                    delta -= node.Parent.Delta;
                }
                node = node.Parent;
                continue;
            }

            if (node.Left != IntervalNode.Sentinel && !node.Left.IsVisited)
            {
                // go.Left
                node = node.Left;
                continue;
            }

            // handle current node
            nodeStart = delta + node.Start;
            nodeEnd = delta + node.End;

            node.SetCachedOffsets(nodeStart, nodeEnd, cachedVersionId);

            bool include = true;
            if (filterOwnerId != 0 && node.OwnerId != 0 && node.OwnerId != filterOwnerId)
                include = false;
            if (filterOutValidation && node.IsForValidation)
                include = false;
            if (filterFontDecorations && node.AffectsFont)
                include = false;
            if (onlyMarginDecorations && !node.IsInGlyphMargin)
                include = false;

            if (include)
                result.Add(node);

            node.IsVisited = true;

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go.Right
                delta += node.Delta;
                node = node.Right;
                continue;
            }
        }

        _root.IsVisited = false;

        return result;
    }

    public List<IntervalNode> IntervalSearch(
        int intervalStart,
        int intervalEnd,
        int filterOwnerId,
        bool filterOutValidation,
        bool filterFontDecorations,
        int cachedVersionId,
        bool onlyMarginDecorations)
    {
        // https://en.wikipedia.org/wiki/Interval_tree#Augmented_tree
        // Now, it is known that two intervals A and B overlap only when both
        // A.low <= B.high and A.high >= B.low. When searching the trees for
        // nodes overlapping with a given interval, you can immediately skip:
        //  a) all nodes to the.Right of nodes whose low value is past the end of the given interval.
        //  b) all nodes that have their maximum 'high' value below the start of the given interval.

        var node = _root;
        int delta = 0;
        int nodeMaxEnd = 0;
        int nodeStart = 0;
        int nodeEnd = 0;
        List<IntervalNode> result = [];
        while (node != IntervalNode.Sentinel)
        {
            if (node.IsVisited)
            {
                // going up from this node
                node.Left.IsVisited = false;
                node.Right.IsVisited = false;
                if (node == node.Parent.Right)
                {
                    delta -= node.Parent.Delta;
                }
                node = node.Parent;
                continue;
            }

            if (!node.Left.IsVisited)
            {
                // first time seeing this node
                nodeMaxEnd = delta + node.MaxEnd;
                if (nodeMaxEnd < intervalStart)
                {
                    // cover case b) from above
                    // there is no need to search this node or its children
                    node.IsVisited = true;
                    continue;
                }

                if (node.Left != IntervalNode.Sentinel)
                {
                    // go.Left
                    node = node.Left;
                    continue;
                }
            }

            // handle current node
            nodeStart = delta + node.Start;
            if (nodeStart > intervalEnd)
            {
                // cover case a) from above
                // there is no need to search this node or its.Right subtree
                node.IsVisited = true;
                continue;
            }

            nodeEnd = delta + node.End;

            if (nodeEnd >= intervalStart)
            {
                // There is overlap
                node.SetCachedOffsets(nodeStart, nodeEnd, cachedVersionId);

                bool include = true;
                if (filterOwnerId != 0 && node.OwnerId != 0 && node.OwnerId != filterOwnerId)
                    include = false;
                if (filterOutValidation && node.IsForValidation)
                    include = false;
                if (filterFontDecorations && node.AffectsFont)
                    include = false;
                if (onlyMarginDecorations && !node.IsInGlyphMargin)
                    include = false;

                if (include)
                    result.Add(node);
            }

            node.IsVisited = true;

            if (node.Right != IntervalNode.Sentinel && !node.Right.IsVisited)
            {
                // go.Right
                delta += node.Delta;
                node = node.Right;
                continue;
            }
        }

        _root.IsVisited = false;

        return result;
    }

    #endregion

    #region Red-Black Tree Operations

    public void Insert(IntervalNode node)
    {
        RbTreeInsert(node);
        NormalizeDeltaIfNecessary();
    }

    public void Delete(IntervalNode node)
    {
        RbTreeDelete(node);
        NormalizeDeltaIfNecessary();
    }

    private IntervalNode RbTreeInsert(IntervalNode newNode)
    {
        if (_root == IntervalNode.Sentinel)
        {
            newNode.Parent = IntervalNode.Sentinel;
            newNode.Left = IntervalNode.Sentinel;
            newNode.Right = IntervalNode.Sentinel;
            newNode.NodeColor = NodeColor.Black;
            _root = newNode;
            return _root;
        }

        TreeInsert(this, newNode);

        RecomputeMaxEndWalkToRoot(newNode.Parent);

        // repair tree
        IntervalNode x = newNode;
        while (x != _root && x.Parent.NodeColor == NodeColor.Red)
        {
            if (x.Parent == x.Parent.Parent.Left)
            {
                var y = x.Parent.Parent.Right;

                if (y.NodeColor == NodeColor.Red)
                {
                    x.Parent.NodeColor = NodeColor.Black;
                    y.NodeColor = NodeColor.Black;
                    x.Parent.Parent.NodeColor = NodeColor.Red;
                    x = x.Parent.Parent;
                }
                else
                {
                    if (x == x.Parent.Right)
                    {
                        x = x.Parent;
                        LeftRotate(x);
                    }
                    x.Parent.NodeColor = NodeColor.Black;
                    x.Parent.Parent.NodeColor = NodeColor.Red;
                    RightRotate(x.Parent.Parent);
                }
            }
            else
            {
                var y = x.Parent.Parent.Left;

                if (y.NodeColor == NodeColor.Red)
                {
                    x.Parent.NodeColor = NodeColor.Black;
                    y.NodeColor = NodeColor.Black;
                    x.Parent.Parent.NodeColor = NodeColor.Red;
                    x = x.Parent.Parent;
                }
                else
                {
                    if (x == x.Parent.Left)
                    {
                        x = x.Parent;
                        RightRotate(x);
                    }
                    x.Parent.NodeColor = NodeColor.Black;
                    x.Parent.Parent.NodeColor = NodeColor.Red;
                    LeftRotate(x.Parent.Parent);
                }
            }
        }

        _root.NodeColor = NodeColor.Black;

        return newNode;
    }

    private static void TreeInsert(IntervalTree T, IntervalNode z)
    {
        int delta = 0;
        IntervalNode x = T._root;
        int zAbsoluteStart = z.Start;
        int zAbsoluteEnd = z.End;
        while (true)
        {
            int cmp = IntervalCompare(zAbsoluteStart, zAbsoluteEnd, x.Start + delta, x.End + delta);
            if (cmp < 0)
            {
                // this node should be inserted to the left
                // => it is not affected by the node's delta
                if (x.Left == IntervalNode.Sentinel)
                {
                    z.Start -= delta;
                    z.End -= delta;
                    z.MaxEnd -= delta;
                    x.Left = z;
                    break;
                }
                else
                {
                    x = x.Left;
                }
            }
            else
            {
                // this node should be inserted to the right
                // => it is not affected by the node's delta
                if (x.Right == IntervalNode.Sentinel)
                {
                    z.Start -= delta + x.Delta;
                    z.End -= delta + x.Delta;
                    z.MaxEnd -= delta + x.Delta;
                    x.Right = z;
                    break;
                }
                else
                {
                    delta += x.Delta;
                    x = x.Right;
                }
            }
        }

        z.Parent = x;
        z.Left = IntervalNode.Sentinel;
        z.Right = IntervalNode.Sentinel;
        z.NodeColor = NodeColor.Red;
    }

    private void RbTreeDelete(IntervalNode z)
    {
        IntervalNode x, y;

        // RB-DELETE except we don't swap z and y in case c)
        // i.e. we always delete what's pointed at by z.

        if (z.Left == IntervalNode.Sentinel)
        {
            x = z.Right;
            y = z;

            // x's delta is no longer influenced by z's delta
            x.Delta += z.Delta;
            if (x.Delta < Constants.MIN_SAFE_DELTA || x.Delta > Constants.MAX_SAFE_DELTA)
            {
                _requestNormalizeDelta = true;
            }
            x.Start += z.Delta;
            x.End += z.Delta;
        }
        else if (z.Right == IntervalNode.Sentinel)
        {
            x = z.Left;
            y = z;

        }
        else
        {
            y = Leftest(z.Right);
            x = y.Right;

            // y's delta is no longer influenced by z's delta,
            // but we don't want to walk the entire right-hand-side subtree of x.
            // we therefore maintain z's delta in y, and adjust only x
            x.Start += y.Delta;
            x.End += y.Delta;
            x.Delta += y.Delta;
            if (x.Delta < Constants.MIN_SAFE_DELTA || x.Delta > Constants.MAX_SAFE_DELTA)
            {
                _requestNormalizeDelta = true;
            }

            y.Start += z.Delta;
            y.End += z.Delta;
            y.Delta = z.Delta;
            if (y.Delta < Constants.MIN_SAFE_DELTA || y.Delta > Constants.MAX_SAFE_DELTA)
            {
                _requestNormalizeDelta = true;
            }
        }

        if (y == _root)
        {
            _root = x;
            x.NodeColor = NodeColor.Black;

            z.Detach();
            IntervalNode.ResetSentinel();
            x.MaxEnd = ComputeMaxEnd(x);
            _root.Parent = IntervalNode.Sentinel;
            return;
        }

        bool yWasRed = y.NodeColor == NodeColor.Red;

        if (y == y.Parent.Left)
            y.Parent.Left = x;
        else
            y.Parent.Right = x;

        if (y == z)
        {
            x.Parent = y.Parent;
        }
        else
        {

            if (y.Parent == z)
            {
                x.Parent = y;
            }
            else
            {
                x.Parent = y.Parent;
            }

            y.Left = z.Left;
            y.Right = z.Right;
            y.Parent = z.Parent;
            y.NodeColor = z.NodeColor;

            if (z == _root)
            {
                _root = y;
            }
            else
            {
                if (z == z.Parent.Left)
                {
                    z.Parent.Left = y;
                }
                else
                {
                    z.Parent.Right = y;
                }
            }

            if (y.Left != IntervalNode.Sentinel)
            {
                y.Left.Parent = y;
            }
            if (y.Right != IntervalNode.Sentinel)
            {
                y.Right.Parent = y;
            }
        }

        z.Detach();

        if (yWasRed)
        {
            RecomputeMaxEndWalkToRoot(x.Parent);
            if (y != z)
            {
                RecomputeMaxEndWalkToRoot(y);
                RecomputeMaxEndWalkToRoot(y.Parent);
            }
            IntervalNode.ResetSentinel();
            return;
        }

        RecomputeMaxEndWalkToRoot(x);
        RecomputeMaxEndWalkToRoot(x.Parent);
        if (y != z)
        {
            RecomputeMaxEndWalkToRoot(y);
            RecomputeMaxEndWalkToRoot(y.Parent);
        }

        // RB-DELETE-FIXUP
        IntervalNode w;
        while (x != _root && x.NodeColor == NodeColor.Black)
        {
            if (x == x.Parent.Left)
            {
                w = x.Parent.Right;

                if (w.NodeColor == NodeColor.Red)
                {
                    w.NodeColor= NodeColor.Black;
                    x.Parent.NodeColor = NodeColor.Red;
                    LeftRotate(x.Parent);
                    w = x.Parent.Right;
                }

                if (w.Left.NodeColor == NodeColor.Black && w.Right.NodeColor == NodeColor.Black)
                {
                    w.NodeColor = NodeColor.Red;
                    x = x.Parent;
                }
                else
                {
                    if (w.Right.NodeColor == NodeColor.Black)
                    {
                        w.Left.NodeColor = NodeColor.Black;
                        w.NodeColor = NodeColor.Red;
                        RightRotate(w);
                        w = x.Parent.Right;
                    }

                    w.NodeColor = x.Parent.NodeColor;
                    x.Parent.NodeColor = NodeColor.Black;
                    w.Right.NodeColor = NodeColor.Black;
                    LeftRotate(x.Parent);
                    x = _root;
                }

            }
            else
            {
                w = x.Parent.Left;

                if (w.NodeColor == NodeColor.Red)
                {
                    w.NodeColor = NodeColor.Black;
                    x.Parent.NodeColor = NodeColor.Red;
                    RightRotate(x.Parent);
                    w = x.Parent.Left;
                }

                if (w.Left.NodeColor == NodeColor.Black && w.Right.NodeColor == NodeColor.Black)
                {
                    w.NodeColor = NodeColor.Red;
                    x = x.Parent;

                }
                else
                {
                    if (w.Left.NodeColor == NodeColor.Black)
                    {
                        w.Right.NodeColor = NodeColor.Black;
                        w.NodeColor = NodeColor.Red;
                        LeftRotate(w);
                        w = x.Parent.Left;
                    }

                    w.NodeColor = x.Parent.NodeColor;
                    x.Parent.NodeColor = NodeColor.Black;
                    w.Left.NodeColor = NodeColor.Black;
                    RightRotate(x.Parent);
                    x = _root;
                }
            }
        }

        x.NodeColor = NodeColor.Black;
        IntervalNode.ResetSentinel();
    }

    private void LeftRotate(IntervalNode x)
    {
        var y = x.Right;

        y.Delta += x.Delta; // y's delta is no longer influenced by x's delta
        if (y.Delta < Constants.MIN_SAFE_DELTA || y.Delta > Constants.MAX_SAFE_DELTA)
            _requestNormalizeDelta = true;
        y.Start += x.Delta;
        y.End += x.Delta;

        x.Right = y.Left; // turn y's left subtree into x's right subtree.
        if (y.Left != IntervalNode.Sentinel)
            y.Left.Parent = x;

        y.Parent = x.Parent; // link x's parent to y.
        if (x.Parent == IntervalNode.Sentinel)
            _root = y;
        else if (x == x.Parent.Left)
            x.Parent.Left = y;
        else
            x.Parent.Right = y;

        y.Left = x; // put x on y's left.
        x.Parent = y;

        // Update MaxEnd
        x.MaxEnd = ComputeMaxEnd(x);
        y.MaxEnd = ComputeMaxEnd(y);
    }

    private void RightRotate(IntervalNode y)
    {
        var x = y.Left;

        y.Delta -= x.Delta; // y's delta is now influenced by x's delta
        if (y.Delta < Constants.MIN_SAFE_DELTA || y.Delta > Constants.MAX_SAFE_DELTA)
            _requestNormalizeDelta = true;
        y.Start -= x.Delta;
        y.End -= x.Delta;

        y.Left = x.Right; // turn x's right subtree into y's left subtree.
        if (x.Right != IntervalNode.Sentinel)
            x.Right.Parent = y;

        x.Parent = y.Parent; // link y's parent to x.
        if (y.Parent == IntervalNode.Sentinel)
            _root = x;
        else if (y == y.Parent.Right)
            y.Parent.Right = x;
        else
            y.Parent.Left = x;

        x.Right = y; // put y on x's right.
        y.Parent = x;

        // Update MaxEnd
        y.MaxEnd = ComputeMaxEnd(y);
        x.MaxEnd = ComputeMaxEnd(x);
    }

    private static IntervalNode Leftest(IntervalNode node)
    {
        while (node.Left != IntervalNode.Sentinel)
            node = node.Left;
        return node;
    }

    #endregion

    #region Max End Computation

    private static int ComputeMaxEnd(IntervalNode node)
    {
        int maxEnd = node.End;
        if (node.Left != IntervalNode.Sentinel)
        {
            maxEnd = Math.Max(maxEnd, node.Left.MaxEnd);
        }
        if (node.Right != IntervalNode.Sentinel)
        {
            maxEnd = Math.Max(maxEnd, node.Right.MaxEnd + node.Delta);
        }
        return maxEnd;
    }

    private static void RecomputeMaxEndWalkToRoot(IntervalNode node)
    {
        while (node != IntervalNode.Sentinel)
        {
            int maxEnd = ComputeMaxEnd(node);
            if(node.MaxEnd == maxEnd)
                return; // No need to go further
            node.MaxEnd = maxEnd;
            node = node.Parent;
        }
    }

    #endregion

    private static int IntervalCompare(int aStart, int aEnd, int bStart, int bEnd)
    {
        if (aStart == bStart)
            return aEnd - aStart;
        return aStart - bStart;
    }
}
