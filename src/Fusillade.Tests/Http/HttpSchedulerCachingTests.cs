using Akavache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Fusillade.Tests.Http
{
    public class HttpSchedulerCachingTests
    {
        [Fact]
        public async Task CachingFunctionShouldBeCalledWithContent()
        {
            var innerHandler = new TestHttpMessageHandler(_ => {
                var ret = new HttpResponseMessage() {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            });

            var contentResponses = new List<byte[]>();
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key, ct) => {
                contentResponses.Add(await re.Content.ReadAsByteArrayAsync());
            });

            var client = new HttpClient(fixture);
            var str = await client.GetStringAsync("http://lol/bar");

            Assert.Equal("foo", str);
            Assert.Equal(1, contentResponses.Count);
            Assert.Equal(3, contentResponses[0].Length);
        }

        [Fact]
        public async Task CachingFunctionShouldPreserveHeaders()
        {
            var innerHandler = new TestHttpMessageHandler(_ => {
                var ret = new HttpResponseMessage() {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            });

            var etagResponses = new List<string>();
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (rq, re, key, ct) => {
                etagResponses.Add(re.Headers.ETag.Tag);
                return Task.FromResult(true);
            });

            var client = new HttpClient(fixture);
            var resp = await client.GetAsync("http://lol/bar");
            Assert.Equal("\"worifjw\"", etagResponses[0]);
        }

        [Fact]
        public async Task RoundTripIntegrationTest()
        {
            var cache = new InMemoryBlobCache();

            var cachingHandler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated, cacheResultFunc: async (rq, resp, key, ct) => {
                var data = await resp.Content.ReadAsByteArrayAsync();
                await cache.Insert(key, data);
            });

            var client = new HttpClient(cachingHandler);
            var origData = await client.GetStringAsync("http://httpbin.org/get");

            Assert.True(origData.Contains("origin"));
            Assert.Equal(1, (await cache.GetAllKeys()).Count());

            var offlineHandler = new OfflineHttpMessageHandler(async (rq, key, ct) => {
                return await cache.Get(key);
            });

            client = new HttpClient(offlineHandler);
            var newData = await client.GetStringAsync("http://httpbin.org/get");

            Assert.Equal(origData, newData);

            bool shouldDie = true;
            try {
                await client.GetStringAsync("http://httpbin.org/gzip");
            } catch (Exception ex) {
                shouldDie = false;
                Console.WriteLine(ex);
            }

            Assert.False(shouldDie);
        }

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
            var innerHandler = new TestHttpMessageHandler(_ => {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                return Observable.Return(ret);
            });

            var cached = false;
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: (rq, re, key, ct) => {
                cached = true;
                return Task.FromResult(true);
            });

            var client = new HttpClient(fixture);
            var request = new HttpRequestMessage(new HttpMethod(method), "http://lol/bar");
            await client.SendAsync(request);

            Assert.Equal(shouldCache, cached);
        }
    }
}
