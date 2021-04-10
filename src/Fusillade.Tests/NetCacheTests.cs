// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Xunit;

namespace Fusillade.Tests
{
    /// <summary>
    /// Checks to make sure that the NetCache operates correctly.
    /// </summary>
    [CollectionDefinition(nameof(NetCacheTests), DisableParallelization = true)]
    public class NetCacheTests
    {
        /// <summary>
        /// Verifies that we are registering the default handlers correctly.
        /// </summary>
        [Fact]
        public void DefaultValuesShouldBeRegistered()
        {
            Assert.NotNull(NetCache.Speculative);
            Assert.NotNull(NetCache.UserInitiated);
            Assert.NotNull(NetCache.Background);
            Assert.NotNull(NetCache.Offline);
            Assert.NotNull(NetCache.OperationQueue);
            Assert.Null(NetCache.RequestCache);
        }
    }
}
