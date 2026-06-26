// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks to make sure the base http scheduler works.</summary>
[InheritsTests]
public class BaseHttpSchedulerSharedTests : HttpSchedulerSharedTests
{
    /// <inheritdoc/>
    protected override LimitingHttpMessageHandler CreateFixture(HttpMessageHandler? innerHandler) =>
        new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, operationQueue: new());
}
