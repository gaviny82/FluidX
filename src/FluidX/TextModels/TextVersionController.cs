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
/// <see cref="VersionId"/> starts at 0 and increments by 1 for each successful action.
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

/// <summary>
/// A component that provides a linear monotonically increasing version history of a mutable <see cref="ITextBuffer"/>.
/// Each version is associated with a <see cref="TextRecord"/>, which captures the states of the <see cref="ITextBuffer"/> at that point in time.
/// Multiple versions can select the same <see cref="TextRecord"/>.
/// A set of spans describing the changes from the transition from the previous record is associated with each <see cref="TextRecord"/>.
/// </summary>
/// <remarks>
/// This component also provides undo, redo and jump operations to restore the buffer to a previous record.
/// Successful undo, redo, and jump operations create new versions, but do not create new records.
/// A jump to the current record is a no-op.
/// The controller maintains a bounded history of records and versions, discarding the oldest records and versions when the limits are exceeded.
/// </remarks>
public sealed class TextVersionController
{
    private readonly CircularBuffer<TextRecord> _records;
    private readonly CircularBuffer<TextVersion> _versions;
    private int _currentRecordIndex; // Logical index of the current record in the buffer, not a RecordId.
    private long _nextRecordId = 0;
    private ITextBuffer _buffer;

    /// <summary>
    /// Monotonic ID of the latest version of the buffer.
    /// </summary>
    /// <remarks>
    /// The initial version of the buffer is 0. Increases by 1 after every action given by <see cref="TextVersionKind"/>.
    /// </remarks>
    public long VersionId { get; private set; }

    /// <summary>
    /// Creates a controller for managing the version history of the <see cref="ITextBuffer"/> provided.
    /// </summary>
    /// <param name="buffer">The text buffer to be managed by this component.</param>
    /// <param name="recordLimit">Maximum retained records, including the current one.</param>
    /// <param name="versionLimit">Maximum retained chronological version metadata.</param>
    public TextVersionController(ITextBuffer buffer, int recordLimit = 100, int versionLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionLimit);
        _buffer = buffer;
        _records = new CircularBuffer<TextRecord>(recordLimit);
        _versions = new CircularBuffer<TextVersion>(versionLimit);
        _records.Append(new TextRecord(0, buffer.CreateSnapshot(), -1, []));
        _versions.Append(new TextVersion(0, -1, 0, TextVersionKind.Initial));
    }

    #region Accessors for record, version and snapshot

    /// <summary>
    /// The record currently represented by <see cref="_buffer"/>.
    /// </summary>
    public TextRecord CurrentRecord => _records[_currentRecordIndex];

    /// <summary>
    /// The current stable read-only buffer view.
    /// </summary>
    public ITextSnapshot CurrentSnapshot => CurrentRecord.Snapshot;

    /// <summary>
    /// The ID of the current record; undo, redo, and jump can move it backward or forward.
    /// </summary>
    public long RecordId => CurrentRecord.RecordId;

    /// <summary>
    /// The latest chronological action.
    /// </summary>
    public TextVersion CurrentVersion => _versions.Last;

    #endregion

    #region Accessors for retained version history and records

    /// <summary>
    /// <see langword="true"/> if there is a retained record before the current one that can be restored with <see cref="Undo"/>.
    /// </summary>
    public bool CanUndo => _currentRecordIndex > 0;

    /// <summary>
    /// <see langword="true"/> if there is a retained record after the current one that can be restored with <see cref="Redo"/>.
    /// </summary>
    public bool CanRedo => _currentRecordIndex + 1 < _records.Count;

    /// <summary>
    /// Returns a copy of currently stored versions in chronological order, oldest first.
    /// </summary>
    public ImmutableArray<TextVersion> GetStoredVersions() => _versions.ToImmutableArray();

    /// <summary>
    /// Returns a copy of currently retained past records, nearest first, excluding the current record.
    /// </summary>
    public ImmutableArray<TextRecord> GetHistoryRecords() =>
        Enumerable.Range(0, _currentRecordIndex)
            .Select(i => _records[_currentRecordIndex - i - 1])
            .ToImmutableArray();

    /// <summary>
    /// Returns a copy of currently retained future records, nearest first, excluding the current record.
    /// </summary>
    public ImmutableArray<TextRecord> GetFutureRecords() =>
        Enumerable.Range(_currentRecordIndex + 1, _records.Count - _currentRecordIndex - 1)
            .Select(i => _records[i])
            .ToImmutableArray();

    /// <summary>
    /// Number of versions currently stored, including <see cref="CurrentVersion"/>.
    /// </summary>
    public int StoredVersionCount => _versions.Count;

    /// <summary>
    /// Gets a stored version relative to the latest version at the time of this call.
    /// Index 0 returns <see cref="CurrentVersion"/>; larger indices return older versions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the stored versions.</exception>
    public TextVersion GetStoredVersion(int indexFromCurrent)
    {
        if ((uint)indexFromCurrent >= (uint)_versions.Count)
            throw new ArgumentOutOfRangeException(nameof(indexFromCurrent));
        return _versions[_versions.Count - 1 - indexFromCurrent];
    }

    /// <summary>
    /// Number of retained past records available for undo, excluding the current record.
    /// </summary>
    public int HistoryRecordCount => _currentRecordIndex;

    /// <summary>
    /// Gets a retained past record relative to the current record at the time of this call.
    /// Index 0 returns the nearest undo record.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list of retained past records.</exception>
    public TextRecord GetHistoryRecord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, HistoryRecordCount);
        return _records[_currentRecordIndex - 1 - index];
    }

    /// <summary>
    /// Number of retained future records available for redo, excluding the current record.
    /// </summary>
    public int FutureRecordCount => _records.Count - _currentRecordIndex - 1;

    /// <summary>
    /// Gets a retained future record relative to the current record at the time of this call.
    /// Index 0 returns the nearest redo record.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list of retained future records.</exception>
    public TextRecord GetFutureRecord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FutureRecordCount);
        return _records[_currentRecordIndex + 1 + index];
    }

    #endregion
{

}
