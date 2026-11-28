// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace HttpCacheExamples;

/// <summary>Reports the configured prefetch budget while an inner Fusillade handler enforces it.</summary>
internal sealed class PrefetchBudgetHandler : LimitingHttpMessageHandler
{
    /// <summary>Initializes a new instance of the <see cref="PrefetchBudgetHandler"/> class.</summary>
    internal PrefetchBudgetHandler()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrefetchBudgetHandler"/> class.</summary>
    /// <param name="limiter">The handler that counts transferred bytes.</param>
    internal PrefetchBudgetHandler(LimitingHttpMessageHandler limiter)
        : base(limiter)
    {
    }

    /// <summary>Gets the budget most recently set by the owner.</summary>
    internal long? ConfiguredBudget { get; private set; }

    /// <inheritdoc/>
    public override void ResetLimit(long? maxBytesToRead)
    {
        if (InnerHandler is not LimitingHttpMessageHandler limiter)
        {
            throw new InvalidOperationException("Configure a limiting inner handler before setting the prefetch budget.");
        }

        limiter.ResetLimit(maxBytesToRead);
        ConfiguredBudget = maxBytesToRead;
    }
}
