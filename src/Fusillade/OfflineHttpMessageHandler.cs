// Copyright (c) 2020 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade
{
    /// <summary>
    /// A http handler that will make a response even if the HttpClient is offline.
    /// </summary>
    public class OfflineHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, CancellationToken, Task<byte[]>>? _retrieveBody;

        /// <summary>
        /// Initializes a new instance of the <see cref="OfflineHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="retrieveBodyFunc">A function that will retrieve a body.</param>
        public OfflineHttpMessageHandler(Func<HttpRequestMessage, string, CancellationToken, Task<byte[]>>? retrieveBodyFunc)
        {
            _retrieveBody = retrieveBodyFunc;
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var retrieveBody = _retrieveBody;
            if (retrieveBody == null && NetCache.RequestCache != null)
            {
                retrieveBody = NetCache.RequestCache.Fetch;
            }

            if (retrieveBody == null)
            {
                throw new Exception("Configure NetCache.RequestCache before calling this!");
            }

            var body = await retrieveBody(request, RateLimitedHttpMessageHandler.UniqueKeyForRequest(request), cancellationToken).ConfigureAwait(false);
            if (body == null)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var byteContent = new ByteArrayContent(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = byteContent };
        }
    }
}
