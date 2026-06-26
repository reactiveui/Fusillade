// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Akavache;
using Akavache.SystemTextJson;

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests.Http;
#else
namespace Fusillade.Tests.Http;
#endif

/// <summary>Checks to make sure that the http scheduler caches correctly.</summary>
[NotInParallel]
public class HttpSchedulerCachingTests
{
    /// <summary>The repeated test URL used by cache tests.</summary>
    private const string TestBarUrl = "http://lol/bar";

    /// <summary>The repeated unique key test URL.</summary>
    private const string UniqueKeyTestUrl = "http://example/foo";

    /// <summary>The byte length of the "foo" test payload.</summary>
    private const int FooContentLength = 3;

    /// <summary>Checks to make sure that the caching functions are only called with content.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldBeCalledWithContentAsync()
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new("\"worifjw\"");
            return Signal.Emit(ret);
        });

        var contentResponses = new List<byte[]>();

        var fixture = new RateLimitedHttpMessageHandler(
            innerHandler,
            Priority.UserInitiated,
            cacheResultFunc: async (_, re, _, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync(ct)));

        var client = new HttpClient(fixture);
        var str = await client.GetStringAsync(new Uri(TestBarUrl));

        using (Assert.Multiple())
        {
            await Assert.That(str).IsEqualTo("foo");
            await Assert.That(contentResponses.Count).IsEqualTo(1);
        }

        await Assert.That(contentResponses[0].Length).IsEqualTo(FooContentLength);
    }

    /// <summary>Checks to make sure that the cache preserves the http headers.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldPreserveHeadersAsync()
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new("\"worifjw\"");
            return Signal.Emit(ret);
        });

        var etagResponses = new List<string>();
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, re, _, _) =>
        {
            etagResponses.Add(re.Headers.ETag!.Tag);
            return Task.FromResult(true);
        });

        using var client = new HttpClient(fixture);
        using var response = await client.GetAsync(new Uri(TestBarUrl));
        await Assert.That(etagResponses[0]).IsEqualTo("\"worifjw\"");
    }

    /// <summary>Checks that the default NetCache request cache is used when no cache callback is supplied.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldUseNetCacheRequestCacheByDefaultAsync()
    {
        using var scope = new NetCacheTestScope(true);
        var requestCache = new RecordingRequestCache();
        NetCache.RequestCache = requestCache;

        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            return Signal.Emit(ret);
        });

        using var operationQueue = new OperationQueue();
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, operationQueue: operationQueue);
        using var client = new HttpClient(fixture);
        using var response = await client.GetAsync(new Uri(TestBarUrl));
        var str = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(str).IsEqualTo("foo");
            await Assert.That(requestCache.SaveCount).IsEqualTo(1);
            await Assert.That(requestCache.SavedKey?.StartsWith("HttpSchedulerCache_", StringComparison.Ordinal)).IsTrue();
            await Assert.That(requestCache.SavedBytes).IsNotNull();
        }

        await Assert.That(requestCache.SavedBytes!.Length).IsEqualTo(FooContentLength);
    }

    /// <summary>Checks that authorization headers affect request identity.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task UniqueKeyForRequestShouldIncludeAuthorizationAsync()
    {
        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Get, UniqueKeyTestUrl);
        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, UniqueKeyTestUrl);
        authorizedRequest.Headers.Authorization = new("Bearer", "token");

        var anonymousKey = RateLimitedHttpMessageHandler.UniqueKeyForRequest(anonymousRequest);
        var authorizedKey = RateLimitedHttpMessageHandler.UniqueKeyForRequest(authorizedRequest);

        await Assert.That(authorizedKey == anonymousKey).IsFalse();
    }

    /// <summary>Checks that comment-only user-agent values affect request identity.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task UniqueKeyForRequestShouldIncludeCommentUserAgentAsync()
    {
        using var productRequest = new HttpRequestMessage(HttpMethod.Get, UniqueKeyTestUrl);
        using var commentRequest = new HttpRequestMessage(HttpMethod.Get, UniqueKeyTestUrl);
        productRequest.Headers.UserAgent.ParseAdd("FusilladeTests/1.0");
        commentRequest.Headers.UserAgent.ParseAdd("(FusilladeTests)");

        var productKey = RateLimitedHttpMessageHandler.UniqueKeyForRequest(productRequest);
        var commentKey = RateLimitedHttpMessageHandler.UniqueKeyForRequest(commentRequest);

        await Assert.That(commentKey == productKey).IsFalse();
    }

    /// <summary>Does a round trip integration test.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    [Skip("Requires updated Akavache version to work properly")]
    public async Task RoundTripIntegrationTestAsync()
    {
        var aka = CacheDatabase.CreateBuilder("Fusillade.Tests").WithSerializerSystemTextJson().Build();
        var cache = new InMemoryBlobCache(aka.Serializer!);

        var cachingHandler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated, cacheResultFunc: async (_, resp, key, ct) =>
        {
            var data = await resp.Content.ReadAsByteArrayAsync(ct);
            await cache.Insert(key, data);
        });

        using var client = new HttpClient(cachingHandler);
        var origData = await client.GetStringAsync(new Uri("http://httpbin.org/get"));

        await Assert.That(origData).Contains("origin");

        var singleKey = await cache.GetAllKeys();
        using (Assert.Multiple())
        {
            await Assert.That(string.IsNullOrEmpty(singleKey)).IsFalse();
            await Assert.That(singleKey.StartsWith("HttpSchedulerCache_", StringComparison.Ordinal)).IsTrue();
        }

        var offlineHandler = new OfflineHttpMessageHandler(async (_, key, _) => await cache.Get(key));

        using var offlineClient = new HttpClient(offlineHandler);
        var newData = await offlineClient.GetStringAsync(new Uri("http://httpbin.org/get"));

        await Assert.That(origData).IsEqualTo(newData);

        var shouldDie = true;
        try
        {
            await offlineClient.GetStringAsync(new Uri("http://httpbin.org/gzip"));
        }
        catch (Exception ex)
        {
            shouldDie = false;
            Console.WriteLine(ex);
        }

        await Assert.That(shouldDie).IsFalse();
    }

    /// <summary>Checks that only relevant http methods are cached.</summary>
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
    public async Task OnlyCacheRelevantMethodsAsync(string method, bool shouldCache)
    {
        var innerHandler = new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            return Signal.Emit(ret);
        });

        var cached = false;
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, _, _, _) =>
        {
            cached = true;
            return Task.FromResult(true);
        });

        using var client = new HttpClient(fixture);
        using var request = new HttpRequestMessage(new(method), TestBarUrl);
        using var response = await client.SendAsync(request);

        await Assert.That(cached).IsEqualTo(shouldCache);
    }
}
