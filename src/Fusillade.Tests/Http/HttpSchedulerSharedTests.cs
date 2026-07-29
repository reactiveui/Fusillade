// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
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

    /// <summary>The number of seconds allowed for deterministic test waits.</summary>
    private const int DefaultTimeoutSeconds = 2;

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

    /// <summary>The number of bytes in a megabyte.</summary>
    private const int BytesPerMegabyte = 1_048_576;

    /// <summary>The number of megabytes allowed for the release download request buffer.</summary>
    private const int ReleaseRequestBufferMegabytes = 64;

    /// <summary>The base URI used by requests sent through the test invoker.</summary>
    private static readonly Uri ExampleBaseUri = new(ExampleBaseUrl);

    /// <summary>The upper bound for deterministic waits before a test is considered hung.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

    /// <summary>Checks to make sure a dummy request is completed.</summary>
    /// <returns>A task to monitor the progress.</returns>
    [Test]
    public async Task HttpSchedulerShouldCompleteADummyRequestAsync()
    {
        var fixture = CreateFixture(new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            return Signal.Emit(ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        using var rq = CreateGetRequest("/");

        using var response = await Signal.FromTask(client.SendAsync(rq, CancellationToken.None))
            .Timeout(DefaultTimeout, ThreadPoolSequencer.Instance)
            .ToTask();

        var bytes = await response.Content.ReadAsByteArrayAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
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

            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

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

        using var client = CreateMessageInvoker(fixture);

        var (requests, responses) = CreateAndSendGetRequests(client, TotalRequests);

        using (Assert.Multiple())
        {
            await Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref scheduledCount) == MaxConcurrentRequests && blockedRqs.Count == MaxConcurrentRequests, DefaultTimeout)).IsTrue();
            await Assert.That(scheduledCount).IsEqualTo(MaxConcurrentRequests);
            await Assert.That(completedCount).IsEqualTo(0);
        }

        // Complete one request to free a slot and allow the 5th to be scheduled.
        var firstSubj = GetFirstValue(blockedRqs);
        firstSubj.OnNext(RxVoid.Default);
        firstSubj.OnCompleted();

        // Wait for the 5th to be scheduled deterministically.
        await scheduled5Tcs.Task.WaitAsync(DefaultTimeout);

        using (Assert.Multiple())
        {
            // Ensure the completedCount advanced for the one we just finished.
            await Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref completedCount) >= 1, DefaultTimeout)).IsTrue();
            await Assert.That(scheduledCount).IsEqualTo(TotalRequests);
            await Assert.That(completedCount).IsEqualTo(1);
        }

        await CompleteQueuedRequestsAsync(blockedRqs, completed5Tcs, responses, requests);

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
        var fixture = CreateFixture(new TestHttpMessageHandler(static _ =>
        {
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            return Signal.Emit(ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        fixture.ResetLimit(RateLimitByteBudget);

        // Under the limit => succeed
        using var rq = CreateGetRequest("/");
        using var resp = await client.SendAsync(rq, CancellationToken.None);
        await Assert.That(resp.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Crossing the limit => succeed
        using var secondRequest = CreateGetRequest("/");
        using var secondResponse = await client.SendAsync(secondRequest, CancellationToken.None);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Over the limit => cancelled
        using var canceledRequest = CreateGetRequest("/");
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await client.SendAsync(canceledRequest, CancellationToken.None));
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
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = CreateGetRequest(path);
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

    /// <summary>Checks that disposing one debounced response does not invalidate another caller's content.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task DebouncedResponsesShouldHaveIndependentContentLifetimesAsync()
    {
        const string ExpectedContent = "foo";
        var messageCount = 0;
        using var handlerEntered = new SemaphoreSlim(0);
        using var gate = new Signal<RxVoid>();

        var fixture = (RateLimitedHttpMessageHandler)CreateFixture(new TestHttpMessageHandler(__ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ExpectedContent, Encoding.UTF8) };

            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();
            return gate.Take(1).Select(__ => response);
        }));

        using var client = CreateMessageInvoker(fixture);

        async Task<HttpResponseMessage> SendGetAsync()
        {
            using var request = CreateGetRequest("/");
            return await client.SendAsync(request, CancellationToken.None);
        }

        var firstResponseTask = SendGetAsync();
        await Assert.That(await handlerEntered.WaitAsync(DefaultTimeout)).IsTrue();

        var secondResponseTask = SendGetAsync();
        await Assert.That(SpinWait.SpinUntil(() => fixture.TotalInflightReferenceCount == DebouncedReferenceCount, DefaultTimeout)).IsTrue();

        gate.OnNext(RxVoid.Default);
        gate.OnCompleted();

        var firstResponse = await firstResponseTask;
        using var secondResponse = await secondResponseTask;

        var responsesAreDistinct = !ReferenceEquals(firstResponse, secondResponse);
        var contentsAreDistinct = !ReferenceEquals(firstResponse.Content, secondResponse.Content);
        string firstContent;
        try
        {
            firstContent = await firstResponse.Content.ReadAsStringAsync();
        }
        finally
        {
            firstResponse.Dispose();
        }

        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(responsesAreDistinct).IsTrue();
            await Assert.That(contentsAreDistinct).IsTrue();
            await Assert.That(firstContent).IsEqualTo(ExpectedContent);
            await Assert.That(secondContent).IsEqualTo(ExpectedContent);
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
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = CreateGetRequest(path);
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
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1).Select(__ => ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = CreateGetRequest(path);
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
        using var client = CreateMessageInvoker(fixture);

        using var request = CreateGetRequest("/");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendAsync(request, CancellationToken.None));
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
            var ret = new HttpResponseMessage { Content = new StringContent("foo", Encoding.UTF8), StatusCode = HttpStatusCode.OK };

            ret.Headers.ETag = new(ETagValue);
            _ = Interlocked.Increment(ref messageCount);
            _ = handlerEntered.Release();

            return gate.Take(1)
                .Do(__ => Interlocked.Increment(ref finalMessageCount))
                .Select(__ => ret);
        }));

        using var client = CreateMessageInvoker(fixture);

        // Local function so the request (an IDisposable) is created and awaited entirely
        // within its own scope; callers only ever hold the resulting Task.
        async Task<HttpResponseMessage> SendGet(string path, CancellationToken token = default)
        {
            using var request = CreateGetRequest(path);
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

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await resp1Task);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await resp2Task);

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
        var fixture = CreateFixture(new HttpClientHandler { AllowAutoRedirect = true, MaxRequestContentBufferSize = BytesPerMegabyte * ReleaseRequestBufferMegabytes });

        using var client = CreateMessageInvoker(fixture);
        using var result = await client.SendAsync(new(HttpMethod.Get, new Uri(Input)), CancellationToken.None);
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

    /// <summary>Creates an invoker that owns the supplied test handler.</summary>
    /// <param name="handler">The handler to invoke.</param>
    /// <returns>An invoker for the supplied handler.</returns>
    private static HttpMessageInvoker CreateMessageInvoker(HttpMessageHandler handler) => new(handler, disposeHandler: true);

    /// <summary>Creates an absolute GET request for the test host.</summary>
    /// <param name="path">The path relative to the test host.</param>
    /// <returns>An HTTP GET request.</returns>
    private static HttpRequestMessage CreateGetRequest(string path) => new(HttpMethod.Get, new Uri(ExampleBaseUri, path));

    /// <summary>Creates and sends a set of uniquely addressed GET requests.</summary>
    /// <param name="client">The HTTP invoker used to send each request.</param>
    /// <param name="count">The number of requests to create.</param>
    /// <returns>The created requests and their response tasks.</returns>
    private static (HttpRequestMessage[] Requests, Task<HttpResponseMessage>[] Responses) CreateAndSendGetRequests(
        HttpMessageInvoker client,
        int count)
    {
        var requests = new HttpRequestMessage[count];
        var responses = new Task<HttpResponseMessage>[count];
        for (var i = 0; i < count; i++)
        {
            requests[i] = CreateGetRequest($"/{i}");
            responses[i] = client.SendAsync(requests[i], CancellationToken.None);
        }

        return (requests, responses);
    }

    /// <summary>Completes the queued requests and disposes their test resources.</summary>
    /// <param name="blockedRequests">The signals blocking the inner HTTP handler.</param>
    /// <param name="completedRequests">The signal completed after every request finishes.</param>
    /// <param name="responseTasks">The response tasks to await.</param>
    /// <param name="requests">The requests created by the test.</param>
    /// <returns>A task that completes after the queued requests finish.</returns>
    private static async Task CompleteQueuedRequestsAsync(
        ConcurrentDictionary<HttpRequestMessage, Signal<RxVoid>> blockedRequests,
        TaskCompletionSource completedRequests,
        IEnumerable<Task<HttpResponseMessage>> responseTasks,
        IEnumerable<HttpRequestMessage> requests)
    {
        var signals = GetValuesSnapshot(blockedRequests);
        foreach (var signal in signals)
        {
            signal.OnNext(RxVoid.Default);
            signal.OnCompleted();
        }

        await completedRequests.Task.WaitAsync(DefaultTimeout);
        var responses = await Task.WhenAll(responseTasks).WaitAsync(DefaultTimeout);
        DisposeCompletedRequests(responses, requests, signals);
    }

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
        foreach (var pair in values)
        {
            snapshot[index] = pair.Value;
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
