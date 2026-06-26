// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Request cache implementation that records calls for tests.</summary>
internal sealed class RecordingRequestCache : IRequestCache
{
    /// <summary>Gets or sets the bytes returned by <see cref="FetchAsync"/>.</summary>
    public byte[]? FetchedBytes { get; set; }

    /// <summary>Gets the number of save calls.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Gets the key passed to the last save call.</summary>
    public string? SavedKey { get; private set; }

    /// <summary>Gets the bytes passed to the last save call.</summary>
    public byte[]? SavedBytes { get; private set; }

    /// <inheritdoc />
    public async Task SaveAsync(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct)
    {
        _ = request;
        SaveCount++;
        SavedKey = key;
        SavedBytes = await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <inheritdoc />
    public Task<byte[]?> FetchAsync(HttpRequestMessage request, string key, CancellationToken ct)
    {
        _ = request;
        _ = key;
        _ = ct;
        return Task.FromResult(FetchedBytes);
    }
}
