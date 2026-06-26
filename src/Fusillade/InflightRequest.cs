// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>
/// Tracks a single in-flight request that may be shared by several callers,
/// cancelling the underlying work only once every caller has cancelled.
/// </summary>
/// <param name="onFullyCancelled">Invoked when the last caller cancels.</param>
internal sealed class InflightRequest(Action onFullyCancelled) : IDisposable
{
    /// <summary>The shared response signal observed by all attached callers.</summary>
    private readonly AsyncSignal<HttpResponseMessage> _response = new();

    /// <summary>Serializes buffering of the shared response body for per-caller clones.</summary>
    private readonly SemaphoreSlim _responseBufferGate = new(1, 1);

    /// <summary>The buffered shared response body, if the response has content.</summary>
    private byte[]? _bufferedContent;

    /// <summary>The number of callers currently sharing this request.</summary>
    private int _refCount = 1;

    /// <summary>Tracks whether response content has already been buffered.</summary>
    private bool _contentBuffered;

    /// <summary>Tracks whether the response signal has completed.</summary>
    private int _isCompleted;

    /// <summary>Tracks whether the final caller cancellation has already run.</summary>
    private int _isFullyCancelled;

    /// <summary>Gets the number of callers currently sharing this request.</summary>
    public int ReferenceCount => Volatile.Read(ref _refCount);

    /// <summary>Gets the signal that yields the shared response.</summary>
    public IObservable<HttpResponseMessage> Response => _response;

    /// <summary>Adds a caller reference to this request.</summary>
    public void AddRef() => Interlocked.Increment(ref _refCount);

    /// <summary>Completes the shared response successfully.</summary>
    /// <param name="response">The response to publish to all callers.</param>
    /// <returns><see langword="true"/> when the response was completed by this call.</returns>
    public bool TrySetResult(HttpResponseMessage response)
    {
        if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
        {
            return false;
        }

        _response.OnNext(response);
        _response.OnCompleted();
        return true;
    }

    /// <summary>Completes the shared response with an exception.</summary>
    /// <param name="exception">The exception to publish to all callers.</param>
    /// <returns><see langword="true"/> when the response was completed by this call.</returns>
    public bool TrySetException(Exception exception)
    {
        if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
        {
            return false;
        }

        _response.OnError(exception);
        return true;
    }

    /// <summary>Completes the shared response as cancelled.</summary>
    /// <param name="cancellationToken">The token that cancelled the shared work.</param>
    /// <returns><see langword="true"/> when the response was completed by this call.</returns>
    public bool TrySetCanceled(CancellationToken cancellationToken)
    {
        return TrySetException(new OperationCanceledException(cancellationToken));
    }

    /// <summary>Waits for the shared response, cancelling only this caller when requested.</summary>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The shared response task for this caller.</returns>
    public Task<HttpResponseMessage> WaitAsync(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || _response.IsCompleted)
        {
            return WaitForCallerResponseAsync(CancellationToken.None);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Cancel();
            return CreateCancelledTaskAsync(cancellationToken);
        }

        return WaitWithReferenceCancellationAsync(cancellationToken);
    }

    /// <summary>Removes a caller reference, fully cancelling once none remain.</summary>
    public void Cancel()
    {
        if (Volatile.Read(ref _isCompleted) != 0)
        {
            return;
        }

        var remaining = Interlocked.Decrement(ref _refCount);
        if (remaining < 0)
        {
            _ = Interlocked.Increment(ref _refCount);
            return;
        }

        if (remaining != 0 || Interlocked.Exchange(ref _isFullyCancelled, 1) != 0)
        {
            return;
        }

        onFullyCancelled();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response.Dispose();
        _responseBufferGate.Dispose();
    }

    /// <summary>Creates a task that is already in the cancelled state.</summary>
    /// <param name="cancellationToken">The token to associate with the cancellation.</param>
    /// <returns>A cancelled task.</returns>
    private static Task<HttpResponseMessage> CreateCancelledTaskAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
#if NET5_0_OR_GREATER
        tcs.SetCanceled(cancellationToken);
#else
        _ = cancellationToken;
        tcs.SetCanceled();
#endif
        return tcs.Task;
    }

    /// <summary>Copies HTTP headers from one header collection to another.</summary>
    /// <param name="source">The source headers.</param>
    /// <param name="target">The target headers.</param>
    private static void CopyHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> source, HttpHeaders target)
    {
        foreach (var header in source)
        {
            _ = target.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>Waits for the shared response and returns an independent response instance for this caller.</summary>
    /// <param name="cancellationToken">The caller cancellation token used while buffering content.</param>
    /// <returns>The cloned response.</returns>
    private async Task<HttpResponseMessage> WaitForCallerResponseAsync(CancellationToken cancellationToken)
    {
        var response = await _response.ToTask(CancellationToken.None).ConfigureAwait(false);
        return await CloneResponseForCallerAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates an independent response object for a caller sharing the same in-flight request.</summary>
    /// <param name="response">The shared response.</param>
    /// <param name="cancellationToken">A token used while buffering content.</param>
    /// <returns>A caller-owned response.</returns>
    private async Task<HttpResponseMessage> CloneResponseForCallerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[]? contentBytes = null;
        HttpContentHeaders? contentHeaders = null;
        var content = response.Content;
        if (content is not null)
        {
            contentHeaders = content.Headers;
            await _responseBufferGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_contentBuffered)
                {
#if NET5_0_OR_GREATER
                    _bufferedContent = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
                    _ = cancellationToken;
                    _bufferedContent = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                    _contentBuffered = true;
                }

                contentBytes = _bufferedContent;
            }
            finally
            {
                _ = _responseBufferGate.Release();
            }
        }

        var clone = new HttpResponseMessage(response.StatusCode)
        {
            ReasonPhrase = response.ReasonPhrase,
            RequestMessage = response.RequestMessage,
            Version = response.Version,
        };

        CopyHeaders(response.Headers, clone.Headers);

        if (contentBytes is not null && contentHeaders is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            CopyHeaders(contentHeaders, clone.Content.Headers);
        }

        return clone;
    }

    /// <summary>Waits for the shared response and drops this caller reference if the caller cancels.</summary>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The completed response.</returns>
    private async Task<HttpResponseMessage> WaitWithReferenceCancellationAsync(CancellationToken cancellationToken)
    {
        var responseTask = _response.ToTask(CancellationToken.None);
        var cancellationTask = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        static void OnCanceled(object? state)
        {
            var (request, completion, token) = ((InflightRequest Request, TaskCompletionSource<HttpResponseMessage> Completion, CancellationToken Token))state!;
            request.Cancel();
            _ = completion.TrySetCanceled(token);
        }

#if NET8_0_OR_GREATER
        await using var registration = cancellationToken.Register(
            OnCanceled,
            (this, cancellationTask, cancellationToken));
#else
        using var registration = cancellationToken.Register(
            OnCanceled,
            (this, cancellationTask, cancellationToken));
#endif

        var completedTask = await Task.WhenAny(responseTask, cancellationTask.Task).ConfigureAwait(false);
        if (!ReferenceEquals(completedTask, responseTask))
        {
            _ = responseTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        var response = await completedTask.ConfigureAwait(false);
        return await CloneResponseForCallerAsync(response, CancellationToken.None).ConfigureAwait(false);
    }
}
