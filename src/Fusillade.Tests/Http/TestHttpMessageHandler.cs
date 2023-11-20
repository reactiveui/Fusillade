// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade.Tests
{
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
}
