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
using NUnit.Framework; // switched from xUnit
using ReactiveUI;
using ReactiveUI.Testing;

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
        [Test]
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

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(bytes, Has.Length.EqualTo(3));
            }
        }

        /// <summary>
        /// Checks to make sure that the http scheduler doesn't do too much scheduling all at once.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Test]
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

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref scheduledCount) == 4 && blockedRqs.Count == 4, TimeSpan.FromSeconds(2)), Is.True);
                    Assert.That(scheduledCount, Is.EqualTo(4));
                    Assert.That(completedCount, Is.Zero);
                }

                // Complete one request to free a slot and allow the 5th to be scheduled.
                var firstSubj = blockedRqs.First().Value;
                firstSubj.OnNext(Unit.Default);
                firstSubj.OnCompleted();

                // Wait for the 5th to be scheduled deterministically.
                await scheduled5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                using (Assert.EnterMultipleScope())
                {
                    // Ensure the completedCount advanced for the one we just finished.
                    Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref completedCount) >= 1, TimeSpan.FromSeconds(2)), Is.True);
                    Assert.That(scheduledCount, Is.EqualTo(5));
                    Assert.That(completedCount, Is.EqualTo(1));
                }

                // Complete all remaining requests (snapshot to avoid concurrent mutation during enumeration).
                var subjects = blockedRqs.Values.ToArray();
                foreach (var v in subjects)
                {
                    v.OnNext(Unit.Default);
                    v.OnCompleted();
                }

                // Wait until all completed.
                await completed5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(scheduledCount, Is.EqualTo(5));
                    Assert.That(completedCount, Is.EqualTo(5));
                }

                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Checks to make sure that the rate limited scheduler stops after content limit has been reached.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
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
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Crossing the limit => succeed
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            resp = await client.SendAsync(rq);
            Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Over the limit => cancelled
            rq = new HttpRequestMessage(HttpMethod.Get, "/");
            Assert.ThrowsAsync<TaskCanceledException>(async () => await client.SendAsync(rq));
        }

        /// <summary>
        /// Tests to make sure that concurrent requests aren't debounced.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task ConcurrentRequestsToTheSameResourceAreDebounced()
        {
            var messageCount = 0;
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

            Assert.That(messageCount, Is.Zero);

            var resp1Task = client.SendAsync(rq1);
            var resp2Task = client.SendAsync(rq2);
            Assert.That(messageCount, Is.EqualTo(1));

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            var resp1 = await resp1Task;
            var resp2 = await resp2Task;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resp1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(resp2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(messageCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Checks to make sure that requests don't get unfairly cancelled.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task DebouncedRequestsDontGetUnfairlyCancelled()
        {
            var messageCount = 0;
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

            Assert.That(messageCount, Is.Zero);

            var cts = new CancellationTokenSource();

            var resp1Task = client.SendAsync(rq1, cts.Token);
            var resp2Task = client.SendAsync(rq2);
            Assert.That(messageCount, Is.EqualTo(1));

            cts.Cancel();

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            Assert.ThrowsAsync<TaskCanceledException>(async () => await resp1Task);
            var resp2 = await resp2Task;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resp2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(messageCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Checks to make sure that different paths aren't debounced.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task RequestsToDifferentPathsArentDebounced()
        {
            var messageCount = 0;
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

            Assert.That(messageCount, Is.Zero);

            var resp1Task = client.SendAsync(rq1);
            var resp2Task = client.SendAsync(rq2);
            Assert.That(messageCount, Is.EqualTo(2));

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            var resp1 = await resp1Task;
            var resp2 = await resp2Task;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resp1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(resp2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(messageCount, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Tests if a debounce is fully cancelling requests.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        public async Task FullyCancelledDebouncedRequestsGetForRealCancelled()
        {
            var messageCount = 0;
            var finalMessageCount = 0;
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

            Assert.That(messageCount, Is.Zero);

            var cts = new CancellationTokenSource();

            var resp1Task = client.SendAsync(rq1, cts.Token);
            var resp2Task = client.SendAsync(rq2, cts.Token);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(messageCount, Is.EqualTo(1));
                Assert.That(finalMessageCount, Is.Zero);
            }

            cts.Cancel();

            gate.OnNext(Unit.Default);
            gate.OnNext(Unit.Default);

            Assert.ThrowsAsync<TaskCanceledException>(async () => await resp1Task);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await resp2Task);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(messageCount, Is.EqualTo(1));
                Assert.That(finalMessageCount, Is.Zero);
            }
        }

        /// <summary>
        /// Attempts to download a release from github to test the filters.
        /// </summary>
        /// <returns>A task to monitor the progress.</returns>
        [Test]
        [Category("Slow")]
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

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.IsSuccessStatusCode, Is.True);
                Assert.That(bytes, Has.Length.EqualTo(8089690));
            }
        }

        /// <summary>
        /// Creates the test fixtures.
        /// </summary>
        /// <param name="innerHandler">The inner handler.</param>
        /// <returns>The limiting handler.</returns>
        protected abstract LimitingHttpMessageHandler CreateFixture(HttpMessageHandler? innerHandler = null);
    }
}
