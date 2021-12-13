﻿// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
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
using Xunit;

namespace Fusillade.Tests.Http
{
    /// <summary>
    /// Checks to make sure that the http scheduler caches correctly.
    /// </summary>
    public class HttpSchedulerCachingTests
    {
        /// <summary>
        /// Checks to make sure that the caching functiosn are only called with content.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
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

#if NET462
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync().ConfigureAwait(false)));
#else
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key, ct) => contentResponses.Add(await re.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)));
#endif

            var client = new HttpClient(fixture);
            var str = await client.GetStringAsync(new Uri("http://lol/bar")).ConfigureAwait(false);

            Assert.Equal("foo", str);
            Assert.Equal(1, contentResponses.Count);
            Assert.Equal(3, contentResponses[0].Length);
        }

        /// <summary>
        /// Checks to make sure that the cache preserves the http headers.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task CachingFunctionShouldPreserveHeaders()
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

            var etagResponses = new List<string>();
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (rq, re, key, ct) =>
            {
                etagResponses.Add(re.Headers.ETag.Tag);
                return Task.FromResult(true);
            });

            var client = new HttpClient(fixture);
            var resp = await client.GetAsync(new Uri("http://lol/bar")).ConfigureAwait(false);
            Assert.Equal("\"worifjw\"", etagResponses[0]);
        }

        /// <summary>
        /// Does a round trip integration test.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task RoundTripIntegrationTest()
        {
            var cache = new InMemoryBlobCache();

            var cachingHandler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated, cacheResultFunc: async (rq, resp, key, ct) =>
            {
                var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                await cache.Insert(key, data);
            });

            var client = new HttpClient(cachingHandler);
            var origData = await client.GetStringAsync(new Uri("http://httpbin.org/get")).ConfigureAwait(false);

            Assert.True(origData.Contains("origin"));
            Assert.Equal(1, (await cache.GetAllKeys()).Count());

            var offlineHandler = new OfflineHttpMessageHandler(async (rq, key, ct) => await cache.Get(key));

            client = new HttpClient(offlineHandler);
            var newData = await client.GetStringAsync(new Uri("http://httpbin.org/get")).ConfigureAwait(false);

            Assert.Equal(origData, newData);

            bool shouldDie = true;
            try
            {
                await client.GetStringAsync(new Uri("http://httpbin.org/gzip")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                shouldDie = false;
                Console.WriteLine(ex);
            }

            Assert.False(shouldDie);
        }

        /// <summary>
        /// Checks that only relevant http methods are cached.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="shouldCache">If it should be cached or not.</param>
        /// <returns>A task to monitor the progress.</returns>
        [Theory]
        [InlineData("GET", true)]
        [InlineData("HEAD", true)]
        [InlineData("OPTIONS", true)]
        [InlineData("POST", false)]
        [InlineData("DELETE", false)]
        [InlineData("PUT", false)]
        [InlineData("WHATEVER", false)]
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
            var request = new HttpRequestMessage(new HttpMethod(method), "http://lol/bar");
            await client.SendAsync(request).ConfigureAwait(false);

            Assert.Equal(shouldCache, cached);
        }
    }
}
