// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks shared in-flight request behavior.</summary>
public class InflightRequestTests
{
    /// <summary>The upper bound for deterministic waits before a test is considered hung.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Verifies that a successful response is published to the public observable surface once.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ResponseShouldPublishSuccessfulResultOnceAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        using var ignoredResponse = new HttpResponseMessage(HttpStatusCode.OK);
        using var request = new InflightRequest(() => { });
        var responseTask = request.Response.ToTask(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(request.TrySetResult(response)).IsTrue();
            await Assert.That(request.TrySetResult(ignoredResponse)).IsFalse();
        }

        var result = await responseTask.WaitAsync(DefaultTimeout);
        await Assert.That(ReferenceEquals(result, response)).IsTrue();
    }

    /// <summary>Verifies that exception completion is published once.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ResponseShouldPublishExceptionOnceAsync()
    {
        using var request = new InflightRequest(() => { });
        var responseTask = request.WaitAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(request.TrySetException(new InvalidOperationException("boom"))).IsTrue();
            await Assert.That(request.TrySetException(new InvalidOperationException("again"))).IsFalse();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await responseTask.WaitAsync(DefaultTimeout));
    }

    /// <summary>Verifies that cancellation completion is published as an operation cancellation.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ResponseShouldPublishCancellationAsync()
    {
        using var cts = new CancellationTokenSource();
        using var request = new InflightRequest(() => { });
        var responseTask = request.WaitAsync(CancellationToken.None);

        await Assert.That(request.TrySetCanceled(cts.Token)).IsTrue();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await responseTask.WaitAsync(DefaultTimeout));
    }

    /// <summary>Verifies that a pre-cancelled caller token cancels only that caller.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task WaitAsyncShouldCancelImmediatelyWhenCallerTokenIsAlreadyCancelledAsync()
    {
        var cancelCount = 0;
        using var cts = new CancellationTokenSource();
        using var request = new InflightRequest(() => cancelCount++);

        await cts.CancelAsync();
        var responseTask = request.WaitAsync(cts.Token);

        using (Assert.Multiple())
        {
            await Assert.That(request.ReferenceCount).IsEqualTo(0);
            await Assert.That(cancelCount).IsEqualTo(1);
        }

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await responseTask.WaitAsync(DefaultTimeout));
    }

    /// <summary>Verifies that cancelling all references invokes the shared cancellation callback once.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task CancelShouldRunFullyCancelledCallbackOnceAsync()
    {
        var cancelCount = 0;
        using var request = new InflightRequest(() => cancelCount++);

        request.Cancel();
        request.Cancel();

        using (Assert.Multiple())
        {
            await Assert.That(request.ReferenceCount).IsEqualTo(0);
            await Assert.That(cancelCount).IsEqualTo(1);
        }
    }

    /// <summary>Verifies that completion wins over later caller cancellation.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task CancelAfterCompletionShouldNotRunFullyCancelledCallbackAsync()
    {
        var cancelCount = 0;
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var request = new InflightRequest(() => cancelCount++);

        await Assert.That(request.TrySetResult(response)).IsTrue();
        request.Cancel();

        await Assert.That(cancelCount).IsEqualTo(0);
    }

    /// <summary>Verifies that cancelling one caller leaves other debounced callers attached.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task WaitAsyncShouldDropOnlyTheCancelledCallerReferenceAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var cts = new CancellationTokenSource();
        using var request = new InflightRequest(() => { });
        request.AddRef();

        var cancelledTask = request.WaitAsync(cts.Token);
        await cts.CancelAsync();

        using (Assert.Multiple())
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledTask.WaitAsync(DefaultTimeout));
            await Assert.That(request.ReferenceCount).IsEqualTo(1);
        }

        await Assert.That(request.TrySetResult(response)).IsTrue();
        var result = await request.WaitAsync(CancellationToken.None).WaitAsync(DefaultTimeout);
        await Assert.That(result.StatusCode).IsEqualTo(response.StatusCode);
    }

    /// <summary>Verifies that a live cancellable wait returns the shared response when the response completes first.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task WaitAsyncShouldReturnResponseWhenResponseCompletesBeforeCallerCancellationAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var cts = new CancellationTokenSource();
        using var request = new InflightRequest(() => { });

        var responseTask = request.WaitAsync(cts.Token);

        await Assert.That(request.TrySetResult(response)).IsTrue();
        var result = await responseTask.WaitAsync(DefaultTimeout);

        await Assert.That(result.StatusCode).IsEqualTo(response.StatusCode);
    }

    /// <summary>Verifies that AddRef after full cancellation cannot run the callback a second time.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task CancelShouldNotRunFullyCancelledCallbackTwiceAfterRefIsReaddedAsync()
    {
        var cancelCount = 0;
        using var request = new InflightRequest(() => cancelCount++);

        request.Cancel();
        request.AddRef();
        request.Cancel();

        using (Assert.Multiple())
        {
            await Assert.That(request.ReferenceCount).IsEqualTo(0);
            await Assert.That(cancelCount).IsEqualTo(1);
        }
    }
}
