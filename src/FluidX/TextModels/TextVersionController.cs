using System;
using System.Collections.Generic;
using System.Text;

namespace FluidX.TextModels;

/* Refactor: TextVersionController
 * - Use a ring buffer of TextSnapshot to store the undo/redo history text buffer snapshots.
 * - - Use a pointer into the ring buffer to track the current version.
 * - Store a monotonic linear history of TextVersion, each version stores the changes.
 * 
 * Operations:
 * - Undo: moves the pointer back to the previous snapshot, creates new TextVersion of type 'undo'
 * - Redo: moves the pointer forward to the next snapshot, creates new TextVersion of type 'redo'
 * - Edit: creates a new TextVersion of type 'edit', stores the changes, creates new snapshot (or supplied by PlainTextModel),
 *         and moves the pointer forward to the new snapshot.
 * - EOLNorm: creates a new TextVersion of type 'eolnorm', stores the changes (changed line original EOLs, new EOL), allowing reverting EOLNorm,
 *            creates new snapshot (or supplied by PlainTextModel), and moves the pointer forward to the new snapshot.
 *
 * TBD: How can we restore an abstract ITextBuffer to an ITextSnapshot? Apply reverse changes?
 *      or swap a pointer? or add ITextSnapshot.CreateBuffer() method to create a new ITextBuffer from the snapshot?
 *
 * Methods:
 * - GetHistoryStack, GetFutureStack, GetCurrentVersion, GetCurrentSnapshot, GetCurrentBuffer
 * - CanUndo, CanRedo
 * - [TBD] Query changes between any two versions?
 * - [TBD] Set the limit of versions and snapshots to keep?
 * - [TBD] allow jumpting to any history/future snapshot? or equivalent to a multi-step undo/redo? should this be O(1) direct jump, or O(N) sequential undo/redo?
 */

public class TextVersionController
{

}
