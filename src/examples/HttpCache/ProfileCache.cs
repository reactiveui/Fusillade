// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;

namespace HttpCacheExamples;

/// <summary>Keeps a snapshot of each response body for offline reads.</summary>
internal sealed class ProfileCache : IRequestCache
{
    /// <summary>Stores bodies by the key supplied by Fusillade.</summary>
    private readonly ConcurrentDictionary<string, byte[]> _bodies = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task SaveAsync(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct) =>
        _bodies[key] = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<byte[]?> FetchAsync(HttpRequestMessage request, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_bodies.TryGetValue(key, out var body) ? body : null);
    }
}
