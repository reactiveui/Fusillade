// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NUnit.Framework; // switched from xUnit

namespace Fusillade.Tests
{
    /// <summary>
    /// Checks to make sure that the NetCache operates correctly.
    /// </summary>
    [TestFixture]
    [NonParallelizable] // replaces CollectionDefinition DisableParallelization
    public class NetCacheTests
    {
        /// <summary>
        /// Verifies that we are registering the default handlers correctly.
        /// </summary>
        [Test]
        public void DefaultValuesShouldBeRegistered()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(NetCache.Speculative, Is.Not.Null);
                Assert.That(NetCache.UserInitiated, Is.Not.Null);
                Assert.That(NetCache.Background, Is.Not.Null);
                Assert.That(NetCache.Offline, Is.Not.Null);
                Assert.That(NetCache.OperationQueue, Is.Not.Null);
                Assert.That(NetCache.RequestCache, Is.Null);
            }
        }
    }
}
