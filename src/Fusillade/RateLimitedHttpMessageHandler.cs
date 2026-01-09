// Copyright (c) 2025 ReactiveUI and Contributors. All rights reserved.
// Licensed to ReactiveUI and Contributors under one or more agreements.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Punchclock;

namespace Fusillade;

/// <summary>
/// A http handler which will limit the rate at which we can read.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RateLimitedHttpMessageHandler"/> class.
/// </remarks>
/// <param name="handler">The handler we are wrapping.</param>
/// <param name="basePriority">The base priority of the request.</param>
/// <param name="priority">The priority of the request.</param>
/// <param name="maxBytesToRead">The maximum number of bytes we can read.</param>
/// <param name="opQueue">The operation queue on which to run the operation.</param>
/// <param name="cacheResultFunc">A method that is called if we need to get cached results.</param>
public class RateLimitedHttpMessageHandler(HttpMessageHandler? handler, Priority basePriority, int priority = 0, long? maxBytesToRead = null, OperationQueue? opQueue = null, Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResultFunc = null) : LimitingHttpMessageHandler(handler)
{
    private readonly int _priority = (int)basePriority + priority;
    private readonly Dictionary<string, InflightRequest> _inflightResponses = [];
    private long? _maxBytesToRead = maxBytesToRead;

    /// <summary>
    /// Generates a unique key for a <see cref="HttpRequestMessage"/>.
    /// This assists with the caching.
    /// </summary>
    /// <param name="request">The request to generate a unique key for.</param>
    /// <returns>The unique key.</returns>
    public static string UniqueKeyForRequest(HttpRequestMessage request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var ret = new[]
        {
            request.RequestUri!.ToString(),
            request.Method.Method,
            request.Headers.Accept.ConcatenateAll(x => x.CharSet + x.MediaType),
            request.Headers.AcceptEncoding.ConcatenateAll(x => x.Value),
            (request.Headers.Referrer ?? new Uri("http://example")).AbsoluteUri,
            request.Headers.UserAgent.ConcatenateAll(x => x.Product != null ? x.Product.ToString() : x.Comment!),
        }.Aggregate(
            new StringBuilder(),
            (acc, x) =>
            {
                acc.AppendLine(x);
                return acc;
            });

        if (request.Headers.Authorization != null)
        {
            ret.Append(request.Headers.Authorization.Parameter).AppendLine(request.Headers.Authorization.Scheme);
        }

        return "HttpSchedulerCache_" + ret.ToString().GetHashCode().ToString("x", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override void ResetLimit(long? maxBytesToRead = null) => _maxBytesToRead = maxBytesToRead;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var method = request.Method;
        if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options)
        {
            return base.SendAsync(request, cancellationToken);
        }

        var cacheResult = cacheResultFunc;
        if (cacheResult == null && NetCache.RequestCache != null)
        {
            cacheResult = NetCache.RequestCache.Save;
        }

        if (_maxBytesToRead < 0)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>();
#if NET5_0_OR_GREATER
            tcs.SetCanceled(cancellationToken);
#else
            tcs.SetCanceled();
#endif
            return tcs.Task;
        }

        var key = UniqueKeyForRequest(request);
        var realToken = new CancellationTokenSource();
        var ret = new InflightRequest(() =>
        {
            lock (_inflightResponses)
            {
                _inflightResponses.Remove(key);
            }

            realToken.Cancel();
        });

        lock (_inflightResponses)
        {
            if (_inflightResponses.TryGetValue(key, out var existingRequest))
            {
                existingRequest.AddRef();
                cancellationToken.Register(existingRequest.Cancel);

                return existingRequest.Response.ToTask(cancellationToken);
            }

            _inflightResponses[key] = ret;
        }

        cancellationToken.Register(ret.Cancel);

        var queue = opQueue ?? NetCache.OperationQueue;

        queue.Enqueue(
            _priority,
            null!,
            async () =>
            {
                try
                {
                    var resp = await base.SendAsync(request, realToken.Token).ConfigureAwait(false);

                    if (_maxBytesToRead != null && resp.Content?.Headers.ContentLength != null)
                    {
                        _maxBytesToRead -= resp.Content.Headers.ContentLength;
                    }

                    if (cacheResult != null && resp.Content != null)
                    {
                        var ms = new MemoryStream();
#if NET5_0_OR_GREATER
                        var stream = await resp.Content.ReadAsStreamAsync(realToken.Token).ConfigureAwait(false);
#else
                        var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                        await stream.CopyToAsync(ms, 32 * 1024, realToken.Token).ConfigureAwait(false);

                        realToken.Token.ThrowIfCancellationRequested();

                        var newResp = new HttpResponseMessage();
                        foreach (var kvp in resp.Headers)
                        {
                            newResp.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                        }

                        var newContent = new ByteArrayContent(ms.ToArray());
                        foreach (var kvp in resp.Content.Headers)
                        {
                            newContent.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                        }

                        newResp.Content = newContent;

                        resp = newResp;
                        await cacheResult(request, resp, key, realToken.Token).ConfigureAwait(false);
                    }

                    return resp;
                }
                finally
                {
                    lock (_inflightResponses)
                    {
                        _inflightResponses.Remove(key);
                    }
                }
            },
            realToken.Token).ToObservable().Subscribe(ret.Response);

        return ret.Response.ToTask(cancellationToken);
    }
}
