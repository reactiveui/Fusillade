// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
#if REACTIVE_SHIM
using Punchclock.Reactive;
#else
using Punchclock;
#endif

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>A http handler which will limit the rate at which we can read.</summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RateLimitedHttpMessageHandler"/> class.
/// </remarks>
/// <param name="handler">The handler we are wrapping.</param>
/// <param name="basePriority">The base priority of the request.</param>
/// <param name="priority">The priority of the request.</param>
/// <param name="maxBytesToRead">The maximum number of bytes we can read.</param>
/// <param name="opQueue">The operation queue on which to run the operation.</param>
/// <param name="cacheResultFunc">A method that is called if we need to get cached results.</param>
public class RateLimitedHttpMessageHandler(
    HttpMessageHandler? handler,
    Priority basePriority,
    int priority = 0,
    long? maxBytesToRead = null,
    OperationQueue? opQueue = null,
    Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResultFunc = null)
    : LimitingHttpMessageHandler(handler)
{
    /// <summary>Buffer size (32 KB) used when copying a response body so it can be cached.</summary>
    private const int CopyBufferSize = 32 * 1024;

    /// <summary>The effective queue priority (base priority plus offset).</summary>
    private readonly int _priority = (int)basePriority + priority;

    /// <summary>The set of in-flight requests, keyed by their unique request key.</summary>
    private readonly Dictionary<string, InflightRequest> _inflightResponses = [];

    /// <summary>The remaining byte budget, or <see langword="null"/> for unlimited.</summary>
    private long? _maxBytesToRead = maxBytesToRead;

    /// <summary>
    /// Gets the number of distinct requests currently in flight (one entry per
    /// unique request key). Exposed so tests can deterministically observe
    /// debouncing rather than relying on timing.
    /// </summary>
    internal int InflightRequestCount
    {
        get
        {
            lock (_inflightResponses)
            {
                return _inflightResponses.Count;
            }
        }
    }

    /// <summary>
    /// Gets the total number of callers attached across all in-flight requests.
    /// Two requests that debounce onto the same key contribute a count of two
    /// while only producing a single <see cref="InflightRequestCount"/> entry.
    /// </summary>
    internal int TotalInflightReferenceCount
    {
        get
        {
            lock (_inflightResponses)
            {
                return _inflightResponses.Values.Sum(static x => x.ReferenceCount);
            }
        }
    }

    /// <summary>Generates a unique key for a <see cref="HttpRequestMessage"/>.</summary>
    /// <param name="request">The request to generate a unique key for.</param>
    /// <returns>The unique key.</returns>
    public static string UniqueKeyForRequest(HttpRequestMessage request)
    {
        ArgumentExceptionHelper.ThrowIfNull(request);

        var ret = new[]
        {
            request.RequestUri?.ToString(),
            request.Method.Method,
            request.Headers.Accept.ConcatenateAll(x => x.CharSet + x.MediaType),
            request.Headers.AcceptEncoding.ConcatenateAll(x => x.Value),
            (request.Headers.Referrer ?? new Uri("http://example")).AbsoluteUri,
            request.Headers.UserAgent.ConcatenateAll(x => x.Product is not null ? x.Product.ToString() : x.Comment!),
        }.Aggregate(
            new StringBuilder(),
            (acc, x) =>
            {
                acc.AppendLine(x);
                return acc;
            });

        if (request.Headers.Authorization is not null)
        {
            ret.Append(request.Headers.Authorization.Parameter).AppendLine(request.Headers.Authorization.Scheme);
        }

        return "HttpSchedulerCache_" + ret.ToString().GetHashCode().ToString("x", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override void ResetLimit(long? maxBytesToRead) => _maxBytesToRead = maxBytesToRead;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentExceptionHelper.ThrowIfNull(request);

        var method = request.Method;
        if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options)
        {
            return base.SendAsync(request, cancellationToken);
        }

        if (_maxBytesToRead < 0)
        {
            return CreateCancelledTaskAsync(cancellationToken);
        }

        var cacheResult = cacheResultFunc;
        if (cacheResult is null && NetCache.RequestCache is not null)
        {
            cacheResult = NetCache.RequestCache.SaveAsync;
        }

        var key = UniqueKeyForRequest(request);
        CancellationTokenSource realToken;
        InflightRequest ret;

        lock (_inflightResponses)
        {
            if (_inflightResponses.TryGetValue(key, out var existingRequest))
            {
                existingRequest.AddRef();

                return existingRequest.WaitAsync(cancellationToken);
            }

            realToken = new();
            var token = realToken;
            ret = new(() =>
            {
                lock (_inflightResponses)
                {
                    _inflightResponses.Remove(key);
                }

                try
                {
                    token.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The underlying request already completed and disposed the
                    // token, so there is nothing left to cancel.
                }
            });

            _inflightResponses[key] = ret;
        }

        var queue = opQueue ?? NetCache.OperationQueue;

        _ = CompleteInflightRequestAsync(queue, ret, request, key, realToken, cacheResult);

        return ret.WaitAsync(cancellationToken);
    }

    /// <summary>Creates a task that is already in the cancelled state.</summary>
    /// <param name="cancellationToken">The token to associate with the cancellation.</param>
    /// <returns>A cancelled task.</returns>
    [SuppressMessage("Major Code Smell", "S1172:Unused method parameters should be removed", Justification = "Not used in net framework")]
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

    /// <summary>
    /// Buffers the response body so it can be cached, returning a fresh response with the buffered content.
    /// </summary>
    /// <param name="request">The originating request.</param>
    /// <param name="response">The response whose body is buffered.</param>
    /// <param name="key">The cache key for the request.</param>
    /// <param name="cacheResult">The callback that persists the buffered response.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A response backed by the buffered content.</returns>
    private static async Task<HttpResponseMessage> CopyAndCacheResponseAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        string key,
        Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task> cacheResult,
        CancellationToken cancellationToken)
    {
        var ms = new MemoryStream();
#if NET5_0_OR_GREATER
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        await stream.CopyToAsync(ms, CopyBufferSize, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var newResp = new HttpResponseMessage();
        foreach (var kvp in response.Headers)
        {
            newResp.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        var newContent = new ByteArrayContent(ms.ToArray());
        foreach (var kvp in response.Content.Headers)
        {
            newContent.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        newResp.Content = newContent;

        await cacheResult(request, newResp, key, cancellationToken).ConfigureAwait(false);

        return newResp;
    }

    /// <summary>Completes a shared in-flight response from the queued operation.</summary>
    /// <param name="queue">The queue that schedules the request.</param>
    /// <param name="inflightRequest">The shared in-flight request state.</param>
    /// <param name="request">The originating request.</param>
    /// <param name="key">The cache key for the request.</param>
    /// <param name="realToken">The cancellation source that owns the underlying request.</param>
    /// <param name="cacheResult">The optional callback that persists the response.</param>
    /// <returns>A task representing the completion publication.</returns>
    private async Task CompleteInflightRequestAsync(
        OperationQueue queue,
        InflightRequest inflightRequest,
        HttpRequestMessage request,
        string key,
        CancellationTokenSource realToken,
        Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResult)
    {
        try
        {
            var response = await queue.Enqueue(
                    _priority,
                    null!,
                    () => FetchAndCacheAsync(request, key, cacheResult, realToken.Token),
                    realToken.Token)
                .ConfigureAwait(false);

            inflightRequest.TrySetResult(response);
        }
        catch (OperationCanceledException) when (realToken.IsCancellationRequested)
        {
            inflightRequest.TrySetCanceled(realToken.Token);
        }
        catch (Exception ex)
        {
            inflightRequest.TrySetException(ex);
        }
        finally
        {
            lock (_inflightResponses)
            {
                if (_inflightResponses.TryGetValue(key, out var currentRequest) &&
                    ReferenceEquals(currentRequest, inflightRequest))
                {
                    _inflightResponses.Remove(key);
                }
            }

            realToken.Dispose();
        }
    }

    /// <summary>Sends the request through the inner handler and optionally caches the result.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="key">The cache key for the request.</param>
    /// <param name="cacheResult">The optional callback that persists the response.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The response from the inner handler.</returns>
    private async Task<HttpResponseMessage> FetchAndCacheAsync(
        HttpRequestMessage request,
        string key,
        Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResult,
        CancellationToken cancellationToken)
    {
        var resp = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (_maxBytesToRead is not null && resp.Content?.Headers.ContentLength is not null)
        {
            _maxBytesToRead -= resp.Content.Headers.ContentLength;
        }

        if (cacheResult is not null && resp.Content is not null)
        {
            resp = await CopyAndCacheResponseAsync(request, resp, key, cacheResult, cancellationToken).ConfigureAwait(false);
        }

        return resp;
    }
}
