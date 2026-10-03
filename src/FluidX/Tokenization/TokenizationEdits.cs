using FluidX.TextBuffers;
using FluidX.TextModels;

namespace FluidX.Tokenization;

/// <summary>Adapts snapshot transitions to the line-based edits required by token stores.</summary>
internal static class TokenizationEdits
{
    /// <summary>Returns edits in descending source order for one transition.</summary>
    public static IReadOnlyList<TextReplacement> GetEdits(TextRecordTransition transition)
    {
        var source = transition.Source.Snapshot;
        var target = transition.Target.Snapshot;
        var edits = new List<TextReplacement>(transition.ChangeSpans.Length);
        int lineDelta = 0;
        bool crossesCrLf = false;
        for (int i = transition.ChangeSpans.Length - 1; i >= 0; i--)
        {
            var span = transition.ChangeSpans[i];
            var range = source.GetRangeAt(span.OldPosition, span.OldLength);
            string text = target.GetTextInRange(target.GetRangeAt(span.NewPosition, span.NewLength));
            edits.Add(new(range, text));
            lineDelta += EOLCounter.CountEOL(text).eolCount - (range.EndLineIndex - range.StartLineIndex);
            crossesCrLf |= InsideCrLf(source, span.OldPosition) || InsideCrLf(source, span.OldEnd)
                || InsideCrLf(target, span.NewPosition) || InsideCrLf(target, span.NewEnd);
        }
        // A CRLF seam can change line structure outside the changed substring. Token stores
        // require complete line boundaries; the public event retains the original exact spans.
        if (crossesCrLf || source.LineCount + lineDelta != target.LineCount)
            return [new(source.GetRangeAt(0, source.Length), target.GetTextInRange(target.GetRangeAt(0, target.Length)))];
        return edits;
    }

    private static bool InsideCrLf(IReadOnlyTextBuffer snapshot, int offset) =>
        offset > 0 && offset < snapshot.Length
        && snapshot.GetChar(offset - 1) == '\r' && snapshot.GetChar(offset) == '\n';
}
