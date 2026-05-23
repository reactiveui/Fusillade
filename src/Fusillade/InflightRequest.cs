// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Reactive.Subjects;
using System.Threading;

namespace Fusillade;

/// <summary>
/// Tracks a single in-flight request that may be shared by several callers,
/// cancelling the underlying work only once every caller has cancelled.
/// </summary>
/// <param name="onFullyCancelled">Invoked when the last caller cancels.</param>
internal sealed class InflightRequest(Action onFullyCancelled)
{
    /// <summary>The number of callers currently sharing this request.</summary>
    private int _refCount = 1;

    /// <summary>Gets the subject that yields the shared response.</summary>
    public AsyncSubject<HttpResponseMessage> Response { get; } = new();

    /// <summary>Gets the number of callers currently sharing this request.</summary>
    public int ReferenceCount => Volatile.Read(ref _refCount);

    /// <summary>Adds a caller reference to this request.</summary>
    public void AddRef() => Interlocked.Increment(ref _refCount);

    /// <summary>
    /// Removes a caller reference, fully cancelling once none remain.
    /// </summary>
    public void Cancel()
    {
        if (Interlocked.Decrement(ref _refCount) > 0)
        {
            return;
        }

        onFullyCancelled();
    }
}
