// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Splat;

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks to make sure that the NetCache operates correctly.</summary>
[NotInParallel]
public class NetCacheTests
{
    /// <summary>Verifies that we are registering the default handlers correctly.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task DefaultValuesShouldBeRegisteredAsync()
    {
        using var scope = new NetCacheTestScope();

        using (Assert.Multiple())
        {
            await Assert.That(NetCache.Speculative).IsNotNull();
            await Assert.That(NetCache.UserInitiated).IsNotNull();
            await Assert.That(NetCache.Background).IsNotNull();
            await Assert.That(NetCache.Offline).IsNotNull();
            await Assert.That(NetCache.OperationQueue).IsNotNull();
            await Assert.That(NetCache.RequestCache).IsNull();
        }
    }

    /// <summary>Verifies that NetCache resolves registered handlers before falling back to defaults.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ValuesShouldBeResolvedFromCurrentResolverAsync()
    {
        using var scope = new NetCacheTestScope();
        using var resolver = new ModernDependencyResolver();
        using var speculative = new TestLimitingHttpMessageHandler(null);
        using var userInitiated = new HttpClientHandler();
        using var background = new HttpClientHandler();
        using var offline = new HttpClientHandler();
        using var operationQueue = new OperationQueue();

        resolver.RegisterConstant<LimitingHttpMessageHandler>(speculative, "Speculative");
        resolver.RegisterConstant<HttpMessageHandler>(userInitiated, "UserInitiated");
        resolver.RegisterConstant<HttpMessageHandler>(background, "Background");
        resolver.RegisterConstant<HttpMessageHandler>(offline, "Offline");
        resolver.RegisterConstant(operationQueue, "OperationQueue");

        NetCache.CreateDefaultInstances(resolver);

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(NetCache.Speculative, speculative)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UserInitiated, userInitiated)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Background, background)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Offline, offline)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.OperationQueue, operationQueue)).IsTrue();
        }
    }

    /// <summary>Verifies that unit-test mode uses thread-local overrides.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task UnitTestModeSettersShouldUseThreadLocalOverridesAsync()
    {
        using var scope = new NetCacheTestScope(true);
        using var speculative = new TestLimitingHttpMessageHandler(null);
        using var userInitiated = new HttpClientHandler();
        using var background = new HttpClientHandler();
        using var offline = new HttpClientHandler();
        var operationQueue = new OperationQueue();
        var requestCache = new RecordingRequestCache();

        NetCache.Speculative = speculative;
        NetCache.UserInitiated = userInitiated;
        NetCache.Background = background;
        NetCache.Offline = offline;
        NetCache.OperationQueue = operationQueue;
        NetCache.RequestCache = requestCache;

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(NetCache.Speculative, speculative)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UserInitiated, userInitiated)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Background, background)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Offline, offline)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.OperationQueue, operationQueue)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.RequestCache, requestCache)).IsTrue();
        }
    }

    /// <summary>Verifies that nested scopes isolate thread overrides without replacing an unmodified mode detector.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task NestedScopeShouldRestoreModeDetectorAndThreadOverridesAsync()
    {
        using var outerScope = new NetCacheTestScope(false);
        using var speculative = new TestLimitingHttpMessageHandler(null);
        using var userInitiated = new HttpClientHandler();
        using var background = new HttpClientHandler();
        using var offline = new HttpClientHandler();
        var operationQueue = new OperationQueue();
        var requestCache = new RecordingRequestCache();

        NetCache.UnitTestSpeculativeState = speculative;
        NetCache.UnitTestUserInitiatedState = userInitiated;
        NetCache.UnitTestBackgroundState = background;
        NetCache.UnitTestOfflineState = offline;
        NetCache.UnitTestOperationQueueState = operationQueue;
        NetCache.UnitTestRequestCacheState = requestCache;

        var modeDetector = new CountingModeDetector(false);
        ModeDetector.OverrideModeDetector(modeDetector);
        {
            using var innerScope = new NetCacheTestScope();
            using (Assert.Multiple())
            {
                await Assert.That(modeDetector.CallCount).IsEqualTo(0);
                await Assert.That(NetCache.UnitTestSpeculativeState).IsNull();
                await Assert.That(NetCache.UnitTestUserInitiatedState).IsNull();
                await Assert.That(NetCache.UnitTestBackgroundState).IsNull();
                await Assert.That(NetCache.UnitTestOfflineState).IsNull();
                await Assert.That(NetCache.UnitTestOperationQueueState).IsNull();
                await Assert.That(NetCache.UnitTestRequestCacheState).IsNull();
            }
        }

        var restoredMode = ModeDetector.InUnitTestRunner();
        using (Assert.Multiple())
        {
            await Assert.That(restoredMode).IsFalse();
            await Assert.That(modeDetector.CallCount).IsEqualTo(1);
            await Assert.That(ReferenceEquals(NetCache.UnitTestSpeculativeState, speculative)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UnitTestUserInitiatedState, userInitiated)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UnitTestBackgroundState, background)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UnitTestOfflineState, offline)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UnitTestOperationQueueState, operationQueue)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UnitTestRequestCacheState, requestCache)).IsTrue();
        }
    }

    /// <summary>Verifies that process mode updates the shared instances.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ProcessModeSettersShouldUpdateSharedInstancesAsync()
    {
        using var scope = new NetCacheTestScope(false);
        NetCache.CreateDefaultInstances(new ModernDependencyResolver());
        using var speculative = new TestLimitingHttpMessageHandler(null);
        using var userInitiated = new HttpClientHandler();
        using var background = new HttpClientHandler();
        using var offline = new HttpClientHandler();
        var operationQueue = new OperationQueue();
        var requestCache = new RecordingRequestCache();

        NetCache.Speculative = speculative;
        NetCache.UserInitiated = userInitiated;
        NetCache.Background = background;
        NetCache.Offline = offline;
        NetCache.OperationQueue = operationQueue;
        NetCache.RequestCache = requestCache;

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(NetCache.Speculative, speculative)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.UserInitiated, userInitiated)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Background, background)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.Offline, offline)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.OperationQueue, operationQueue)).IsTrue();
            await Assert.That(ReferenceEquals(NetCache.RequestCache, requestCache)).IsTrue();
        }

        NetCache.RequestCache = null;
        await Assert.That(NetCache.RequestCache).IsNull();
    }

    /// <summary>Verifies that NetCache rejects null handlers and queues.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task SettersShouldRejectNullValuesAsync()
    {
        using var scope = new NetCacheTestScope(false);

        using (Assert.Multiple())
        {
            await Assert.ThrowsAsync<ArgumentNullException>(static () =>
            {
                NetCache.Speculative = null!;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<ArgumentNullException>(static () =>
            {
                NetCache.UserInitiated = null!;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<ArgumentNullException>(static () =>
            {
                NetCache.Background = null!;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<ArgumentNullException>(static () =>
            {
                NetCache.Offline = null!;
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<ArgumentNullException>(static () =>
            {
                NetCache.OperationQueue = null!;
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>Test limiting handler for NetCache registration tests.</summary>
    /// <param name="innerHandler">The inner handler.</param>
    private sealed class TestLimitingHttpMessageHandler(HttpMessageHandler? innerHandler) : LimitingHttpMessageHandler(innerHandler)
    {
        /// <inheritdoc />
        public override void ResetLimit(long? maxBytesToRead) => _ = maxBytesToRead;
    }

    /// <summary>Mode detector that records how often its result is requested.</summary>
    /// <param name="result">The result returned by <see cref="IModeDetector.InUnitTestRunner"/>.</param>
    private sealed class CountingModeDetector(bool result) : IModeDetector
    {
        /// <summary>Gets the number of calls made to <see cref="InUnitTestRunner"/>.</summary>
        public int CallCount { get; private set; }

        /// <inheritdoc />
        public bool? InUnitTestRunner()
        {
            CallCount++;
            return result;
        }
    }
}
