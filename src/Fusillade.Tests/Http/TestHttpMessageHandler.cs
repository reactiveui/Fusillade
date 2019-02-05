// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.Reactive.Threading.Tasks;
using System.Reactive.Linq;

namespace Fusillade.Tests
{
    public class TestHttpMessageHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, IObservable<HttpResponseMessage>> _block;

        public TestHttpMessageHandler(Func<HttpRequestMessage, IObservable<HttpResponseMessage>> createResult)
        {
            _block = createResult;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Observable.Throw<HttpResponseMessage>(new OperationCanceledException()).ToTask();
            }

            return _block(request).ToTask(cancellationToken);
        }
    }
}