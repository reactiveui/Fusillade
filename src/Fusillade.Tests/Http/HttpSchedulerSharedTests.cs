// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Microsoft.Reactive.Testing;
using ReactiveUI;
using ReactiveUI.Testing;
using Xunit;

namespace Fusillade.Tests
{
    public abstract class HttpSchedulerSharedTests
    {
        [Fact]
        public async Task HttpSchedulerShouldCompleteADummyRequest()
        {
            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            var rq = new HttpRequestMessage(HttpMethod.Get, "/");

            var result = await client.SendAsync(rq).ToObservable()
                .Timeout(TimeSpan.FromSeconds(2.0), RxApp.TaskpoolScheduler);

            var bytes = await result.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

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

            new TestScheduler().WithAsync(async sched =>
            {
                var fixture = CreateFixture(new TestHttpMessageHandler(rq =>
                {
                    scheduledCount++;
                    var ret = new HttpResponseMessage()
                    {
                        Content = new StringContent("foo", Encoding.UTF8),
                        StatusCode = HttpStatusCode.OK,
                    };

                    ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");

                    blockedRqs[rq] = new Subject<Unit>();
                    return blockedRqs[rq].Select(_ => ret).Finally(() => completedCount++);
                }));

                var client = new HttpClient(fixture);
                client.BaseAddress = new Uri("http://example");

                var rqs =
                    Enumerable
                        .Range(0, 5)
                        .Select(x => new HttpRequestMessage(HttpMethod.Get, "/" + x))
                        .ToArray();

                rqs.ToObservable()
                    .Select(rq => client.SendAsync(rq))
                    .Merge()
                    .ToObservableChangeSet()
                    .ObserveOn(ImmediateScheduler.Instance)
                    .Bind(out var results)
                    .Subscribe();

                Assert.Equal(4, scheduledCount);
                Assert.Equal(0, completedCount);

                var firstSubj = blockedRqs.First().Value;
                firstSubj.OnNext(Unit.Default);
                firstSubj.OnCompleted();

                Assert.Equal(5, scheduledCount);
                Assert.Equal(1, completedCount);

                foreach (var v in blockedRqs.Values)
                {
                    v.OnNext(Unit.Default);
                    v.OnCompleted();
                }

                Assert.Equal(5, scheduledCount);
                Assert.Equal(5, completedCount);
            });
        }

        [Fact]
        public async Task RateLimitedSchedulerShouldStopAfterContentLimitReached()
        {
            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                return Observable.Return(ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            fixture.ResetLimit(5);

            // Under the limit => succeed
            var rq = new HttpRequestMessage(HttpMethod.Get, "/");
            var resp = await client.SendAsync(rq).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Crossing the limit => succeed
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            resp = await client.SendAsync(rq).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Over the limit => cancelled
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            await Assert.ThrowsAsync<TaskCanceledException>(() => client.SendAsync(rq)).ConfigureAwait(false);
        }

        [Fact]
        public async Task ConcurrentRequestsToTheSameResourceAreDebounced()
        {
            int messageCount = 0;
            Subject<Unit> gate = new Subject<Unit>();

            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                messageCount++;

                return gate.Take(1).Select(__ => ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            var rq1 = new HttpRequestMessage(HttpMethod.Get, "/");
            var rq2 = new HttpRequestMessage(HttpMethod.Get, "/");

            Assert.Equal(0, messageCount);

            var resp1Task = client.SendAsync(rq1);
            var resp2Task = client.SendAsync(rq2);
            Assert.Equal(1, messageCount);

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            var resp1 = await resp1Task;
            var resp2 = await resp2Task;

            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            Assert.Equal(1, messageCount);
        }

        [Fact]
        public async Task DebouncedRequestsDontGetUnfairlyCancelled()
        {
            int messageCount = 0;
            Subject<Unit> gate = new Subject<Unit>();

            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                messageCount++;

                return gate.Take(1).Select(__ => ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            var rq1 = new HttpRequestMessage(HttpMethod.Get, "/");
            var rq2 = new HttpRequestMessage(HttpMethod.Get, "/");

            Assert.Equal(0, messageCount);

            /* NB: Here's the thing we're testing for
             *
             * When we issue concurrent requests to the same resource, one of them
             * will actually do the request, and one of them will wait on the other.
             * In this case, rq1 will do the request, and rq2 will just return
             * whatever rq1 will return.
             *
             * The key then, is to only truly cancel rq1 if both rq1 *and* rq2
             * are cancelled, but rq1 should *appear* to be cancelled */
            var cts = new CancellationTokenSource();

            var resp1Task = client.SendAsync(rq1, cts.Token);
            var resp2Task = client.SendAsync(rq2);
            Assert.Equal(1, messageCount);

            cts.Cancel();

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            await Assert.ThrowsAsync<TaskCanceledException>(() => resp1Task).ConfigureAwait(false);
            var resp2 = await resp2Task;

            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            Assert.Equal(1, messageCount);
        }

        [Fact]
        public async Task RequestsToDifferentPathsArentDebounced()
        {
            int messageCount = 0;
            Subject<Unit> gate = new Subject<Unit>();

            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                messageCount++;

                return gate.Take(1).Select(__ => ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            var rq1 = new HttpRequestMessage(HttpMethod.Get, "/foo");
            var rq2 = new HttpRequestMessage(HttpMethod.Get, "/bar");

            Assert.Equal(0, messageCount);

            var resp1Task = client.SendAsync(rq1);
            var resp2Task = client.SendAsync(rq2);
            Assert.Equal(2, messageCount);

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            var resp1 = await resp1Task;
            var resp2 = await resp2Task;

            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            Assert.Equal(2, messageCount);
        }

        [Fact]
        public async Task FullyCancelledDebouncedRequestsGetForRealCancelled()
        {
            int messageCount = 0;
            int finalMessageCount = 0;
            Subject<Unit> gate = new Subject<Unit>();

            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");
                messageCount++;

                return gate.Take(1)
                    .Do(__ => finalMessageCount++)
                    .Select(__ => ret);
            }));

            var client = new HttpClient(fixture)
            {
                BaseAddress = new Uri("http://example"),
            };

            var rq1 = new HttpRequestMessage(HttpMethod.Get, "/");
            var rq2 = new HttpRequestMessage(HttpMethod.Get, "/");

            Assert.Equal(0, messageCount);

            /* NB: Here's the thing we're testing for
             *
             * When we issue concurrent requests to the same resource, one of them
             * will actually do the request, and one of them will wait on the other.
             * In this case, rq1 will do the request, and rq2 will just return
             * whatever rq1 will return.
             *
             * The key then, is to only truly cancel rq1 if both rq1 *and* rq2
             * are cancelled, but rq1 should *appear* to be cancelled. This test
             * cancels both requests then makes sure we actually cancel the
             * underlying result */
            var cts = new CancellationTokenSource();

            var resp1Task = client.SendAsync(rq1, cts.Token);
            var resp2Task = client.SendAsync(rq2, cts.Token);
            Assert.Equal(1, messageCount);
            Assert.Equal(0, finalMessageCount);

            cts.Cancel();

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            await Assert.ThrowsAsync<TaskCanceledException>(() => resp1Task).ConfigureAwait(false);
            await Assert.ThrowsAsync<TaskCanceledException>(() => resp2Task).ConfigureAwait(false);

            Assert.Equal(1, messageCount);
            Assert.Equal(0, finalMessageCount);
        }

        [Fact]
        [Trait("Slow", "Very Yes")]
        public async Task DownloadARelease()
        {
            var input = @"https://github.com/akavache/Akavache/releases/download/3.2.0/Akavache.3.2.0.zip";
            var fixture = CreateFixture(new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                MaxRequestContentBufferSize = 1048576 * 64,
            });

            var client = new HttpClient(fixture);
            var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(input))).ConfigureAwait(false);
            var bytes = await result.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            Assert.True(result.IsSuccessStatusCode);
            Assert.Equal(8089690, bytes.Length);
        }

        protected abstract LimitingHttpMessageHandler CreateFixture(HttpMessageHandler innerHandler = null);
    }
}
