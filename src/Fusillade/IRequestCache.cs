// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>This Interface is a simple cache for HTTP requests and intentionally does not conform to HTTP caching rules.</summary>
public interface IRequestCache
{
    /// <summary>
    /// Implement this method by saving the Body of the response. The
    /// response is already downloaded as a ByteArrayContent so you don't
    /// have to worry about consuming the stream.
    /// </summary>
    /// <param name="request">The originating request.</param>
    /// <param name="response">The response whose body you should save.</param>
    /// <param name="key">A unique key used to identify the request details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completion.</returns>
    Task SaveAsync(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct);

    /// <summary>Implement this by loading the Body of the given request / key.</summary>
    /// <param name="request">The originating request.</param>
    /// <param name="key">A unique key used to identify the request details,
    /// that was given in SaveAsync().</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Body of the given request, or null if the search
    /// completed successfully but the response was not found.</returns>
    Task<byte[]?> FetchAsync(HttpRequestMessage request, string key, CancellationToken ct);
}
