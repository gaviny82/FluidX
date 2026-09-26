using System.Collections.Immutable;
using FluidX.Common.DataStructures;
using FluidX.TextBuffers;

namespace FluidX.TextModels;

/// <summary>
/// The action intent saved in a <see cref="TextVersion"/>.
/// </summary>
/// <remarks>
/// <see cref="Undo"/>, <see cref="Redo"/>, and <see cref="Jump"/> remain distinct even when they select the same target record.
/// <see cref="Initial"/> marks the starting state.
/// </remarks>
public enum TextVersionKind { Initial, Edit, EolNormalization, Replacement, Undo, Redo, Jump }

/// <summary>
/// Represents a chronological version of the <see cref="ITextBuffer"/> created by an action applied to the text model.
/// <see cref="VersionId"/> starts at 0 and increments by 1 for each successful action.
/// <see cref="TargetRecordId"/> can return to an earlier value on undo, redo, or jump.
/// </summary>
/// <param name="VersionId">Monotonic ID of this version.</param>
/// <param name="SourceRecordId">Record ID selected before the action; -1 for the initial version.</param>
/// <param name="TargetRecordId">Record ID selected after the action.</param>
/// <param name="Kind">The intent of action creating this version, or <see cref="TextVersionKind.Initial">
/// for the initial state when the <see cref="TextVersionController"/> is created.</param>
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
/// An immutable captured buffer state with change spans from its source record. Record IDs
/// start from 0 and increase monotonically. Edits, EOL rewrites, and explicit replacements create new records;
/// undo, redo, and jump select existing records.
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
/// The owner applies buffer changes and restores snapshots, then records the completed
/// operation here. Successful navigation creates a version but no new record.
/// Old records and versions are evicted independently when their limits are exceeded.
/// This class only manages <see cref="TextVersion"/> and <see cref="TextRecord"/> entries,
/// and does not mutate a buffer or publish model content notifications.
/// </remarks>
public sealed class TextVersionController
{
    private readonly CircularBuffer<TextRecord> _records;
    private readonly CircularBuffer<TextVersion> _versions;
    private int _currentRecordIndex; // Logical index of the current record in the buffer, not a RecordId.
    private long _nextRecordId = 0;

    /// <summary>
    /// Monotonic ID of the latest version of the buffer.
    /// </summary>
    /// <remarks>
    /// The initial version is 0. Each completed edit, EOL rewrite, replacement, or
    /// navigation to another record increments it by 1.
    /// </remarks>
    public long VersionId { get; private set; }

    /// <summary>
    /// Creates a controller for managing the version history of the <see cref="ITextBuffer"/> provided.
    /// </summary>
    /// <param name="initialSnapshot">The initial buffer state when this controller is initialized.</param>
    /// <param name="recordLimit">Maximum retained records, including the current one.</param>
    /// <param name="versionLimit">Maximum retained chronological version metadata.</param>
    public TextVersionController(ITextSnapshot initialSnapshot, int recordLimit = 100, int versionLimit = 1000)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionLimit);
        _records = new CircularBuffer<TextRecord>(recordLimit);
        _versions = new CircularBuffer<TextVersion>(versionLimit);
        _records.Append(new TextRecord(0, initialSnapshot, -1, []));
        _versions.Append(new TextVersion(0, -1, 0, TextVersionKind.Initial));
    }

    #region Accessors for record, version and snapshot

    /// <summary>
    /// The record currently selected by this controller.
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
    /// <see langword="true"/> if a preceding record is available for undo navigation.
    /// </summary>
    public bool CanUndo => _currentRecordIndex > 0;

    /// <summary>
    /// <see langword="true"/> if a following record is available for redo navigation.
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

    /// <summary>
    /// Gets a retained record by its ID on the undo/redo path.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The record is not retained.</exception>
    public TextRecord GetRecord(long recordId) => _records[FindRecordIndex(recordId)];

    /// <summary>
    /// Returns ordered adjacent transitions from one retained record to another.
    /// Change coordinates in each transition refer to that transition's two snapshots.
    /// </summary>
    /// <remarks>Returns an empty array when both IDs are equal, and throws if either record is no longer retained.</remarks>
    public ImmutableArray<TextRecordTransition> GetTransitionsBetweenRecords(long fromRecordId, long toRecordId)
    {
        int from = FindRecordIndex(fromRecordId);
        int to = FindRecordIndex(toRecordId);
        var result = ImmutableArray.CreateBuilder<TextRecordTransition>(Math.Abs(to - from));
        if (from < to)
        {
            for (int i = from + 1; i <= to; i++)
            {
                result.Add(new TextRecordTransition(_records[i - 1], _records[i],
                    _records[i].ChangeSpans));
            }
        }
        else
        {
            for (int i = from; i > to; i--)
            {
                // Reverse change spans for backward transitions
                result.Add(new TextRecordTransition(_records[i], _records[i - 1],
                    _records[i].ChangeSpans.Select(change => new TextChangeSpan(
                        change.NewPosition, change.NewLength, change.OldPosition, change.OldLength))
                    .ToImmutableArray()));
            }
        }
        return result.MoveToImmutable();
    }

    #endregion

    #region Commit versions and records after actions

    /// <summary>
    /// Commit a version after a buffer edit. This creates a record and discards redo history.
    /// </summary>
    /// <param name="changes">The change spans of the edit operation.</param>
    /// <param name="snapshot">The resulting snapshot after applying the edit.</param>
    /// <remarks>
    /// The caller must supply ordered, nonoverlapping spans with valid UTF-16 coordinates
    /// that describe the full change from the current snapshot to <paramref name="snapshot"/>.
    /// Otherwise, the behavior is undefined.
    /// </remarks>
    public TextVersion CommitEdit(ImmutableArray<TextChangeSpan> changes, ITextSnapshot snapshot)
    {
        if (changes.IsDefault)
            throw new ArgumentException("Changes must be an initialized immutable array.", nameof(changes));
        ArgumentNullException.ThrowIfNull(snapshot);
        return AppendRecord(TextVersionKind.Edit, changes, snapshot);
    }

    /// <summary>
    /// Commit a version after an EOL normalization. This creates a record and discards redo history.
    /// </summary>
    /// <param name="changes">The change spans of the edit operation.</param>
    /// <param name="snapshot">The resulting snapshot after applying the EOL normalization.</param>
    /// <remarks>
    /// The caller must supply ordered, nonoverlapping spans with valid UTF-16 coordinates
    /// that describe the full change from the current snapshot to <paramref name="snapshot"/>.
    /// Otherwise, the behavior is undefined.
    /// </remarks>
    public TextVersion CommitEolNormalization(ImmutableArray<TextChangeSpan> changes, ITextSnapshot snapshot)
    {
        if (changes.IsDefault)
            throw new ArgumentException("Changes must be an initialized immutable array.", nameof(changes));
        ArgumentNullException.ThrowIfNull(snapshot);
        return AppendRecord(TextVersionKind.EolNormalization, changes, snapshot);
    }

    /// <summary>
    /// Commit a version after an undo, redo, or jump to an explicity record.
    /// </summary>
    /// <param name="kind">The kind of navigation. Must be either <see cref="TextVersionKind.Undo"/>,
    /// <see cref="TextVersionKind.Redo"/>, or <see cref="TextVersionKind.Jump"/>.</param>
    /// <param name="targetRecordId">The ID of the target record to navigate to.</param>
    /// <remarks>
    /// Undo and redo must select the adjacent past or future record respectively; an
    /// explicit jump may select any retained record, including an adjacent one.
    /// A jump to the current record is a no-op.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The target record is not retained.</exception>
    /// <exception cref="ArgumentException">The kind or target direction is invalid.</exception>
    public TextVersion CommitNavigation(TextVersionKind kind, long targetRecordId)
    {
        if (kind is not (TextVersionKind.Undo or TextVersionKind.Redo or TextVersionKind.Jump))
            throw new ArgumentException("Expected Undo, Redo, or Jump.", nameof(kind));

        int target = FindRecordIndex(targetRecordId);
        if (kind == TextVersionKind.Undo && target != _currentRecordIndex - 1 ||
            kind == TextVersionKind.Redo && target != _currentRecordIndex + 1)
            throw new ArgumentException("The target is not the adjacent record for this action.", nameof(targetRecordId));

        if (target == _currentRecordIndex)
            return CurrentVersion;

        long sourceRecordId = RecordId;
        _currentRecordIndex = target;
        return AppendVersion(kind, sourceRecordId);
    }

    /// <summary>
    /// Commit a full buffer replacement using the supplied snapshot. The caller
    /// must restore or replace the buffer first.
    /// </summary>
    /// <remarks>
    /// Redo history is discarded. The former record remains undoable while retained.
    /// </remarks>
    public TextVersion CommitReplacement(ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ImmutableArray<TextChangeSpan> changes =
            [new TextChangeSpan(0, CurrentSnapshot.Length, 0, snapshot.Length)];
        return AppendRecord(TextVersionKind.Replacement, changes, snapshot);
    }

    #endregion

    #region Helpers

    private int FindRecordIndex(long recordId)
    {
        const int LinearSearchThreshold = 16;
        if (_records.Count <= LinearSearchThreshold)
        {
            // Linear search is faster for small collections.
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].RecordId == recordId) return i;
        }
        else
        {
            // Record IDs increase across the retained path, including after redo history is discarded.
            // Use O(log n) binary search for larger collections.
            int low = 0;
            int high = _records.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                long middleId = _records[middle].RecordId;
                if (middleId == recordId) return middle;
                if (middleId < recordId) low = middle + 1;
                else high = middle - 1;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(recordId), "The record is not in retained undo/redo history.");
    }

    private TextVersion AppendRecord(TextVersionKind kind,
        ImmutableArray<TextChangeSpan> changes, ITextSnapshot snapshot)
    {
        long fromRecordId = RecordId;
        long nextId = checked(_nextRecordId + 1);
        // Drop redo records. External holders of their snapshots remain unaffected.
        _records.Truncate(_currentRecordIndex + 1);
        _records.Append(new TextRecord(nextId, snapshot, fromRecordId, changes));
        _nextRecordId = nextId;
        _currentRecordIndex = _records.Count - 1;
        return AppendVersion(kind, fromRecordId);
    }

    private TextVersion AppendVersion(TextVersionKind kind, long fromRecordId)
    {
        long nextVersion = checked(VersionId + 1);
        var version = new TextVersion(nextVersion, fromRecordId, RecordId, kind);
        _versions.Append(version);
        VersionId = nextVersion;
        return version;
    }

    #endregion

}

/*
 * Legacy buffer-mutating APIs, retained for reference while PlainTextModel's
 * restoration path is designed. These are intentionally outside the controller.
 *
 * public TextVersion? Undo() => CanUndo ? MoveTo(_currentRecordIndex - 1, TextVersionKind.Undo) : null;
 * public TextVersion? Redo() => CanRedo ? MoveTo(_currentRecordIndex + 1, TextVersionKind.Redo) : null;
 *
 * public TextVersion JumpToRecord(long recordId)
 * {
 *     for (int i = 0; i < _records.Count; i++)
 *         if (_records[i].RecordId == recordId)
 *             return MoveTo(i, TextVersionKind.Jump);
 *     throw new ArgumentOutOfRangeException(nameof(recordId), "The record is not retained.");
 * }
 *
 * public TextVersion ReplaceWithRecord(TextRecord source)
 * {
 *     ArgumentNullException.ThrowIfNull(source);
 *     ITextSnapshot before = CurrentSnapshot;
 *     if (_buffer is not ISnapshotRestorableTextBuffer restorable ||
 *         !restorable.TryRestoreSnapshot(source.Snapshot))
 *     {
 *         string text = source.Snapshot.GetTextInRange(
 *             source.Snapshot.GetRangeAt(0, source.Snapshot.Length));
 *         _buffer.ApplyEdits([new TextReplacement(_buffer.GetRangeAt(0, _buffer.Length), text)]);
 *     }
 *     ITextSnapshot after = _buffer.CreateSnapshot();
 *     ImmutableArray<TextChangeSpan> changes =
 *         [new TextChangeSpan(0, before.Length, 0, after.Length)];
 *     return AppendRecord(TextVersionKind.Replacement, changes, after);
 * }
 *
 * private TextVersion MoveTo(int target, TextVersionKind kind)
 * {
 *     if (target == _currentRecordIndex) return CurrentVersion;
 *     long fromRecordId = RecordId;
 *     TextRecord destination = _records[target];
 *     if (_buffer is not ISnapshotRestorableTextBuffer restorable ||
 *         !restorable.TryRestoreSnapshot(destination.Snapshot))
 *     {
 *         if (target < _currentRecordIndex)
 *             for (int i = _currentRecordIndex; i > target; i--)
 *                 ApplyRawChanges(_records[i].Snapshot, _records[i - 1].Snapshot,
 *                     _records[i].ChangeSpans, reverse: true);
 *         else
 *             for (int i = _currentRecordIndex + 1; i <= target; i++)
 *                 ApplyRawChanges(_records[i - 1].Snapshot, _records[i].Snapshot,
 *                     _records[i].ChangeSpans, reverse: false);
 *     }
 *     _currentRecordIndex = target;
 *     return AppendVersion(kind, fromRecordId);
 * }
 *
 * ApplyRawChanges and its CRLF-boundary full-replacement fallback belong in the
 * model's restoration helper, alongside model metadata and event updates.
 */
