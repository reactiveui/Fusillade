// Copyright (c) 2021 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
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
using Microsoft.Reactive.Testing;
using ReactiveUI;
using ReactiveUI.Testing;
using Xunit;

namespace Fusillade.Tests
{
    /// <summary>
    /// Base class full of common requests.
    /// </summary>
    public abstract class HttpSchedulerSharedTests
    {
        /// <summary>
        /// Checks to make sure a dummy request is completed.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
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

            var bytes = await result.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(3 /*foo*/, bytes.Length);
        }

        /// <summary>
        /// Checks to make sure that the http scheduler doesn't do too much scheduling all at once.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public async Task HttpSchedulerShouldntScheduleLotsOfStuffAtOnce()
        {
            var blockedRqs = new ConcurrentDictionary<HttpRequestMessage, Subject<Unit>>();
            var scheduledCount = 0;
            var completedCount = 0;

            var scheduled5Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed5Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await new TestScheduler().WithAsync(async _ =>
            {
                var fixture = CreateFixture(new TestHttpMessageHandler(rq =>
                {
                    var current = Interlocked.Increment(ref scheduledCount);
                    if (current == 5)
                    {
                        scheduled5Tcs.TrySetResult();
                    }

                    var ret = new HttpResponseMessage()
                    {
                        Content = new StringContent("foo", Encoding.UTF8),
                        StatusCode = HttpStatusCode.OK,
                    };

                    ret.Headers.ETag = new EntityTagHeaderValue("\"worifjw\"");

                    var subj = new Subject<Unit>();
                    blockedRqs[rq] = subj;

                    return subj
                        .Select(_ => ret)
                        .Finally(() =>
                        {
                            var c = Interlocked.Increment(ref completedCount);
                            if (c == 5)
                            {
                                completed5Tcs.TrySetResult();
                            }
                        });
                }));

                var client = new HttpClient(fixture)
                {
                    BaseAddress = new Uri("http://example")
                };

                var rqs =
                    Enumerable
                        .Range(0, 5)
                        .Select(x => new HttpRequestMessage(HttpMethod.Get, "/" + x))
                        .ToArray();

                using var subscription =
                    rqs.ToObservable()
                       .Select(rq => client.SendAsync(rq))
                       .Merge()
                       .Subscribe();

                // Wait until the first 4 are scheduled (OperationQueue(4) concurrency).
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref scheduledCount) == 4 && blockedRqs.Count == 4, TimeSpan.FromSeconds(2)));
                Assert.Equal(4, scheduledCount);
                Assert.Equal(0, completedCount);

                // Complete one request to free a slot and allow the 5th to be scheduled.
                var firstSubj = blockedRqs.First().Value;
                firstSubj.OnNext(Unit.Default);
                firstSubj.OnCompleted();

                // Wait for the 5th to be scheduled deterministically.
                await scheduled5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                // Ensure the completedCount advanced for the one we just finished.
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref completedCount) >= 1, TimeSpan.FromSeconds(2)));
                Assert.Equal(5, scheduledCount);
                Assert.Equal(1, completedCount);

                // Complete all remaining requests (snapshot to avoid concurrent mutation during enumeration).
                var subjects = blockedRqs.Values.ToArray();
                foreach (var v in subjects)
                {
                    v.OnNext(Unit.Default);
                    v.OnCompleted();
                }

                // Wait until all completed.
                await completed5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                Assert.Equal(5, scheduledCount);
                Assert.Equal(5, completedCount);

                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Checks to make sure that the rate limited scheduler stops after content limit has been reached.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
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

        /// <summary>
        /// Tests to make sure that concurrent requests aren't debounced.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task ConcurrentRequestsToTheSameResourceAreDebounced()
        {
            int messageCount = 0;
            Subject<Unit> gate = new();

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

        /// <summary>
        /// Checks to make sure that requests don't get unfairly cancelled.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task DebouncedRequestsDontGetUnfairlyCancelled()
        {
            int messageCount = 0;
            Subject<Unit> gate = new();

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

            await Assert.ThrowsAsync<TaskCanceledException>(() => resp1Task);
            var resp2 = await resp2Task;

            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            Assert.Equal(1, messageCount);
        }

        /// <summary>
        /// Checks to make sure that different paths aren't debounced.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task RequestsToDifferentPathsArentDebounced()
        {
            int messageCount = 0;
            Subject<Unit> gate = new();

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

        /// <summary>
        /// Tests if a debounce is fully cancelling requests.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        public async Task FullyCancelledDebouncedRequestsGetForRealCancelled()
        {
            int messageCount = 0;
            int finalMessageCount = 0;
            Subject<Unit> gate = new();

            var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
            {
                var ret = new HttpResponseMessage()
                {
                    Content = new StringContent("foo", Encoding.UTF8),
                    StatusCode = HttpStatusCode.OK,
                };

                ret.Headers.ETag = new("\"worifjw\"");
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

            await Assert.ThrowsAsync<TaskCanceledException>(() => resp1Task);
            await Assert.ThrowsAsync<TaskCanceledException>(() => resp2Task);

            Assert.Equal(1, messageCount);
            Assert.Equal(0, finalMessageCount);
        }

        /// <summary>
        /// Attempts to download a release from github to test the filters.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Fact]
        [Trait("Slow", "Very Yes")]
        public async Task DownloadARelease()
        {
            const string input = "https://github.com/akavache/Akavache/releases/download/3.2.0/Akavache.3.2.0.zip";
            var fixture = CreateFixture(new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                MaxRequestContentBufferSize = 1048576 * 64,
            });

            var client = new HttpClient(fixture);
            var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(input)));
            var bytes = await result.Content.ReadAsByteArrayAsync();

            Assert.True(result.IsSuccessStatusCode);
            Assert.Equal(8089690, bytes.Length);
        }

        /// <summary>
        /// Creates the test fixtures.
        /// </summary>
        /// <param name="innerHandler">The inner handler.</param>
        /// <returns>The limiting handler.</returns>
        protected abstract LimitingHttpMessageHandler CreateFixture(HttpMessageHandler innerHandler = null);
    }
}
