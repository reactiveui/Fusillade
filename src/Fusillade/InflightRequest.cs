// Copyright (c) 2025 ReactiveUI and Contributors. All rights reserved.
// Licensed to ReactiveUI and Contributors under one or more agreements.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Threading;

namespace Fusillade;

internal class InflightRequest(Action onFullyCancelled)
{
    private int _refCount = 1;

    public AsyncSubject<HttpResponseMessage> Response { get; } = new();

    public void AddRef() => Interlocked.Increment(ref _refCount);

    public void Cancel()
    {
        if (Interlocked.Decrement(ref _refCount) <= 0)
        {
            onFullyCancelled();
        }
    }
}
