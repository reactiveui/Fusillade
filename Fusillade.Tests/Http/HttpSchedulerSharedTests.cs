using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fusillade;
using Microsoft.Reactive.Testing;
using Punchclock;
using ReactiveUI;
using ReactiveUI.Testing;
using Xunit;

namespace Fusillade.Tests
{
    public abstract class HttpSchedulerSharedTests
    {
        protected abstract SpeculativeHttpMessageHandler CreateFixture(HttpMessageHandler innerHandler = null);

        [Fact]
        public async Task HttpSchedulerShouldCompleteADummyRequest()
        {
            var fixture = CreateFixture(new TestHttpMessageHandler(_ => {
                var ret = new HttpResponseMessage() {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };
            
                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            }));

            var client = new HttpClient(fixture) {
                BaseAddress = new Uri("http://example"),
            };

            var rq = new HttpRequestMessage(HttpMethod.Get, "/");

            var result = await client.SendAsync(rq).ToObservable()
                .Timeout(TimeSpan.FromSeconds(2.0), RxApp.TaskpoolScheduler);

            var bytes = await result.Content.ReadAsByteArrayAsync();

            Console.WriteLine(Encoding.UTF8.GetString(bytes));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(3 /*foo*/, bytes.Length);
        }

        [Fact]
        public void HttpSchedulerShouldntScheduleLotsOfStuffAtOnce()
        {
            var blockedRqs = new Dictionary<HttpRequestMessage, Subject<Unit>>();
            var scheduledCount = default(int);
            var completedCount = default(int);

            var fixture = CreateFixture(new TestHttpMessageHandler(rq => {
                scheduledCount++;
                var ret = new HttpResponseMessage() {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");

                blockedRqs[rq] = new Subject<Unit>();
                return blockedRqs[rq].Select(_ => ret).Finally(() => completedCount++);
            }));

            var client = new HttpClient(fixture);
            client.BaseAddress = new Uri("http://example");

            (new TestScheduler()).With(sched => {
                var rqs = Enumerable.Range(0, 5)
                    .Select(x => new HttpRequestMessage(HttpMethod.Get, "/" + x.ToString()))
                    .ToArray();

                var results = rqs.ToObservable()
                    .Select(rq => client.SendAsync(rq))
                    .Merge()
                    .CreateCollection();

                sched.Start();
                        
                Assert.Equal(4, scheduledCount);
                Assert.Equal(0, completedCount);

                var firstSubj = blockedRqs.First().Value;
                firstSubj.OnNext(Unit.Default); firstSubj.OnCompleted();

                sched.Start();

                Assert.Equal(5, scheduledCount);
                Assert.Equal(1, completedCount);

                foreach (var v in blockedRqs.Values) {
                    v.OnNext(Unit.Default); v.OnCompleted();
                }

                sched.Start();

                Assert.Equal(5, scheduledCount);
                Assert.Equal(5, completedCount);
            });
        }

        [Fact]
        public async Task RateLimitedSchedulerShouldStopAfterContentLimitReached()
        {
            var fixture = CreateFixture(new TestHttpMessageHandler(_ => {
                var ret = new HttpResponseMessage() {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };
            
                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            }));

            var client = new HttpClient(fixture) {
                BaseAddress = new Uri("http://example"),
            };

            fixture.ResetLimit(5);

            // Under the limit => succeed
            var rq = new HttpRequestMessage(HttpMethod.Get, "/");
            var resp = await client.SendAsync(rq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Crossing the limit => succeed
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            resp = await client.SendAsync(rq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Over the limit => cancelled
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            await Assert.ThrowsAsync<TaskCanceledException>(() => client.SendAsync(rq));
        }

        /*
        [Fact]
        public void CancelAllShouldCancelAllInflightRequests()
        {
            // NB: This is intentionally picked to be under the OperationQueue's
            // default concurrency limit of 4
            var resps = Enumerable.Range(0, 3)
                .Select(_ => new AsyncSubject<HttpResponseMessage>())
                .ToArray();

            var currentResp = 0;
            var client = new HttpClient(new TestHttpMessageHandler(_ => 
                resps[(currentResp++) % resps.Length]));

            var fixture = CreateFixture();
            fixture.Client = client;

            Assert.True(resps.All(x => x.HasObservers == false));

            fixture.ScheduleAll(sched =>
            {
                resps.ToObservable()
                    .SelectMany(_ => 
                        sched.Schedule(new HttpRequestMessage(HttpMethod.Get, new Uri("http://example/" + Guid.NewGuid())), 3))
                    .Subscribe();
            });

            Assert.True(resps.All(x => x.HasObservers == true));

            fixture.CancelAll();

            Assert.True(resps.All(x => x.HasObservers == false));
        }
        */

        /*
         * HttpSchedulerExtensions
         */

        /*
        [Fact]
        public void ScheduleAllShouldLetUsCancelEverything()
        {
            // NB: This is intentionally picked to be under the OperationQueue's
            // default concurrency limit of 4
            var resps = Enumerable.Range(0, 3)
                .Select(_ => new AsyncSubject<HttpResponseMessage>())
                .ToArray();

            var currentResp = 0;
            var client = new HttpClient(new TestHttpMessageHandler(_ => 
                resps[(currentResp++) % resps.Length]));

            var fixture = CreateFixture();
            fixture.Client = client;

            Assert.True(resps.All(x => x.HasObservers == false));

            var disp = fixture.ScheduleAll(sched =>
            {
                resps.ToObservable()
                    .SelectMany(_ => 
                        sched.Schedule(new HttpRequestMessage(HttpMethod.Get, new Uri("http://example/" + Guid.NewGuid())), 3))
                    .Subscribe();
            });

            Assert.True(resps.All(x => x.HasObservers == true));

            disp.Dispose();

            Assert.True(resps.All(x => x.HasObservers == false));
        }
        */

        [Fact]
        [Trait("Slow", "Very Yes")]
        public async Task DownloadARelease()
        {
            var input = @"https://github.com/akavache/Akavache/releases/download/3.2.0/Akavache.3.2.0.zip";
            var fixture = CreateFixture(new HttpClientHandler() {
                AllowAutoRedirect = true,
                MaxRequestContentBufferSize = 1048576 * 64,
            });

            var client = new HttpClient(fixture);
            var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(input)));
            var bytes = await result.Content.ReadAsByteArrayAsync();

            Assert.True(result.IsSuccessStatusCode);
            Assert.Equal(8089690, bytes.Length);
        }
    }

    public class BaseHttpSchedulerSharedTests : HttpSchedulerSharedTests
    {
        protected override SpeculativeHttpMessageHandler CreateFixture(HttpMessageHandler innerHandler)
        {
            return new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, opQueue: new OperationQueue(4));
        }
    }

    /*
    public class CachingHttpSchedulerSharedTests : HttpSchedulerSharedTests
    {
        protected override IHttpScheduler CreateFixture()
        {
            return new CachingHttpScheduler(new HttpScheduler(opQueue: new OperationQueue(4)), new TestBlobCache());
        }
    }
    */
}
