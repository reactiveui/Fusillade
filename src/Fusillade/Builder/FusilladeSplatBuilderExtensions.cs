// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Fusillade;
using Fusillade.Helpers;

namespace Splat.Builder;

/// <summary>Splat module for configuring Fusillade.</summary>
[SuppressMessage("Roslynator", "RCS1263:Invalid reference in documentation comment", Justification = "Roslynator does not yet understand C# extension block receiver documentation.")]
public static class FusilladeSplatBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IAppInstance"/>.</summary>
    /// <param name="builder">The builder.</param>
    extension(IAppInstance builder)
    {
        /// <summary>Creates the fusillade net cache.</summary>
        /// <returns>The App Instance for Chaining.</returns>
        public IAppInstance CreateFusilladeNetCache()
        {
            ArgumentExceptionHelper.ThrowIfNull(builder);

            NetCache.CreateDefaultInstances(builder.Current);
            return builder;
        }
    }
}
