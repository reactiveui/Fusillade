// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using System.Reactive.Linq;
using System.Text;
using Akavache;
using Akavache.SystemTextJson;

namespace Fusillade.Tests.Http;

/// <summary>
/// Checks to make sure that the http scheduler caches correctly.
/// </summary>
public class HttpSchedulerCachingTests
{
    /// <summary>The byte length of the "foo" test payload.</summary>
    private const int FooContentLength = 3;

    /// <summary>
    /// Checks to make sure that the caching functiosn are only called with content.
    /// </summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldBeCalledWithContent()
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new("\"worifjw\"");
            return Observable.Return(ret);
        });

        var contentResponses = new List<byte[]>();

        var fixture = new RateLimitedHttpMessageHandler(
            innerHandler,
            Priority.UserInitiated,
            cacheResultFunc: async (_, re, _, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync(ct)));

        var client = new HttpClient(fixture);
        var str = await client.GetStringAsync(new Uri("http://lol/bar"));

        using (Assert.Multiple())
        {
            await Assert.That(str).IsEqualTo("foo");
            await Assert.That(contentResponses.Count).IsEqualTo(1);
        }

        await Assert.That(contentResponses[0].Length).IsEqualTo(FooContentLength);
    }

    /// <summary>
    /// Checks to make sure that the cache preserves the http headers.
    /// </summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldPreserveHeaders()
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new("\"worifjw\"");
            return Observable.Return(ret);
        });

        var etagResponses = new List<string>();
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, re, _, _) =>
        {
            etagResponses.Add(re.Headers.ETag!.Tag);
            return Task.FromResult(true);
        });

        var client = new HttpClient(fixture);
        _ = await client.GetAsync(new Uri("http://lol/bar"));
        await Assert.That(etagResponses[0]).IsEqualTo("\"worifjw\"");
    }

    /// <summary>
    /// Does a round trip integration test.
    /// </summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    [Skip("Requires updated Akavache version to work properly")]
    public async Task RoundTripIntegrationTest()
    {
        var aka = CacheDatabase.CreateBuilder("Fusillade.Tests").WithSerializerSystemTextJson().Build();
        var cache = new InMemoryBlobCache(aka.Serializer!);

        var cachingHandler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated, cacheResultFunc: async (_, resp, key, ct) =>
        {
            var data = await resp.Content.ReadAsByteArrayAsync(ct);
            await cache.Insert(key, data);
        });

        var client = new HttpClient(cachingHandler);
        var origData = await client.GetStringAsync(new Uri("http://httpbin.org/get"));

        await Assert.That(origData).Contains("origin");

        var singleKey = await cache.GetAllKeys();
        using (Assert.Multiple())
        {
            await Assert.That(string.IsNullOrEmpty(singleKey)).IsFalse();
            await Assert.That(singleKey.StartsWith("HttpSchedulerCache_", StringComparison.Ordinal)).IsTrue();
        }

        var offlineHandler = new OfflineHttpMessageHandler(async (_, key, _) => await cache.Get(key));

        client = new(offlineHandler);
        var newData = await client.GetStringAsync(new Uri("http://httpbin.org/get"));

        await Assert.That(origData).IsEqualTo(newData);

        var shouldDie = true;
        try
        {
            await client.GetStringAsync(new Uri("http://httpbin.org/gzip"));
        }
        catch (Exception ex)
        {
            shouldDie = false;
            Console.WriteLine(ex);
        }

        await Assert.That(shouldDie).IsFalse();
    }

    /// <summary>
    /// Checks that only relevant http methods are cached.
    /// </summary>
    /// <param name="method">The name of the method.</param>
    /// <param name="shouldCache">If it should be cached or not.</param>
    /// <returns>A task to monitor the progress.</returns>
    [Arguments("GET", true)]
    [Arguments("HEAD", true)]
    [Arguments("OPTIONS", true)]
    [Arguments("POST", false)]
    [Arguments("DELETE", false)]
    [Arguments("PUT", false)]
    [Arguments("WHATEVER", false)]
    [Test]
    public async Task OnlyCacheRelevantMethods(string method, bool shouldCache)
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            return Observable.Return(ret);
        });

        var cached = false;
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, _, _, _) =>
        {
            cached = true;
            return Task.FromResult(true);
        });

        using var client = new HttpClient(fixture);
        var request = new HttpRequestMessage(new(method), "http://lol/bar");
        await client.SendAsync(request);

        await Assert.That(cached).IsEqualTo(shouldCache);
    }
}
