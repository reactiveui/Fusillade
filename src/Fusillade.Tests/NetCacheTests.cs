// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace Fusillade.Tests;

/// <summary>
/// Checks to make sure that the NetCache operates correctly.
/// </summary>
[NotInParallel]
public class NetCacheTests
{
    /// <summary>
    /// Verifies that we are registering the default handlers correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task DefaultValuesShouldBeRegistered()
    {
        using (Assert.Multiple())
        {
            await Assert.That(NetCache.Speculative).IsNotNull();
            await Assert.That(NetCache.UserInitiated).IsNotNull();
            await Assert.That(NetCache.Background).IsNotNull();
            await Assert.That(NetCache.Offline).IsNotNull();
            await Assert.That(NetCache.OperationQueue).IsNotNull();
            await Assert.That(NetCache.RequestCache).IsNull();
        }
    }
}
