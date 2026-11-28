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
public sealed class HttpCacheExampleTests : IDisposable
{
    /// <summary>An online client with no cache and no transfer limit.</summary>
    private readonly HttpClient _online = new(new HttpCacheExamples.ProfileTransport());

    /// <inheritdoc/>
    public void Dispose() => _online.Dispose();

    /// <summary>Checks that both callers share a download, that it can be read offline and that a prefetch budget can be reset.</summary>
    /// <returns>Completion after all example assertions pass.</returns>
    [Test]
    public async Task DownloadShareCacheAndResetBudgetAsync()
    {
        using var scope = new NetCacheTestScope(inUnitTestRunner: false);
        await HttpCacheExamples.Program.Main();
    }

    /// <summary>Verifies that an online transport alone provides neither offline reads nor a prefetch budget.</summary>
    /// <param name="cancellationToken">Cancels the transport calls.</param>
    /// <returns>Completion after checking the reported failure.</returns>
    [Test]
    public async Task OnlineTransportAloneFailsCacheAndBudgetChecksAsync(CancellationToken cancellationToken)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HttpCacheExamples.Program.ReadOfflineAsync(new(), _online, cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => HttpCacheExamples.Program.LimitPrefetchAsync(_online, cancellationToken));
    }

    /// <summary>Resetting the custom budget requires a limiting inner handler.</summary>
    /// <returns>Completion after checking the configuration error.</returns>
    [Test]
    public async Task MissingLimiterRejectsResetAsync()
    {
        using var handler = new HttpCacheExamples.PrefetchBudgetHandler();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            handler.ResetLimit(1);
            return Task.CompletedTask;
        });
    }
}
