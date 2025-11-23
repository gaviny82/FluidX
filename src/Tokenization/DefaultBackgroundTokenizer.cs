using FluidX.Tokenization.TokenStores;
using System.Diagnostics;

namespace FluidX.Tokenization;

// TODO: Use ReaderWriterLockSlim to implement a real background tokenizer that runs in a separate thread.
// Edits to the text buffer must acquire a write lock before changing the text buffer and invalidating tokenizer states,
// while the background tokenizer acquires a read lock before reading text lines and tokenizing.
// The tokenizer must release the read lock periodically to allow edits to proceed on the UI thread.
public class DefaultBackgroundTokenizer
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly TokenizerWithStateStoreAndTextModel _tokenizerWithStateStore;
    private readonly ContiguousTokensStore _tokenStore;

    public BackgroundTokenizationState BackgroundTokenizationState
    {
        get => field;
        set
        {
            field = value;
            BackgroundTokenizationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    } = BackgroundTokenizationState.InProgress;

    public EventHandler? BackgroundTokenizationStateChanged;


    private bool _isRunning = false;

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
        if (Interlocked.Exchange(ref _isRunning, true) == true)
            return; // Background tokenization task already running

        if (!HasLinesToTokenize)
        {
            // Nothing to do, reset _isRunning flag
            Interlocked.Exchange(ref _isRunning, false);
            return;
        }

        // Start a background tokenization task only if there are invalid lines and no tokenization task is running.
        _ = TokenizeInBackgroundAsync();
    }

    /// <summary>
    /// Requests re-tokenization for the line range. The caller must acquire the
    /// write lock on <see cref="TokenizerLock"/> before calling this method.
    /// </summary>
    /// <param name="range">The range of lines to invalidate.</param>
    public void InvalidateLines(Range range)
    {
        _lock.EnterWriteLock();
        _tokenizerWithStateStore.Store.InvalidateEndStateRange(range);
        _lock.ExitWriteLock();
    }

    private async Task TokenizeInBackgroundAsync()
    {
        try
        {
            _lock.EnterReadLock();
            var builder = new ContiguousMultilineTokensBuilder();
            // Keep tokenizing in background until all lines are valid
            // or a write lock is requested (e.g. user edit)
            while (HasLinesToTokenize)
            {
                TokenizeOneInvalidLine(builder);
            }
            // Commit the tokens obtained in this task
            var chunk = builder.Finalize();
            _tokenStore.SetMultilineTokens(chunk.ToArray(), _tokenizerWithStateStore.TextModel);
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, false);
            _lock.ExitReadLock();
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
