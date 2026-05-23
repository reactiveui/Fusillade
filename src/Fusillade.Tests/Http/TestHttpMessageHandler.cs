// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace Fusillade.Tests;

/// <summary>
/// Tests the main http scheduler.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TestHttpMessageHandler"/> class.
/// </remarks>
/// <param name="createResult">Creates a http response.</param>
public class TestHttpMessageHandler(Func<HttpRequestMessage, IObservable<HttpResponseMessage>> createResult) : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Observable.Throw<HttpResponseMessage>(new OperationCanceledException()).ToTask();
        }

        return createResult(request).ToTask(cancellationToken);
    }
}
