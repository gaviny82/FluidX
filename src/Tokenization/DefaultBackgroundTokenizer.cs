using FluidX.Tokenization.TokenStores;
using System.Diagnostics;

namespace FluidX.Tokenization;

// Tokenization runs on a background task and synchronizes with the main thread using a SemaphoreSlim.
// Edits to the text buffer must acquire a write lock before changing the text buffer and invalidating tokenizer states,
// while the background tokenizer acquires a read lock before reading text lines and tokenizing.
// The tokenizer must release the read lock when the write lock is requested to allow edits to proceed on the UI thread.
// Future work:
// 1. implement lock-free reads using per-line atomic replacements or snapshoting
// so rendering does not require the write lock.
// 2. Cancellation token + clean task exit
public class DefaultBackgroundTokenizer : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _workAvailable = new(0, 1);

    private readonly TokenizerWithStateStoreAndTextModel _tokenizerWithStateStore;
    private readonly ContiguousTokensStore _tokenStore;

    public TokenizerWithStateStoreAndTextModel TokenizerWithStateStore
        => _tokenizerWithStateStore;

    public BackgroundTokenizationState BackgroundTokenizationState
    {
        get => field;
        set
        {
            if (field == value) return;
            // Only fires the event when the state actually changes
            field = value;
            BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = BackgroundTokenizationState.InProgress;

    public EventHandler? BackgroundTokenizationStateChanged;

    //TODO: TokensChanged event


    private Task? _tokenizationTask = null;
    private volatile bool _isWriteRequested = false; // Must be volatile to ensure visibility across threads.
    private CancellationTokenSource _cts = new();

    private bool HasLinesToTokenize
        => !_tokenizerWithStateStore.Store.AllStatesValid;


    public DefaultBackgroundTokenizer(
        TokenizerWithStateStoreAndTextModel tokenizerWithStateStore,
        ContiguousTokensStore tokenStore)
    {
        _tokenizerWithStateStore = tokenizerWithStateStore;
        _tokenStore = tokenStore;
    }

    /// <summary>
    /// Requests the background tokenization worker to tokenize the document if there is any invalid lines.
    /// This must be called when the write lock is acquired by calling <see cref="BeginTextBufferEdit"/>.
    /// </summary>
    public void StartBackgroundTokenizationIfNeeded()
    {
        // Starts the background tokenization task if not already running.
        // TODO: A lock might be needed to guard this, if called from multiple threads.
        // If there is always a single UI thread calling this, then it is fine.
        if (_tokenizationTask is null)
            _tokenizationTask = TokenizeInBackgroundAsync(_cts.Token);

        if (HasLinesToTokenize && _workAvailable.CurrentCount == 0)
        {
            try
            {
                _workAvailable.Release(); // Signals the background task to continue tokenization
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Obtains the write lock and interrupts the background tokenizer. Must be called on the UI thread only,
    /// and before making any edits to the text buffer or reading the token store.
    /// Must call <see cref="StartBackgroundTokenizationIfNeeded"/> to resume tokenization.
    /// </summary>
    public void BeginTextBufferEdit()
    {
        _isWriteRequested = true; // Sets a request flag to interrupt background tokenization.
        _writeLock.Wait(); // Assume the background tokenization task will release the read lock shortly when the requested.
        _isWriteRequested = false; // Reset the request flag when the write lock is acquired.
    }

    /// <summary>
    /// Releases the write lock and allows the background tokenizer to resume.
    /// </summary>
    public void EndTextBufferEdit()
    {
        _writeLock.Release();
        // The edit may have invalidated some lines or interrupted a background tokenization task,
        // so background tokenization should be restarted.
        StartBackgroundTokenizationIfNeeded();
    }

    private async Task TokenizeInBackgroundAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        TimeSpan maxTimeSlice = TimeSpan.FromMilliseconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _workAvailable.WaitAsync(cancellationToken).ConfigureAwait(false); // Only continues if the UI thread signals that there is work to do.
                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false); // Acquires the write lock to wait for any ongoing edits to complete.

                // Tokenize in fixed-time slices to ensure responsiveness
                while (!_isWriteRequested)
                {
                    // Exit and notify when all lines are valid
                    if (!HasLinesToTokenize)
                    {
                        BackgroundTokenizationState = BackgroundTokenizationState.Done;
                        break;
                    }
                    // Begins a new slice
                    BackgroundTokenizationState = BackgroundTokenizationState.InProgress;
                    var builder = new ContiguousMultilineTokensBuilder();
                    sw.Restart();
                    // When a text edit is requested, the slice is stopped and comitted immediately when tokenization
                    // of the current line is done.
                    while (HasLinesToTokenize && sw.Elapsed < maxTimeSlice && !_isWriteRequested)
                    {
                        TokenizeOneInvalidLine(builder);
                    }
                    // Commit the tokens obtained in this slice
                    _tokenStore.SetMultilineTokens(builder.Finalize().ToArray(), _tokenizerWithStateStore.TextModel);
                }
            }
            finally
            {
                _writeLock.Release(); // Releases the write lock to allow the UI thread to proceed with the edit.
            }
        }
    }

    /// <summary>
    /// Tokenizes the next invalid line (if any) and appends the result to the builder.
    /// Returns the tokenized line number or lineCount+1 if none were processed.
    /// </summary>
    private int TokenizeOneInvalidLine(ContiguousMultilineTokensBuilder builder)
    {
        var firstInvalid = _tokenizerWithStateStore.GetFirstInvalidLine();
        if (firstInvalid == null)
        {
            return _tokenizerWithStateStore.TextModel.TextBuffer.LineCount + 1;
        }

        _tokenizerWithStateStore.UpdateTokensUntilLine(builder, firstInvalid.Value.lineNumber);
        return firstInvalid.Value.lineNumber;
    }

    /// <summary>
    /// Requests re-tokenization for the line range. The caller must call <see cref="BeginTextBufferEdit"/>
    /// to acquire the write lock before calling this method.
    /// </summary>
    /// <param name="range">The range of lines to invalidate.</param>
    public void InvalidateLines(Range range)
    {
        _tokenizerWithStateStore.Store.InvalidateEndStateRange(range);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _tokenizationTask?.Wait();
        _writeLock.Dispose();
        _workAvailable.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
