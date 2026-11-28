// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;

namespace HttpCacheExamples;

/// <summary>Answers locally so the sample can count requests reaching the transport.</summary>
internal sealed class ProfileTransport : HttpMessageHandler
{
    /// <summary>Counts sends across concurrent callers.</summary>
    private int _sends;

    /// <summary>Gets the number of requests that reached the service.</summary>
    internal int Sends => Volatile.Read(ref _sends);

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Interlocked.Increment(ref _sends);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ada") });
    }
}
