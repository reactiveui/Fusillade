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
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key) => {
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
            var fixture = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, cacheResultFunc: async (rq, re, key) => {
                etagResponses.Add(re.Headers.ETag.Tag);
            });

            var client = new HttpClient(fixture);
            var resp = await client.GetAsync("http://lol/bar");
            Assert.Equal("\"worifjw\"", etagResponses[0]);
        }
    }
}
