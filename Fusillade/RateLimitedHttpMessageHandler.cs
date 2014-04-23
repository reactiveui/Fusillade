using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Punchclock;

namespace Fusillade
{
    public class RateLimitedHttpMessageHandler : SpeculativeHttpScheduler
    {
        readonly int priority;
        long? maxBytesToRead = null;

        public RateLimitedHttpMessageHandler(HttpMessageHandler handler, Priority basePriority, int priority = 0, long? maxBytesToRead = null) : base(handler)
        {
            this.priority = (int)basePriority + priority;
            this.maxBytesToRead = maxBytesToRead;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (maxBytesToRead != null && maxBytesToRead.Value < 0) {
                var ct = new CancellationTokenSource();
                ct.Cancel();
                cancellationToken = ct.Token;
            }

            return NetCache.OperationQueue.Enqueue(priority, null, cancellationToken, async () => {
                var ret = await base.SendAsync(request, cancellationToken);

                if (maxBytesToRead != null && ret.Content != null && ret.Content.Headers.ContentLength != null) {
                    maxBytesToRead -= ret.Content.Headers.ContentLength;
                }

                return ret;
            });
        }

        public override void ResetLimit(long? maxBytesToRead = null)
        {
            this.maxBytesToRead = maxBytesToRead;
        }
    }
}