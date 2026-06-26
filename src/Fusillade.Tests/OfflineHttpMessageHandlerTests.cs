// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks offline HTTP handler behavior.</summary>
[NotInParallel]
public class OfflineHttpMessageHandlerTests
{
    /// <summary>The base URL for offline handler tests.</summary>
    private const string ExampleBaseUrl = "https://example.com";

    /// <summary>Verifies that a request cache is required when no fetch delegate is supplied.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task SendAsyncShouldRequireRequestCacheAsync()
    {
        using var scope = new NetCacheTestScope();
        using var handler = new OfflineHttpMessageHandler(null);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync(new Uri("/", UriKind.Relative)));
    }

    /// <summary>Verifies that a cache miss returns service unavailable.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task SendAsyncShouldReturnUnavailableWhenBodyIsMissingAsync()
    {
        using var handler = new OfflineHttpMessageHandler((_, _, _) => Task.FromResult<byte[]?>(null));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Verifies that cached bytes are returned as an OK response.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task SendAsyncShouldReturnCachedBodyAsync()
    {
        const string? expected = "cached";
        using var handler = new OfflineHttpMessageHandler((_, _, _) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(expected)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).IsEqualTo(expected);
        }
    }

    /// <summary>Verifies that a null fetch delegate falls back to <see cref="NetCache.RequestCache"/>.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task SendAsyncShouldUseRequestCacheWhenFetchDelegateIsMissingAsync()
    {
        const string? expected = "cached";
        using var scope = new NetCacheTestScope(true);
        NetCache.RequestCache = new RecordingRequestCache
        {
            FetchedBytes = Encoding.UTF8.GetBytes(expected),
        };

        using var handler = new OfflineHttpMessageHandler(null);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).IsEqualTo(expected);
        }
    }
}
