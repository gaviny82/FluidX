using FluidX.Tokenization.TokenStores;
using System.Diagnostics;

namespace FluidX.Tokenization;

// TODO: Use ReaderWriterLockSlim to implement a real background tokenizer that runs in a separate thread.
// Edits to the text buffer must acquire a write lock before changing the text buffer and invalidating tokenizer states,
// while the background tokenizer acquires a read lock before reading text lines and tokenizing.
// The tokenizer must release the read lock periodically to allow edits to proceed on the UI thread.
public sealed class DefaultBackgroundTokenizer : IDisposable
{
    private readonly TokenizerWithStateStoreAndTextModel _tokenizerWithStateStore;
    private readonly IBackgroundTokenizationStore _backgroundTokenStore;

    private int _isScheduled; // 0 = not scheduled, 1 = scheduled
    private bool _isDisposed;

    // Time budget for a whole scheduling slice (rough analog to requestIdleCallback).
    private static readonly TimeSpan BatchBudget = TimeSpan.FromMilliseconds(8);

    // Minimum continuous processing time before yielding (≈1ms).
    private static readonly TimeSpan MinSlice = TimeSpan.FromMilliseconds(1);

    public DefaultBackgroundTokenizer(
        TokenizerWithStateStoreAndTextModel tokenizerWithStateStore,
        IBackgroundTokenizationStore backgroundTokenStore)
    {
        _tokenizerWithStateStore = tokenizerWithStateStore;
        _backgroundTokenStore = backgroundTokenStore;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
    }

    /// <summary>
    /// Entry point when content/attachment changes happen.
    /// Schedules a background pass if needed.
    /// </summary>
    public void HandleChanges()
    {
        BeginBackgroundTokenization();
    }

    /// <summary>
    /// If all states are valid, notify the token store that background tokenization is complete.
    /// </summary>
    public void CheckFinished()
    {
        if (_isDisposed) return;
        if (_tokenizerWithStateStore.Store.AllStatesValid)
        {
            _backgroundTokenStore.BackgroundTokenizationFinished();
        }
    }

    /// <summary>
    /// Requests re-tokenization for the line range [startLineNumber, endLineNumberExclusive).
    /// </summary>
    public void RequestTokens(int startLineNumber, int endLineNumberExclusive)
    {
        _tokenizerWithStateStore.Store.InvalidateEndStateRange(new(startLineNumber, endLineNumberExclusive));
    }

    private void BeginBackgroundTokenization()
    {
        if (_isDisposed || Interlocked.Exchange(ref _isScheduled, 1) == 1)
            return; // Already scheduled or disposed

        // If not scheduled, schedule a background tokenization task only if there is invalid lines.
        if (!HasLinesToTokenize())
        {
            Interlocked.Exchange(ref _isScheduled, 0);
            return;
        }

        _ = Task.Run(BackgroundTokenizeWithBudgetAsync);
    }

    private async Task BackgroundTokenizeWithBudgetAsync()
    {
        try
        {
            var deadline = DateTime.UtcNow + BatchBudget;

            while (HasLinesToTokenize())
            {
                BackgroundTokenizeForAtLeast(MinSlice);

                // Emit the batch we just produced (if any) happens inside BackgroundTokenizeForAtLeast.
                // If we still have time in this budget, yield briefly to avoid monopolizing the thread.
                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                // Yield to the scheduler to keep UI responsive.
                await Task.Yield();
            }
        }
        finally
        {
            // Allow re-scheduling
            Interlocked.Exchange(ref _isScheduled, 0);

            // If there is more to do and we are still attached, schedule again.
            if (!_isDisposed && HasLinesToTokenize())
            {
                BeginBackgroundTokenization();
            }
        }
    }

    /// <summary>
    /// Perform background tokenization work for at least <paramref name="minSlice"/>.
    /// Builds a token batch and submits it to the store.
    /// </summary>
    private void BackgroundTokenizeForAtLeast(TimeSpan minSlice)
    {
        int lineCount = _tokenizerWithStateStore.TextModel.TextBuffer.LineCount;
        var builder = new ContiguousMultilineTokensBuilder();
        var sw = Stopwatch.StartNew();

        do
        {
            if (_isDisposed) break;

            int tokenizedLineNumber = TokenizeOneInvalidLine(builder);
            if (tokenizedLineNumber >= lineCount)
            {
                // Reached end of document or nothing left in this pass
                break;
            }
        }
        while (sw.Elapsed <= minSlice && HasLinesToTokenize());

        // Push out the tokens we accumulated in this slice.
        var chunk = builder.Finalize();
        if (chunk != null && chunk.Count > 0)
        {
            _backgroundTokenStore.SetTokens(chunk.ToArray());
        }

        CheckFinished();
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

    private bool HasLinesToTokenize()
        => !_tokenizerWithStateStore.Store.AllStatesValid;
}