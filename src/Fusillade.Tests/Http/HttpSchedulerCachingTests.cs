// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
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
    private const string TestBarUrl = "https://lol/bar";

    /// <summary>The repeated unique key test URL.</summary>
    private const string UniqueKeyTestUrl = "https://example/foo";

    /// <summary>The test response payload.</summary>
    private const string FooContent = "foo";

    /// <summary>The test response entity tag.</summary>
    private const string FooEntityTag = "\"worifjw\"";

    /// <summary>The cache-key prefix used by the scheduler.</summary>
    private const string CacheKeyPrefix = "HttpSchedulerCache_";

    /// <summary>The byte length of the "foo" test payload.</summary>
    private const int FooContentLength = 3;

    /// <summary>Checks to make sure that the caching functions are only called with content.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldBeCalledWithContentAsync()
    {
        var innerHandler = new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FooContent, Encoding.UTF8) };

            ret.Headers.ETag = new(FooEntityTag);
            return Signal.Emit(ret);
        });

        var contentResponses = new List<byte[]>();

        var fixture = new RateLimitedHttpMessageHandler(
            innerHandler,
            Priority.UserInitiated,
            cacheResultFunc: async (_, re, _, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync(ct)));

        using var client = new HttpMessageInvoker(fixture);
        var str = await GetStringAsync(client, TestBarUrl);

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
        var innerHandler = new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FooContent, Encoding.UTF8) };

            ret.Headers.ETag = new(FooEntityTag);
            return Signal.Emit(ret);
        });

        var etagResponses = new List<string>();
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, re, _, _) =>
        {
            etagResponses.Add(re.Headers.ETag!.Tag);
            return Task.CompletedTask;
        });

        using var client = new HttpMessageInvoker(fixture);
        using var response = await SendGetAsync(client, TestBarUrl);
        await Assert.That(etagResponses[0]).IsEqualTo(FooEntityTag);
    }

    /// <summary>Checks that the default NetCache request cache is used when no cache callback is supplied.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task CachingFunctionShouldUseNetCacheRequestCacheByDefaultAsync()
    {
        using var scope = new NetCacheTestScope(true);
        var requestCache = new RecordingRequestCache();
        NetCache.RequestCache = requestCache;

        var innerHandler = new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FooContent, Encoding.UTF8) };

            return Signal.Emit(ret);
        });

        using var operationQueue = new OperationQueue();
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, operationQueue: operationQueue);
        using var client = new HttpMessageInvoker(fixture);
        using var response = await SendGetAsync(client, TestBarUrl);
        var str = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(str).IsEqualTo("foo");
            await Assert.That(requestCache.SaveCount).IsEqualTo(1);
            await Assert.That(requestCache.SavedKey?.StartsWith(CacheKeyPrefix, StringComparison.Ordinal)).IsTrue();
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

        using var client = new HttpMessageInvoker(cachingHandler);
        var origData = await GetStringAsync(client, "https://httpbin.org/get");

        await Assert.That(origData).Contains("origin");

        var singleKey = await cache.GetAllKeys();
        using (Assert.Multiple())
        {
            await Assert.That(string.IsNullOrEmpty(singleKey)).IsFalse();
            await Assert.That(singleKey.StartsWith(CacheKeyPrefix, StringComparison.Ordinal)).IsTrue();
        }

        var offlineHandler = new OfflineHttpMessageHandler(async (_, key, _) => await cache.Get(key));

        using var offlineClient = new HttpMessageInvoker(offlineHandler);
        var newData = await GetStringAsync(offlineClient, "https://httpbin.org/get");

        await Assert.That(origData).IsEqualTo(newData);

        var shouldDie = true;
        try
        {
            await GetStringAsync(offlineClient, "https://httpbin.org/gzip");
        }
        catch (Exception)
        {
            shouldDie = false;
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
        var innerHandler = new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FooContent, Encoding.UTF8) };

            return Signal.Emit(ret);
        });

        var cached = false;
        var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (_, _, _, _) =>
        {
            cached = true;
            return Task.CompletedTask;
        });

        using var client = new HttpMessageInvoker(fixture);
        using var request = new HttpRequestMessage(new(method), TestBarUrl);
        using var response = await client.SendAsync(request, CancellationToken.None);

        await Assert.That(cached).IsEqualTo(shouldCache);
    }

    /// <summary>Gets the string response for a request sent through an invoker.</summary>
    /// <param name="client">The invoker that sends the request.</param>
    /// <param name="requestUrl">The absolute request URL.</param>
    /// <returns>The response content.</returns>
    private static async Task<string> GetStringAsync(HttpMessageInvoker client, string requestUrl)
    {
        using var response = await SendGetAsync(client, requestUrl);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Sends a GET request through an invoker.</summary>
    /// <param name="client">The invoker that sends the request.</param>
    /// <param name="requestUrl">The absolute request URL.</param>
    /// <returns>The HTTP response.</returns>
    private static async Task<HttpResponseMessage> SendGetAsync(HttpMessageInvoker client, string requestUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        return await client.SendAsync(request, CancellationToken.None);
    }
}
