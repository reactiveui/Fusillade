// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade;

/// <summary>
/// A http handler that will make a response even if the HttpClient is offline.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="OfflineHttpMessageHandler"/> class.
/// </remarks>
/// <param name="retrieveBodyFunc">A function that will retrieve a body.</param>
public class OfflineHttpMessageHandler(Func<HttpRequestMessage, string, CancellationToken, Task<byte[]?>>? retrieveBodyFunc) : HttpMessageHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var retrieveBody = retrieveBodyFunc;
        if (retrieveBody == null && NetCache.RequestCache != null)
        {
            retrieveBody = NetCache.RequestCache.Fetch;
        }

        if (retrieveBody == null)
        {
            throw new InvalidOperationException("Configure NetCache.RequestCache before calling this!");
        }

        var body = await retrieveBody(request, RateLimitedHttpMessageHandler.UniqueKeyForRequest(request), cancellationToken).ConfigureAwait(false);
        if (body == null)
        {
            return new(HttpStatusCode.ServiceUnavailable);
        }

        var byteContent = new ByteArrayContent(body);
        return new(HttpStatusCode.OK) { Content = byteContent };
    }
}
