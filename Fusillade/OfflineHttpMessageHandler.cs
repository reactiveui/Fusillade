using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade
{
    public class OfflineHttpMessageHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, string, CancellationToken, Task<byte[]>> retrieveBody;

        public OfflineHttpMessageHandler(Func<HttpRequestMessage, string, CancellationToken, Task<byte[]>> retrieveBodyFunc)
        {
            retrieveBody = retrieveBodyFunc;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await this.retrieveBody(request, RateLimitedHttpMessageHandler.UniqueKeyForRequest(request), cancellationToken);
            if (body == null) {
                return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
            }

            var byteContent = new ByteArrayContent(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = byteContent };
        }
    }
}
