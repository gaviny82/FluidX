using System;
using System.Collections.Generic;
using System.Text;

namespace FluidX.TextModels;

/// <summary>
/// The action that produced a new <see cref="TextVersion"/>.
/// </summary>
public enum TextVersionKind { Initial, Edit, EolNormalization, Replacement, Undo, Redo, Jump }

/// <summary>
/// Represents a chronological version of the <see cref="ITextBuffer"/> created by an action applied to the text model.
/// <see cref="VersionId"/> increases monotonically from 0 for every action.
/// <see cref="TargetRecordId"/> can return to an earlier value on undo, redo, or jump.
/// </summary>
/// <param name="VersionId">Monotonic ID of this version.</param>
/// <param name="SourceRecordId">Record ID selected before the action; -1 for the initial version.</param>
/// <param name="TargetRecordId">Record ID selected after the action.</param>
/// <param name="Kind">The action performed that creates this version.</param>
public sealed record TextVersion(
    long VersionId,
    long SourceRecordId,
    long TargetRecordId,
    TextVersionKind Kind);

/// <summary>
/// A span of content changed between two <see cref="TextRecord"/>s. The coordinates are
/// measured in UTF-16 offsets in the snapshots before and after an edit.
/// </summary>
/// <param name="OldPosition">Start offset in the preceding record.</param>
/// <param name="OldLength">Length removed from the preceding record.</param>
/// <param name="NewPosition">Start offset in the new record.</param>
/// <param name="NewLength">Length inserted in the new record.</param>
public readonly record struct TextChangeSpan(int OldPosition, int OldLength, int NewPosition, int NewLength)
{
    public int OldEnd => OldPosition + OldLength;
    public int NewEnd => NewPosition + NewLength;
}


/// <summary>
/// An immutable captured buffer state with change spans from its source record. The record ID is unique and monotonically increasing from 0.
/// A new record is only created when the buffer is mutated. Undo, redo, and jump to a different record do not create new records.
/// </summary>
/// <param name="RecordId">Unique ID assigned when the new buffer state was recorded.</param>
/// <param name="Snapshot">The capture immutable state of the text buffer.</param>
/// <param name="SourceRecordId">The record ID from which this state was created, even if it has since been evicted; -1 for the initial record.</param>
/// <param name="ChangeSpans">Changed UTF-16 spans from <see cref="SourceRecordId"/> to this record. Change text can be
/// retrieved from the two snapshots on demand. Empty for the initial record. </param>
public sealed record TextRecord(
    long RecordId,
    ITextSnapshot Snapshot,
    long SourceRecordId,
    ImmutableArray<TextChangeSpan> ChangeSpans);

/// <summary>
/// Represents a transition from one <see cref="TextRecord"/> to another through retained record history.
/// Changes are oriented from <see cref="Source"/> to <see cref="Target"/>.
/// </summary>
/// <remarks>A reverse transition swaps each span's old and new coordinates.</remarks>
public readonly record struct TextRecordTransition(TextRecord Source, TextRecord Target,
    ImmutableArray<TextChangeSpan> ChangeSpans)
{
    /// <summary>
    /// Reads old and new text from the snapshots when a caller needs change content.
    /// </summary>
    public ImmutableArray<TextChange> GetTextChanges()
    {
        var changes = ImmutableArray.CreateBuilder<TextChange>(ChangeSpans.Length);
        foreach (TextChangeSpan span in ChangeSpans)
        {
            string oldText = Source.Snapshot.GetTextInRange(
                Source.Snapshot.GetRangeAt(span.OldPosition, span.OldLength));
            string newText = Target.Snapshot.GetTextInRange(
                Target.Snapshot.GetRangeAt(span.NewPosition, span.NewLength));
            changes.Add(new TextChange(span.OldPosition, oldText, span.NewPosition, newText));
        }
        return changes.MoveToImmutable();
    }
}
{

}
