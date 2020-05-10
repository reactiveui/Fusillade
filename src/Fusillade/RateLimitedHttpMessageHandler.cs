// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
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

namespace Fusillade
{
    /// <summary>
    /// A http handler which will limit the rate at which we can read.
    /// </summary>
    public class RateLimitedHttpMessageHandler : LimitingHttpMessageHandler
    {
        private readonly int _priority;
        private readonly OperationQueue? _opQueue;
        private readonly Dictionary<string, InflightRequest> _inflightResponses =
            new Dictionary<string, InflightRequest>();

        private readonly Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? _cacheResult;

        private long? _maxBytesToRead;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateLimitedHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="handler">The handler we are wrapping.</param>
        /// <param name="basePriority">The base priority of the request.</param>
        /// <param name="priority">The priority of the request.</param>
        /// <param name="maxBytesToRead">The maximum number of bytes we can reead.</param>
        /// <param name="opQueue">The operation queue on which to run the operation.</param>
        /// <param name="cacheResultFunc">A method that is called if we need to get cached results.</param>
        public RateLimitedHttpMessageHandler(HttpMessageHandler handler, Priority basePriority, int priority = 0, long? maxBytesToRead = null, OperationQueue? opQueue = null, Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResultFunc = null)
            : base(handler)
        {
            _priority = (int)basePriority + priority;
            _maxBytesToRead = maxBytesToRead;
            _opQueue = opQueue;
            _cacheResult = cacheResultFunc;
        }

        /// <summary>
        /// Generates a unique key for for a <see cref="HttpRequestMessage"/>.
        /// This assists with the caching.
        /// </summary>
        /// <param name="request">The request to generate a unique key for.</param>
        /// <returns>The unique key.</returns>
        public static string UniqueKeyForRequest(HttpRequestMessage request)
        {
            var ret = new[]
            {
                request.RequestUri.ToString(),
                request.Method.Method,
                request.Headers.Accept.ConcatenateAll(x => x.CharSet + x.MediaType),
                request.Headers.AcceptEncoding.ConcatenateAll(x => x.Value),
                (request.Headers.Referrer ?? new Uri("http://example")).AbsoluteUri,
                request.Headers.UserAgent.ConcatenateAll(x => (x.Product != null ? x.Product.ToString() : x.Comment)),
            }.Aggregate(
                new StringBuilder(),
                (acc, x) =>
                {
                    acc.AppendLine(x);
                    return acc;
                });

            if (request.Headers.Authorization != null)
            {
                ret.AppendLine(request.Headers.Authorization.Parameter + request.Headers.Authorization.Scheme);
            }

            return "HttpSchedulerCache_" + ret.ToString().GetHashCode().ToString("x", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override void ResetLimit(long? maxBytesToRead = null)
        {
            _maxBytesToRead = maxBytesToRead;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.Method;
            if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options)
            {
                return base.SendAsync(request, cancellationToken);
            }

            var cacheResult = _cacheResult;
            if (cacheResult == null && NetCache.RequestCache != null)
            {
                cacheResult = NetCache.RequestCache.Save;
            }

            if (_maxBytesToRead != null && _maxBytesToRead.Value < 0)
            {
                var tcs = new TaskCompletionSource<HttpResponseMessage>();
                tcs.SetCanceled();
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
                if (_inflightResponses.ContainsKey(key))
                {
                    var val = _inflightResponses[key];
                    val.AddRef();
                    cancellationToken.Register(val.Cancel);

                    return val.Response.ToTask(cancellationToken);
                }

                _inflightResponses[key] = ret;
            }

            cancellationToken.Register(ret.Cancel);

            var queue = _opQueue ?? NetCache.OperationQueue;

            queue.Enqueue(
                _priority,
                null!,
                async () =>
                {
                    try
                    {
                        var resp = await base.SendAsync(request, realToken.Token).ConfigureAwait(false);

                        if (_maxBytesToRead != null && resp.Content != null && resp.Content.Headers.ContentLength != null)
                        {
                            _maxBytesToRead -= resp.Content.Headers.ContentLength;
                        }

                        if (cacheResult != null && resp.Content != null)
                        {
                            var ms = new MemoryStream();
                            var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            await stream.CopyToAsync(ms, 32 * 1024, realToken.Token).ConfigureAwait(false);

                            realToken.Token.ThrowIfCancellationRequested();

                            var newResp = new HttpResponseMessage();
                            foreach (var kvp in resp.Headers)
                            {
                                newResp.Headers.Add(kvp.Key, kvp.Value);
                            }

                            var newContent = new ByteArrayContent(ms.ToArray());
                            foreach (var kvp in resp.Content.Headers)
                            {
                                newContent.Headers.Add(kvp.Key, kvp.Value);
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
}
