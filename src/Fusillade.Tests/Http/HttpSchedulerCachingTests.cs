// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Akavache;
using Akavache.SystemTextJson;
using NUnit.Framework; // switched from xunit

namespace Fusillade.Tests.Http
{
    /// <summary>
    /// Checks to make sure that the http scheduler caches correctly.
    /// </summary>
    [TestFixture]
    public class HttpSchedulerCachingTests
    {
        /// <summary>
        /// Checks to make sure that the caching functiosn are only called with content.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task CachingFunctionShouldBeCalledWithContent()
        {
            var innerHandler = new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            });

            var contentResponses = new List<byte[]>();

#if NET472
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync()));
#else
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync(ct)));
#endif

            var client = new HttpClient(fixture);
            var str = await client.GetStringAsync(new Uri("http://lol/bar"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(str, Is.EqualTo("foo"));
                Assert.That(contentResponses, Has.Count.EqualTo(1));
            }

            Assert.That(contentResponses[0], Has.Length.EqualTo(3));
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
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new("\"worifjw\"");
                return Observable.Return(ret);
            });

            var etagResponses = new List<string>();
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (rq, re, key, ct) =>
            {
                etagResponses.Add(re.Headers.ETag!.Tag);
                return Task.FromResult(true);
            });

            var client = new HttpClient(fixture);
            var resp = await client.GetAsync(new Uri("http://lol/bar"));
            Assert.That(etagResponses[0], Is.EqualTo("\"worifjw\""));
        }

        /// <summary>
        /// Does a round trip integration test.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task RoundTripIntegrationTest()
        {
            var aka = CacheDatabase.CreateBuilder().WithSerializerSystemTextJson().Build();
            var cache = new InMemoryBlobCache(aka.Serializer!);

            var cachingHandler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated, cacheResultFunc: async (rq, resp, key, ct) =>
            {
#if NET472
                var data = await resp.Content.ReadAsByteArrayAsync();
#else
                var data = await resp.Content.ReadAsByteArrayAsync(ct);
#endif
                await cache.Insert(key, data);
            });

            var client = new HttpClient(cachingHandler);
            var origData = await client.GetStringAsync(new Uri("http://httpbin.org/get"));

            Assert.That(origData, Does.Contain("origin"));

            var singleKey = await cache.GetAllKeys();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(string.IsNullOrEmpty(singleKey), Is.False);
                Assert.That(singleKey.StartsWith("HttpSchedulerCache_", StringComparison.Ordinal), Is.True);
            }

            var offlineHandler = new OfflineHttpMessageHandler(async (rq, key, ct) => await cache.Get(key));

            client = new HttpClient(offlineHandler);
            var newData = await client.GetStringAsync(new Uri("http://httpbin.org/get"));

            Assert.That(origData, Is.EqualTo(newData));

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

            Assert.That(shouldDie, Is.False);
        }

        /// <summary>
        /// Checks that only relevant http methods are cached.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="shouldCache">If it should be cached or not.</param>
        /// <returns>A task to monitor the progress.</returns>
        [TestCase("GET", true)]
        [TestCase("HEAD", true)]
        [TestCase("OPTIONS", true)]
        [TestCase("POST", false)]
        [TestCase("DELETE", false)]
        [TestCase("PUT", false)]
        [TestCase("WHATEVER", false)]
        public async Task OnlyCacheRelevantMethods(string method, bool shouldCache)
        {
            var innerHandler = new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                return Observable.Return(ret);
            });

            var cached = false;
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (rq, re, key, ct) =>
            {
                cached = true;
                return Task.FromResult(true);
            });

            var client = new HttpClient(fixture);
            var request = new HttpRequestMessage(new(method), "http://lol/bar");
            await client.SendAsync(request);

            Assert.That(cached, Is.EqualTo(shouldCache));
        }
    }
}
