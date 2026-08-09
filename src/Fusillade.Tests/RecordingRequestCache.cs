// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Request cache implementation that records calls for tests.</summary>
internal sealed class RecordingRequestCache : IRequestCache
{
    /// <summary>The bytes returned by <see cref="IRequestCache.FetchAsync(HttpRequestMessage, string, CancellationToken)"/>.</summary>
    private byte[]? _fetchedBytes;

    /// <summary>Gets the number of save calls.</summary>
    internal int SaveCount { get; private set; }

    /// <summary>Gets the key passed to the last save call.</summary>
    internal string? SavedKey { get; private set; }

    /// <summary>Gets the bytes passed to the last save call.</summary>
    internal byte[]? SavedBytes { get; private set; }

    /// <summary>Sets the bytes that <see cref="IRequestCache.FetchAsync(HttpRequestMessage, string, CancellationToken)"/> returns.</summary>
    /// <param name="fetchedBytes">The bytes to return, or <see langword="null"/> for a cache miss.</param>
    internal void SetFetchedBytes(byte[]? fetchedBytes) => _fetchedBytes = fetchedBytes;

    /// <inheritdoc />
    async Task IRequestCache.SaveAsync(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct)
    {
        _ = request;
        SaveCount++;
        SavedKey = key;
        SavedBytes = await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc />
    Task<byte[]?> IRequestCache.FetchAsync(HttpRequestMessage request, string key, CancellationToken ct)
    {
        _ = request;
        _ = key;
        _ = ct;
        return Task.FromResult(_fetchedBytes);
    }
}
