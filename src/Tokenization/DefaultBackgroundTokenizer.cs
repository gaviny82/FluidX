using FluidX.Tokenization.TokenStores;
using System.Diagnostics;

namespace FluidX.Tokenization;

// Tokenization runs on a background task and synchronizes with the main thread using a ReaderWriterLockSlim.
// Edits to the text buffer must acquire a write lock before changing the text buffer and invalidating tokenizer states,
// while the background tokenizer acquires a read lock before reading text lines and tokenizing.
// The tokenizer must release the read lock when the write lock is requested to allow edits to proceed on the UI thread.
public class DefaultBackgroundTokenizer
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TokenizerWithStateStoreAndTextModel _tokenizerWithStateStore;
    private readonly ContiguousTokensStore _tokenStore;

    // TODO: Update BackgroundTokenizationState
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


    private bool _isRunning = false;
    private bool _isWriteLockRequested = false;

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
    /// </summary>
    public void StartBackgroundTokenizationIfNeeded()
    {
        // FIXME: lock is not acquired here, this might lead to race conditions.
        // If the background tokenization task has exited the while loop but not yet reset _isRunning,
        // the following check may incorrectly conclude that a background task is still running. If some lines
        // are invalid at this time, they will not be tokenized until the next call to this function.

        if (Interlocked.Exchange(ref _isRunning, true) == true)
            return; // Background tokenization task already running

        // FIXME: if the background task is already started, setting this to false might
        // lead to multiple background tasks running concurrently and editing the token store at the same time.
        if (!HasLinesToTokenize)
        {
            // Nothing to do, reset _isRunning flag
            Interlocked.Exchange(ref _isRunning, false);
            return;
        }

        // Start a background tokenization task only if there are invalid lines and no tokenization task is running.
        _ = TokenizeInBackgroundAsync();
    }

    public void BeginTextBufferEdit()
    {
        _isWriteLockRequested = true; // Sets a request flag to interrupt background tokenization.
        _lock.Wait(); // Assume the background tokenization task will release the read lock shortly when the requested.
        _isWriteLockRequested = false; // Reset the request flag when the write lock is acquired.
    }

    public void EndTextBufferEdit()
    {
        _lock.Release();
        // The edit may have invalidated some lines or interrupted a background tokenization task,
        // so background tokenization should be restarted.
        StartBackgroundTokenizationIfNeeded();
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

    private async Task TokenizeInBackgroundAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            TimeSpan maxTimeSlice = TimeSpan.FromMilliseconds(2);

            await _lock.WaitAsync(); // Assumes the writer lock held by the UI thread will be released shortly.

            // Keep tokenizing in background until all lines are valid or a write lock is requested (e.g. user edit)
            while (HasLinesToTokenize && !_isWriteLockRequested)
            {
                // To ensure responsiveness, i.e. the read lock can be released quickly,
                // work in this while loop should not take too much time.
                sw.Restart();

                // Tokenize in a fixed-time slice
                var builder = new ContiguousMultilineTokensBuilder();
                while (HasLinesToTokenize && sw.Elapsed < maxTimeSlice)
                {
                    TokenizeOneInvalidLine(builder);
                }

                // Commit the tokens obtained in this slice
                _tokenStore.SetMultilineTokens(builder.Finalize().ToArray(), _tokenizerWithStateStore.TextModel);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, false);
            _lock.Release();
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
}
