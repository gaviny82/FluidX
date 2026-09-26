namespace FluidX.TextBuffers;

/// <summary>
/// Optional support for fast restoration of a <see cref="ITextSnapshot"/>.
/// </summary>
public interface ISnapshotRestorableTextBuffer : ITextBuffer
{
    /// <summary>
    /// Restores <paramref name="snapshot"/> when compatible. Returning <see langword="false"/>
    /// must leave the buffer unchanged.
    /// </summary>
    bool TryRestoreSnapshot(ITextSnapshot snapshot);
}
