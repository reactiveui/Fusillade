// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Base class full of common requests.</summary>
public abstract class HttpSchedulerSharedTests
{
    /// <summary>The ETag value stamped on the canned responses.</summary>
    private const string ETagValue = "\"worifjw\"";

    /// <summary>The base address used by the test clients.</summary>
    private const string ExampleBaseUrl = "http://example";

    /// <summary>The byte length of the "foo" test payload.</summary>
    private const int FooContentLength = 3;

    /// <summary>The maximum number of requests the queue runs concurrently.</summary>
    private const int MaxConcurrentRequests = 4;

    /// <summary>The total number of requests issued by the scheduling test.</summary>
    private const int TotalRequests = 5;

    /// <summary>The number of distinct-path requests issued in the no-debounce test.</summary>
    private const int DistinctPathRequestCount = 2;

    /// <summary>The caller count expected once two requests have debounced onto one in-flight request.</summary>
    private const int DebouncedReferenceCount = 2;

    /// <summary>The byte budget applied in the rate-limit test.</summary>
    private const long RateLimitByteBudget = 5;

    /// <summary>The expected size of the downloaded release archive.</summary>
    private const int ReleaseZipByteLength = 8_089_690;

    /// <summary>The upper bound for deterministic waits before a test is considered hung.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Checks to make sure a dummy request is completed.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task HttpSchedulerShouldCompleteADummyRequestAsync()
    {
        var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            return Signal.Emit(ret);
        }));

        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var rq = new HttpRequestMessage(HttpMethod.Get, "/");

        var result = await Signal.FromTask(client.SendAsync(rq))
            .Timeout(DefaultTimeout, ThreadPoolSequencer.Instance)
            .ToTask();

        var bytes = await result.Content.ReadAsByteArrayAsync();

        using (Assert.Multiple())
        {
            await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(bytes.Length).IsEqualTo(FooContentLength);
        }
    }

    /// <summary>Checks to make sure that the http scheduler doesn't do too much scheduling all at once.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task HttpSchedulerShouldntScheduleLotsOfStuffAtOnceAsync()
    {
        var blockedRqs = new ConcurrentDictionary<HttpRequestMessage, Signal<RxVoid>>();
        var scheduledCount = 0;
        var completedCount = 0;

        var scheduled5Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed5Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var fixture = CreateFixture(new TestHttpMessageHandler(rq =>
        {
            var current = Interlocked.Increment(ref scheduledCount);
            if (current == TotalRequests)
            {
                _ = scheduled5Tcs.TrySetResult();
            }

            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);

            var subj = new Signal<RxVoid>();
            blockedRqs[rq] = subj;

            return subj
                .Select(_ => ret)
                .OnCleanup(() =>
                {
                    var c = Interlocked.Increment(ref completedCount);
                    if (c != TotalRequests)
                    {
                        return;
                    }

                    _ = completed5Tcs.TrySetResult();
                });
        }));

        var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var rqs = new HttpRequestMessage[TotalRequests];
        for (var i = 0; i < rqs.Length; i++)
        {
            rqs[i] = new(HttpMethod.Get, "/" + i);
        }

        var responses = new Task<HttpResponseMessage>[rqs.Length];
        for (var i = 0; i < rqs.Length; i++)
        {
            responses[i] = client.SendAsync(rqs[i]);
        }

        using (Assert.Multiple())
        {
            await Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref scheduledCount) == MaxConcurrentRequests && blockedRqs.Count == MaxConcurrentRequests, TimeSpan.FromSeconds(2))).IsTrue();
            await Assert.That(scheduledCount).IsEqualTo(MaxConcurrentRequests);
            await Assert.That(completedCount).IsEqualTo(0);
        }

        // Complete one request to free a slot and allow the 5th to be scheduled.
        var firstSubj = GetFirstValue(blockedRqs);
        firstSubj.OnNext(RxVoid.Default);
        firstSubj.OnCompleted();

        // Wait for the 5th to be scheduled deterministically.
        await scheduled5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using (Assert.Multiple())
        {
            // Ensure the completedCount advanced for the one we just finished.
            await Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref completedCount) >= 1, TimeSpan.FromSeconds(2))).IsTrue();
            await Assert.That(scheduledCount).IsEqualTo(TotalRequests);
            await Assert.That(completedCount).IsEqualTo(1);
        }

        // Complete all remaining requests (snapshot to avoid concurrent mutation during enumeration).
        foreach (var v in GetValuesSnapshot(blockedRqs))
        {
            v.OnNext(RxVoid.Default);
            v.OnCompleted();
        }

        // Wait until all completed.
        await completed5Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var completedResponses = await Task.WhenAll(responses).WaitAsync(TimeSpan.FromSeconds(2));
        DisposeCompletedRequests(completedResponses, rqs, blockedRqs.Values);

        using (Assert.Multiple())
        {
            await Assert.That(scheduledCount).IsEqualTo(TotalRequests);
            await Assert.That(completedCount).IsEqualTo(TotalRequests);
        }
    }

    /// <summary>Checks to make sure that the rate limited scheduler stops after content limit has been reached.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task RateLimitedSchedulerShouldStopAfterContentLimitReachedAsync()
    {
        var fixture = CreateFixture(new TestHttpMessageHandler(_ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            return Signal.Emit(ret);
        }));

        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        fixture.ResetLimit(RateLimitByteBudget);

        // Under the limit => succeed
        using var rq = new HttpRequestMessage(HttpMethod.Get, "/");
        using var resp = await client.SendAsync(rq);
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Crossing the limit => succeed
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        using var secondResponse = await client.SendAsync(secondRequest);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Over the limit => cancelled
        using var canceledRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await client.SendAsync(canceledRequest));
    }

    /// <summary>Tests to make sure that concurrent requests aren't debounced.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task ConcurrentRequestsToTheSameResourceAreDebouncedAsync()
    {
        var messageCount = 0;
        using var handlerEntered = new SemaphoreSlim(0);
        using var gate = new Signal<RxVoid>();

        var fixture = (RateLimitedHttpMessageHandler)CreateFixture(new TestHttpMessageHandler(__ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            return await client.SendAsync(request, token);
        }

        await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(0);

        // Fire the first request and wait until it has actually reached the (gated) handler.
        var resp1Task = SendGet("/");
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();
        await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);

        // Fire a second request to the same resource; it must debounce onto the first.
        // Wait until both callers are attached before asserting anything.
        var resp2Task = SendGet("/");
        await Assert.That(SpinWait.SpinUntil(() => fixture.TotalInflightReferenceCount == DebouncedReferenceCount, DefaultTimeout)).IsTrue();

        using (Assert.Multiple())
        {
            // One distinct in-flight request, two callers attached => it debounced.
            await Assert.That(fixture.InflightRequestCount).IsEqualTo(1);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);
        }

        // Release the handler; both callers observe the same successful response.
        gate.OnNext(RxVoid.Default);
        gate.OnCompleted();

        using var resp1 = await resp1Task;
        using var resp2 = await resp2Task;

        using (Assert.Multiple())
        {
            await Assert.That(resp1.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(resp2.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);
        }
    }

    /// <summary>Checks to make sure that requests don't get unfairly cancelled.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task DebouncedRequestsDontGetUnfairlyCancelledAsync()
    {
        var messageCount = 0;
        using var handlerEntered = new SemaphoreSlim(0);
        using var gate = new Signal<RxVoid>();

        var fixture = (RateLimitedHttpMessageHandler)CreateFixture(new TestHttpMessageHandler(__ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            return await client.SendAsync(request, token);
        }

        using var cts = new CancellationTokenSource();

        // Cancellable request reaches the gated handler first.
        var resp1Task = SendGet("/", cts.Token);
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();
        await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);

        // A non-cancellable request debounces onto the in-flight one.
        var resp2Task = SendGet("/");
        await Assert.That(SpinWait.SpinUntil(() => fixture.TotalInflightReferenceCount == DebouncedReferenceCount, DefaultTimeout)).IsTrue();
        await Assert.That(fixture.InflightRequestCount).IsEqualTo(1);

        // Cancelling the first caller must not cancel the shared request.
        await cts.CancelAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await resp1Task);

        gate.OnNext(RxVoid.Default);
        gate.OnCompleted();

        using var resp2 = await resp2Task;

        using (Assert.Multiple())
        {
            await Assert.That(resp2.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);
        }
    }

    /// <summary>Checks to make sure that different paths aren't debounced.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task RequestsToDifferentPathsArentDebouncedAsync()
    {
        var messageCount = 0;
        using var handlerEntered = new SemaphoreSlim(0);
        using var gate = new Signal<RxVoid>();

        var fixture = (RateLimitedHttpMessageHandler)CreateFixture(new TestHttpMessageHandler(__ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            return await client.SendAsync(request, token);
        }

        // Two requests to different paths must both reach the handler (no debouncing).
        var resp1Task = SendGet("/foo");
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();

        var resp2Task = SendGet("/bar");
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();

        using (Assert.Multiple())
        {
            // Two distinct in-flight requests => no debouncing occurred.
            await Assert.That(fixture.InflightRequestCount).IsEqualTo(DistinctPathRequestCount);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(DistinctPathRequestCount);
        }

        gate.OnNext(RxVoid.Default);
        gate.OnCompleted();

        using var resp1 = await resp1Task;
        using var resp2 = await resp2Task;

        using (Assert.Multiple())
        {
            await Assert.That(resp1.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(resp2.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(DistinctPathRequestCount);
        }
    }

    /// <summary>Checks that inner handler exceptions are propagated to the caller.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task InnerHandlerExceptionsShouldPropagateAsync()
    {
        var expected = new InvalidOperationException("boom");
        var fixture = CreateFixture(new TestHttpMessageHandler(_ => Signal.Fail<HttpResponseMessage>(expected)));
        using var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync(new Uri("/", UriKind.Relative)));
        await Assert.That(ReferenceEquals(thrown, expected)).IsTrue();
    }

    /// <summary>Tests if a debounce is fully cancelling requests.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task FullyCancelledDebouncedRequestsGetForRealCancelledAsync()
    {
        var messageCount = 0;
        var finalMessageCount = 0;
        using var handlerEntered = new SemaphoreSlim(0);
        using var gate = new Signal<RxVoid>();

        var fixture = (RateLimitedHttpMessageHandler)CreateFixture(new TestHttpMessageHandler(__ =>
        {
            var ret = new HttpResponseMessage
            {
                Content = new StringContent("foo", Encoding.UTF8),
                StatusCode = HttpStatusCode.OK,
            };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1)
                .Do(__ => Interlocked.Increment(ref finalMessageCount))
                .Select(__ => ret);
        }));

        var client = new HttpClient(fixture)
        {
            BaseAddress = new(ExampleBaseUrl),
        };

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            return await client.SendAsync(request, token);
        }

        using var cts = new CancellationTokenSource();

        // First cancellable request reaches the gated handler.
        var resp1Task = SendGet("/", cts.Token);
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();

        // Second cancellable request debounces onto it.
        var resp2Task = SendGet("/", cts.Token);
        await Assert.That(SpinWait.SpinUntil(() => fixture.TotalInflightReferenceCount == DebouncedReferenceCount, DefaultTimeout)).IsTrue();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.InflightRequestCount).IsEqualTo(1);
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);
            await Assert.That(Volatile.Read(ref finalMessageCount)).IsEqualTo(0);
        }

        // Cancelling every caller fully cancels the shared request before the gate fires.
        await cts.CancelAsync();
        await Assert.That(SpinWait.SpinUntil(() => fixture.InflightRequestCount == 0, DefaultTimeout)).IsTrue();

        gate.OnNext(RxVoid.Default);
        gate.OnCompleted();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await resp1Task);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await resp2Task);

        using (Assert.Multiple())
        {
            await Assert.That(Volatile.Read(ref messageCount)).IsEqualTo(1);
            await Assert.That(Volatile.Read(ref finalMessageCount)).IsEqualTo(0);
        }
    }

    /// <summary>Attempts to download a release from github to test the filters.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    [Category("Slow")]
    public async Task DownloadAReleaseAsync()
    {
        const string Input = "https://github.com/akavache/Akavache/releases/download/3.2.0/Akavache.3.2.0.zip";
        var fixture = CreateFixture(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxRequestContentBufferSize = 1_048_576 * 64,
        });

        var client = new HttpClient(fixture);
        var result = await client.SendAsync(new(HttpMethod.Get, new Uri(Input)));
        var bytes = await result.Content.ReadAsByteArrayAsync();

        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccessStatusCode).IsTrue();
            await Assert.That(bytes.Length).IsEqualTo(ReleaseZipByteLength);
        }
    }

    /// <summary>Creates the test fixtures using the default inner handler.</summary>
    /// <returns>The limiting handler.</returns>
    protected LimitingHttpMessageHandler CreateFixture() => CreateFixture(null);

    /// <summary>Creates the test fixtures.</summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <returns>The limiting handler.</returns>
    protected abstract LimitingHttpMessageHandler CreateFixture(HttpMessageHandler? innerHandler);

    /// <summary>Gets the first value from a dictionary without using LINQ.</summary>
    /// <param name="values">The source dictionary.</param>
    /// <returns>The first value.</returns>
    private static Signal<RxVoid> GetFirstValue(ConcurrentDictionary<HttpRequestMessage, Signal<RxVoid>> values)
    {
        using var enumerator = values.Values.GetEnumerator();
        if (enumerator.MoveNext())
        {
            return enumerator.Current;
        }

        throw new InvalidOperationException("Expected at least one blocked request.");
    }

    /// <summary>Gets a stable snapshot of dictionary values without using LINQ.</summary>
    /// <param name="values">The source dictionary.</param>
    /// <returns>The value snapshot.</returns>
    private static Signal<RxVoid>[] GetValuesSnapshot(ConcurrentDictionary<HttpRequestMessage, Signal<RxVoid>> values)
    {
        var snapshot = new Signal<RxVoid>[values.Count];
        var index = 0;
        foreach (var value in values.Values)
        {
            snapshot[index] = value;
            index++;
        }

        if (index == snapshot.Length)
        {
            return snapshot;
        }

        Array.Resize(ref snapshot, index);
        return snapshot;
    }

    /// <summary>Disposes completed request test resources.</summary>
    /// <param name="responses">The completed responses.</param>
    /// <param name="requests">The requests sent by the test.</param>
    /// <param name="signals">The gate signals used by the test handler.</param>
    private static void DisposeCompletedRequests(
        IEnumerable<HttpResponseMessage> responses,
        IEnumerable<HttpRequestMessage> requests,
        IEnumerable<Signal<RxVoid>> signals)
    {
        foreach (var response in responses)
        {
            response.Dispose();
        }

        foreach (var request in requests)
        {
            request.Dispose();
        }

        foreach (var signal in signals)
        {
            signal.Dispose();
        }
    }
}
