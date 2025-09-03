// Copyright (c) 2025 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Fusillade;

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
        NetCache.CreateDefaultInstances(builder.Current);
        return builder;
    }
}
