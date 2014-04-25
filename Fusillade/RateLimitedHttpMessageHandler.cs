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

        long? maxBytesToRead = null;

        public RateLimitedHttpMessageHandler(Priority basePriority, int priority = 0, long? maxBytesToRead = null, OperationQueue opQueue = null) : base()
        {
            this.priority = (int)basePriority + priority;
            this.maxBytesToRead = maxBytesToRead;
            this.opQueue = opQueue;
        }

        public RateLimitedHttpMessageHandler(HttpMessageHandler handler, Priority basePriority, int priority = 0, long? maxBytesToRead = null, OperationQueue opQueue = null) : base(handler)
        {
            this.priority = (int)basePriority + priority;
            this.maxBytesToRead = maxBytesToRead;
            this.opQueue = opQueue;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (maxBytesToRead != null && maxBytesToRead.Value < 0) {
                var tcs = new TaskCompletionSource<HttpResponseMessage>();
                tcs.SetCanceled();
                return tcs.Task;
            }

            var key = uniqueKeyForRequest(request);
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
                var resp = await base.SendAsync(request, realToken.Token);

                if (maxBytesToRead != null && resp.Content != null && resp.Content.Headers.ContentLength != null) {
                    maxBytesToRead -= resp.Content.Headers.ContentLength;
                }

                lock(inflightResponses) inflightResponses.Remove(key);
                return resp;
            }).ToObservable().Subscribe(ret.Response);

            return ret.Response.ToTask(cancellationToken);
        }

        public override void ResetLimit(long? maxBytesToRead = null)
        {
            this.maxBytesToRead = maxBytesToRead;
        }

        static string uniqueKeyForRequest(HttpRequestMessage request)
        {
            var ret = new[] {
                request.RequestUri.ToString(),
                request.Method.Method,
                request.Headers.Accept.ConcatenateAll(x => x.CharSet + x.MediaType),
                request.Headers.AcceptEncoding.ConcatenateAll(x => x.Value),
                (request.Headers.Referrer ?? new Uri("http://example")).AbsoluteUri,
                request.Headers.UserAgent.ConcatenateAll(x => x.Product.ToString()),
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