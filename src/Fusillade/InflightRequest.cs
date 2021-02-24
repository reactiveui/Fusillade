// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Threading;

namespace Fusillade
{
    internal class InflightRequest
    {
        private int _refCount = 1;
        private Action _onCancelled;

        public InflightRequest(Action onFullyCancelled)
        {
            _onCancelled = onFullyCancelled;
            Response = new AsyncSubject<HttpResponseMessage>();
        }

        public AsyncSubject<HttpResponseMessage> Response { get; protected set; }

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public void Cancel()
        {
            if (Interlocked.Decrement(ref _refCount) <= 0)
            {
                _onCancelled();
            }
        }
    }
}
