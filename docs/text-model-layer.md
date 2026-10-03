# Text model layer

The text model layer owns shared document content, edit history, orchestrates background services, and notifies change of content. It provides a general text-file model and a derived code-file model, while leaving interaction and presentation policies to views and application command layers.

## Responsibilities and boundaries

| Component                   | Responsibility                                                                                                                                                                   |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ITextBuffer`               | Text storage, reads, edits, and immutable snapshots. The current model uses `PersistentPieceTreeTextBuffer`.                                                                     |
| `TextModel`                 | General text-file state: validated replacements, document metadata, history navigation, and content notifications. Owns the mutable buffer and exposes immutable read snapshots. |
| `TextVersionController`     | Retained document records and chronological versions. Receives completed changes from the model; does not mutate text, classify content, or publish events.                      |
| `CodeTextModel : TextModel` | Code-file specialization and host for code-oriented services. Currently owns language tokenization; additional service integration is future work.                               |
| View and application layers | Selections, carets, layout, input commands, indentation policy, and other interaction state. Multiple views can share one model.                                                 |

All document mutations go through `TextModel`. The model coordinates buffer mutation, history commit, and notification so consumers observe a committed document state. Writers must be externally serialized; mutations reentered during change notification are rejected. Validation failures leave the model untouched, but execution failures do not have a general rollback guarantee. Subscriber failures propagate after commit.

`CodeTextModel` uses the base model's protected content-change hook to update tokenization before the public event. This keeps code services attached to the same document lifecycle without making the general model depend on language services. Decoration integration and marker movement remain deferred.

## Content and editing

Commands submit explicit `TextReplacement` batches against one pre-edit snapshot. The model validates the batch and applies supplied text verbatim, preserving whitespace and line endings. It does not generate indentation, trim whitespace, or interpret cursor intent. Individual edit boundaries remain observable; callers decide whether their command can combine edits.

`TextEditResult` associates each submitted replacement with its before/after UTF-16 span. This lets commands compute destinations for carets or snippets while keeping selection policy outside the model. These text extents are distinct from valid caret positions; views must apply their own position and affinity rules. Detailed range validation and ordering contracts live in the API comments.

`NormalizeEOL` and `ReplaceContent` are explicit undoable operations using the same history and notification path. Ordinary reads preserve stored line endings; callers can request normalized output without editing the document.

Document EOL classification belongs to each recorded state and is restored with its snapshot. Incremental classification is conservative: `Mixed` means multiple ending kinds may be present, while `Unknown` means there are no line endings. Explicit normalization establishes a uniform ending. BOM is separate, unversioned encoding metadata; character-content flags are conservative model hints.

## Records, versions, and notifications

History separates document state from the sequence of actions:

- A `TextRecord` captures an immutable snapshot, EOL classification, and change spans from its source record.
- A `TextVersion` identifies a committed action in chronological order, including its kind, source/target record IDs, and optional caller metadata.
- A `TextRecordTransition` describes movement between two records, with UTF-16 spans oriented in the direction of the action.

Edits, normalization, and replacement create records. Undo, redo, and jump select retained records and create new versions without duplicating those records. Navigation restores a snapshot where supported, otherwise replays retained transitions. Editing after navigation discards the redo path. Saved snapshots remain usable for explicit replacement after their records leave retained history.

Every valid nonempty edit batch commits one record, version, and event, even when the resulting characters are unchanged. Whole-content replacement also commits without an equality check. Empty batches and other no-op operations leave history untouched. There is currently no edit coalescing or undo grouping.

Records and chronological versions have independent retention limits, currently 1,000 each. Retaining a version does not keep its referenced snapshots alive. Consumers holding records or snapshots retain that content independently of the controller.

`ContentChanged` carries the committed version, before/after records, and ordered transitions. Each transition has its own source and target coordinate spaces, allowing consumers to map anchors through edits and history navigation. Consumers can obtain change text from the snapshots as needed.

Optional `TextModelOperationMetadata` lets higher layers attach immutable command context to an action without the model interpreting it. Metadata belongs to that invocation, including undo or redo; it does not itself implement selection restoration or command correlation. Read-only history queries expose retained states and actions without exposing the controller.

## View and application design

The model represents actual characters shared by all views. The planned `ITextView` owns carets, selections, cursor affinity, virtual columns, and logical-to-display mapping. `TabSize`, `IndentSize`, `InsertSpaces`, and `DefaultEOL` belong to view/application settings. Enter's newline choice is command policy, separate from the document's recorded EOL classification.

Automatic indentation on an empty new line is planned as view-local virtual state. Enter at line end inserts only a newline; a later command materializes indentation together with content in one batch. Splitting before real suffix text inserts actual indentation immediately. Leaving virtual space creates no cleanup edit, and existing real whitespace remains document content. See the [virtual-indentation design](auto-whitespace-design.md) for command behavior.

Views map real anchors through model transitions and validate virtual state when its anchor changes. Virtual columns never become model offsets. Multiple views can therefore maintain different virtual positions over the same shared text without generating document edits.

## Future work

The following view and application features remain outside the current model. They include responsibilities previously embedded in the legacy text model:

- Implement `ITextView` selections, multiple carets, affinity, wrapping, display-line mapping, rendering, hit testing, and virtual-only notifications.
- Implement virtual indentation and settings ownership. Finalize Space/Tab/Backspace behavior, whitespace-only splits, paste/snippets, rectangular selection and copy, multi-caret conflict resolution, and the effect of settings changes on virtual targets and layout. Any stored trailing-whitespace cleanup should be an explicit command or save policy.
- Add selection checkpoints for undo/redo and command/event correlation to avoid processing an originating edit twice. Decide whether asynchronous commands need expected-version checks.
- Design IME composition and cancellation, including rendering pending text over committed content and choosing when indentation materializes. Pending composition is view state, outside committed model history.
- Resume decoration and marker-movement design, including service ownership, update hooks, and any specialized operation metadata.

Further model-layer work includes edit coalescing, explicit undo boundaries, record merging, CR normalization support in buffers and `TextModel.NormalizeEOL`, and memory-based retention. Performance work may combine initial metadata scans, improve EOL classification, and choose between transition replay and full replacement for navigation.

