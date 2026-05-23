// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Fusillade;
using Fusillade.Helpers;

namespace Splat.Builder;

/// <summary>
/// Splat module for configuring Fusillade.
/// </summary>
public static class FusilladeSplatBuilderExtensions
{
    /// <summary>
    /// Creates the fusillade net cache.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The App Instance for Chaining.</returns>
    public static IAppInstance CreateFusilladeNetCache(this IAppInstance builder)
    {
        ArgumentExceptionHelper.ThrowIfNull(builder);

        NetCache.CreateDefaultInstances(builder.Current);
        return builder;
    }
}
