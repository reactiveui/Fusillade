// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Splat;
using Splat.Builder;

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks Fusillade's Splat builder integration.</summary>
[NotInParallel]
public class SplatBuilderTests
{
    /// <summary>Verifies that CreateFusilladeNetCache wires NetCache to the builder's resolver.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task CreateFusilladeNetCacheShouldUseBuilderResolverAsync()
    {
        using var scope = new NetCacheTestScope();
        var resolver = new ModernDependencyResolver();
        IAppInstance app = new AppBuilder(resolver).Build();

        var result = app.CreateFusilladeNetCache();

        await Assert.That(ReferenceEquals(result, app)).IsTrue();
    }

    /// <summary>Verifies that the builder extension rejects a null builder.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task CreateFusilladeNetCacheShouldRejectNullBuilderAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            _ = ((IAppInstance)null!).CreateFusilladeNetCache();
            return Task.CompletedTask;
        });
    }
}
