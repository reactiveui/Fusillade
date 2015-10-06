using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Punchclock;

namespace Fusillade
{
    class InflightRequest
    {
        int refCount = 1;
        Action onCancelled;

        public AsyncSubject<HttpResponseMessage> Response { get; protected set; }

        public InflightRequest(Action onFullyCancelled)
        {
            onCancelled = onFullyCancelled;
            Response = new AsyncSubject<HttpResponseMessage>();
        }

        public void AddRef()
        {
            Interlocked.Increment(ref refCount);
        }

        public void Cancel()
        {
            if (Interlocked.Decrement(ref refCount) <= 0) {
                onCancelled();
            }
        }
    }

    public class RateLimitedHttpMessageHandler : LimitingHttpMessageHandler
    {
        readonly int priority;
        readonly OperationQueue opQueue;
        readonly Dictionary<string, InflightRequest> inflightResponses = 
            new Dictionary<string, InflightRequest>();

        readonly Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task> cacheResult;

        long? maxBytesToRead = null;

        public RateLimitedHttpMessageHandler(HttpMessageHandler handler, Priority basePriority, int priority = 0, long? maxBytesToRead = null, OperationQueue opQueue = null, Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task> cacheResultFunc = null) : base(handler)
        {
            this.priority = (int)basePriority + priority;
            this.maxBytesToRead = maxBytesToRead;
            this.opQueue = opQueue;
            this.cacheResult = cacheResultFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cacheResult = this.cacheResult;
            if (cacheResult == null && NetCache.RequestCache != null) {
                cacheResult = NetCache.RequestCache.Save;
            }

            if (maxBytesToRead != null && maxBytesToRead.Value < 0) {
                var tcs = new TaskCompletionSource<HttpResponseMessage>();
                tcs.SetCanceled();
                return tcs.Task;
            }

            var key = UniqueKeyForRequest(request);
            var realToken = new CancellationTokenSource();
            var ret = new InflightRequest(() => { 
                lock (inflightResponses) inflightResponses.Remove(key);
                realToken.Cancel();
            });

            lock (inflightResponses) {
                if (inflightResponses.ContainsKey(key)) {
                    var val = inflightResponses[key];
                    val.AddRef();
                    cancellationToken.Register(val.Cancel);

                    return val.Response.ToTask(cancellationToken);
                }

                inflightResponses[key] = ret;
            }

            cancellationToken.Register(ret.Cancel);

            var queue = this.opQueue ?? NetCache.OperationQueue;

            queue.Enqueue(priority, null, realToken.Token, async () => {
                try {
                    var resp = await base.SendAsync(request, realToken.Token);

                    if (maxBytesToRead != null && resp.Content != null && resp.Content.Headers.ContentLength != null) {
                        maxBytesToRead -= resp.Content.Headers.ContentLength;
                    }

                    if (cacheResult != null && resp.Content != null) {
                        var ms = new MemoryStream();
                        var stream = await resp.Content.ReadAsStreamAsync();
                        await stream.CopyToAsync(ms, 32 * 1024, realToken.Token);

                        realToken.Token.ThrowIfCancellationRequested();

                        var newResp = new HttpResponseMessage();
                        foreach (var kvp in resp.Headers) { newResp.Headers.Add(kvp.Key, kvp.Value); }

                        var newContent = new ByteArrayContent(ms.ToArray());
                        foreach (var kvp in resp.Content.Headers) { newContent.Headers.Add(kvp.Key, kvp.Value); }
                        newResp.Content = newContent;

                        resp = newResp;
                        await cacheResult(request, resp, key, realToken.Token);
                    }

                    return resp;
                } finally {
                    lock(inflightResponses) inflightResponses.Remove(key);
                }
            }).ToObservable().Subscribe(ret.Response);

            return ret.Response.ToTask(cancellationToken);
        }

        public override void ResetLimit(long? maxBytesToRead = null)
        {
            this.maxBytesToRead = maxBytesToRead;
        }

        public static string UniqueKeyForRequest(HttpRequestMessage request)
        {
            var ret = new[] {
                request.RequestUri.ToString(),
                request.Method.Method,
                request.Headers.Accept.ConcatenateAll(x => x.CharSet + x.MediaType),
                request.Headers.AcceptEncoding.ConcatenateAll(x => x.Value),
                (request.Headers.Referrer ?? new Uri("http://example")).AbsoluteUri,
                request.Headers.UserAgent.ConcatenateAll(x => (x.Product != null ? x.Product.ToString() : x.Comment)),
            }.Aggregate(new StringBuilder(), (acc, x) => { acc.AppendLine(x); return acc; });

            if (request.Headers.Authorization != null) {
                ret.AppendLine(request.Headers.Authorization.Parameter + request.Headers.Authorization.Scheme);
            }

            return "HttpSchedulerCache_" + ret.ToString().GetHashCode().ToString("x", CultureInfo.InvariantCulture);
        }
    }

    internal static class ConcatenateMixin
    {
        public static string ConcatenateAll<T>(this IEnumerable<T> This, Func<T, string> selector, char separator = '|')
        {
            return This.Aggregate(new StringBuilder(), (acc, x) => 
            {
                acc.Append(selector(x));
                acc.Append(separator);
                return acc;
            }).ToString();
        }
    }
}
