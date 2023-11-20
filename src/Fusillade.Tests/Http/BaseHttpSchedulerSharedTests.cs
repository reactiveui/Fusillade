// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net.Http;
using Punchclock;

namespace Fusillade.Tests
{
    /// <summary>
    /// Checks to make sure the base http scheduler works.
    /// </summary>
    public class BaseHttpSchedulerSharedTests : HttpSchedulerSharedTests
    {
        /// <inheritdoc/>
        protected override LimitingHttpMessageHandler CreateFixture(HttpMessageHandler innerHandler) =>
            new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, opQueue: new OperationQueue(4));
    }
}
