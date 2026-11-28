// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Runs the documented cache and prefetch scenario with process-wide handlers.</summary>
[NotInParallel]
public class HttpCacheExampleTests
{
    /// <summary>Checks that both callers share a download, that it can be read offline and that a prefetch budget can be reset.</summary>
    /// <returns>Completion after all example assertions pass.</returns>
    [Test]
    public async Task DownloadShareCacheAndResetBudgetAsync()
    {
        using var scope = new NetCacheTestScope(inUnitTestRunner: false);
        await HttpCacheExamples.Program.Main();
    }
}
