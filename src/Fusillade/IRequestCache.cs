// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Fusillade
{
    /// <summary>
    /// This Interface is a simple cache for HTTP requests - it is intentionally
    /// *not* designed to conform to HTTP caching rules since you most likely want
    /// to override those rules in a client app anyways.
    /// </summary>
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
        Task Save(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct);

        /// <summary>
        /// Implement this by loading the Body of the given request / key.
        /// </summary>
        /// <param name="request">The originating request.</param>
        /// <param name="key">A unique key used to identify the request details,
        /// that was given in Save().</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The Body of the given request, or null if the search
        /// completed successfully but the response was not found.</returns>
        Task<byte[]> Fetch(HttpRequestMessage request, string key, CancellationToken ct);
    }
}
