// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Splat;

#if REACTIVE_SHIM
namespace Fusillade.Reactive;
#else
namespace Fusillade;
#endif

/// <summary>Handles caching for our Http requests.</summary>
public static class NetCache
{
    /// <summary>
    /// The default speculative read budget (5 MB). Speculative requests stop
    /// fetching once this many bytes have been read across all requests.
    /// </summary>
    private const long DefaultSpeculativeMaxBytesToRead = 5_242_880;

    /// <summary>The process-wide speculative handler.</summary>
    private static LimitingHttpMessageHandler _speculative;

    /// <summary>The unit-test override for the speculative handler.</summary>
    [ThreadStatic]
    private static LimitingHttpMessageHandler? _unitTestSpeculative;

    /// <summary>The process-wide user-initiated handler.</summary>
    private static HttpMessageHandler _userInitiated;

    /// <summary>The unit-test override for the user-initiated handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? _unitTestUserInitiated;

    /// <summary>The process-wide background handler.</summary>
    private static HttpMessageHandler _background;

    /// <summary>The unit-test override for the background handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? _unitTestBackground;

    /// <summary>The process-wide offline handler.</summary>
    private static HttpMessageHandler _offline;

    /// <summary>The unit-test override for the offline handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? _unitTestOffline;

    /// <summary>The process-wide operation queue.</summary>
    private static OperationQueue _operationQueue = new();

    /// <summary>The unit-test override for the operation queue.</summary>
    [ThreadStatic]
    private static OperationQueue? _unitTestOperationQueue;

    /// <summary>The process-wide request cache.</summary>
    private static IRequestCache? _requestCache;

    /// <summary>The unit-test override for the request cache.</summary>
    [ThreadStatic]
    private static IRequestCache? _unitTestRequestCache;

    /// <summary>The dependency resolver used to resolve handlers.</summary>
    private static IReadonlyDependencyResolver? _current;

    /// <summary>Initializes static members of the <see cref="NetCache"/> class.</summary>
    static NetCache()
    {
        var innerHandler = GetCurrent().GetService<HttpMessageHandler>() ?? new HttpClientHandler();

        // NB: In vNext this value will be adjusted based on the user's
        // network connection, but that requires us to go fully platformy
        // like Splat.
        _speculative = new RateLimitedHttpMessageHandler(innerHandler, Priority.Speculative, maxBytesToRead: DefaultSpeculativeMaxBytesToRead);
        _userInitiated = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated);
        _background = new RateLimitedHttpMessageHandler(innerHandler, Priority.Background);
        _offline = new OfflineHttpMessageHandler(null);
    }

    /// <summary>
    /// Gets or sets a handler of that allow a certain number of bytes to be
    /// read before cancelling all future requests. This is designed for
    /// reading data that may or may not be used by the user later, in order
    /// to improve response times should the user later request the data.
    /// </summary>
    public static LimitingHttpMessageHandler Speculative
    {
        get => _unitTestSpeculative ?? GetCurrent().GetService<LimitingHttpMessageHandler>("Speculative") ?? _speculative;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestSpeculative = value;
            }
            else
            {
                _speculative = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that should be used for requests initiated by a user
    /// action such as clicking an item, they have the highest priority.
    /// </summary>
    public static HttpMessageHandler UserInitiated
    {
        get => _unitTestUserInitiated ?? GetCurrent().GetService<HttpMessageHandler>("UserInitiated") ?? _userInitiated;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestUserInitiated = value;
            }
            else
            {
                _userInitiated = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that should be used for requests initiated in the
    /// background, and are scheduled at a lower priority.
    /// </summary>
    public static HttpMessageHandler Background
    {
        get => _unitTestBackground ?? GetCurrent().GetService<HttpMessageHandler>("Background") ?? _background;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestBackground = value;
            }
            else
            {
                _background = value;
            }
        }
    }

    /// <summary>Gets or sets a scheduler that fetches results solely from the cache specified in RequestCache.</summary>
    public static HttpMessageHandler Offline
    {
        get => _unitTestOffline ?? GetCurrent().GetService<HttpMessageHandler>("Offline") ?? _offline;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestOffline = value;
            }
            else
            {
                _offline = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that should be used for requests initiated in the
    /// operationQueue, and are scheduled at a lower priority. You don't
    /// need to mess with this.
    /// </summary>
    public static OperationQueue OperationQueue
    {
        get => _unitTestOperationQueue ?? GetCurrent().GetService<OperationQueue>("OperationQueue") ?? _operationQueue;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestOperationQueue = value;
            }
            else
            {
                _operationQueue = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a request cache that if set  indicates that HTTP handlers should save and load
    /// requests from a cached source.
    /// </summary>
    public static IRequestCache? RequestCache
    {
        get => _unitTestRequestCache ?? _requestCache;
        set
        {
            if (ModeDetector.InUnitTestRunner())
            {
                _unitTestRequestCache = value;
                _requestCache ??= value;
            }
            else
            {
                _requestCache = value;
            }
        }
    }

    /// <summary>Creates the default instances. This method is just here to force the static constructor to run.</summary>
    /// <param name="current">The current.</param>
    internal static void CreateDefaultInstances(IReadonlyDependencyResolver? current) => _current = current;

    /// <summary>Captures the process-wide and current-thread cache state.</summary>
    /// <returns>An opaque snapshot that can restore the captured state.</returns>
    internal static NetCacheState CaptureState() => NetCacheState.Capture();

    /// <summary>Restores a state previously returned by <see cref="CaptureState"/>.</summary>
    /// <param name="state">The state snapshot to restore.</param>
    internal static void RestoreState(NetCacheState state)
    {
        ArgumentExceptionHelper.ThrowIfNull(state);
        state.Restore();
    }

    /// <summary>Gets the current dependency resolver, falling back to <see cref="AppLocator.Current"/>.</summary>
    /// <returns>The dependency resolver.</returns>
    private static IReadonlyDependencyResolver GetCurrent() => _current ??= AppLocator.Current;

    /// <summary>An opaque snapshot of the process-wide and current-thread cache state.</summary>
    internal sealed class NetCacheState
    {
        /// <summary>The captured process-wide state.</summary>
        private readonly GlobalState _globalState;

        /// <summary>The captured state for the current thread.</summary>
        private readonly ThreadState _threadState;

        /// <summary>Initializes a new instance of the <see cref="NetCacheState"/> class.</summary>
        /// <param name="globalState">The captured process-wide state.</param>
        /// <param name="threadState">The captured state for the current thread.</param>
        private NetCacheState(GlobalState globalState, ThreadState threadState)
        {
            _globalState = globalState;
            _threadState = threadState;
        }

        /// <summary>Captures the current state.</summary>
        /// <returns>The captured state.</returns>
        internal static NetCacheState Capture() => new(GlobalState.Capture(), ThreadState.Capture());

        /// <summary>Restores the captured state.</summary>
        internal void Restore()
        {
            _globalState.Restore();
            _threadState.Restore();
        }

        /// <summary>A snapshot of process-wide state.</summary>
        private sealed class GlobalState
        {
            /// <summary>The captured speculative handler.</summary>
            private readonly LimitingHttpMessageHandler _speculativeState;

            /// <summary>The captured user-initiated handler.</summary>
            private readonly HttpMessageHandler _userInitiatedState;

            /// <summary>The captured background handler.</summary>
            private readonly HttpMessageHandler _backgroundState;

            /// <summary>The captured offline handler.</summary>
            private readonly HttpMessageHandler _offlineState;

            /// <summary>The captured operation queue.</summary>
            private readonly OperationQueue _operationQueueState;

            /// <summary>The captured request cache.</summary>
            private readonly IRequestCache? _requestCacheState;

            /// <summary>The captured dependency resolver.</summary>
            private readonly IReadonlyDependencyResolver? _currentState;

            /// <summary>Initializes a new instance of the <see cref="GlobalState"/> class.</summary>
            /// <param name="speculativeState">The speculative handler.</param>
            /// <param name="userInitiatedState">The user-initiated handler.</param>
            /// <param name="backgroundState">The background handler.</param>
            /// <param name="offlineState">The offline handler.</param>
            /// <param name="operationQueueState">The operation queue.</param>
            /// <param name="requestCacheState">The request cache.</param>
            /// <param name="currentState">The dependency resolver.</param>
            private GlobalState(
                LimitingHttpMessageHandler speculativeState,
                HttpMessageHandler userInitiatedState,
                HttpMessageHandler backgroundState,
                HttpMessageHandler offlineState,
                OperationQueue operationQueueState,
                IRequestCache? requestCacheState,
                IReadonlyDependencyResolver? currentState)
            {
                _speculativeState = speculativeState;
                _userInitiatedState = userInitiatedState;
                _backgroundState = backgroundState;
                _offlineState = offlineState;
                _operationQueueState = operationQueueState;
                _requestCacheState = requestCacheState;
                _currentState = currentState;
            }

            /// <summary>Captures the process-wide state.</summary>
            /// <returns>The captured state.</returns>
            internal static GlobalState Capture() =>
                new(_speculative, _userInitiated, _background, _offline, _operationQueue, _requestCache, _current);

            /// <summary>Restores the process-wide state.</summary>
            internal void Restore()
            {
                _speculative = _speculativeState;
                _userInitiated = _userInitiatedState;
                _background = _backgroundState;
                _offline = _offlineState;
                _operationQueue = _operationQueueState;
                _requestCache = _requestCacheState;
                _current = _currentState;
            }
        }

        /// <summary>A snapshot of state held for the current thread.</summary>
        private sealed class ThreadState
        {
            /// <summary>The captured speculative handler override.</summary>
            private readonly LimitingHttpMessageHandler? _speculativeState;

            /// <summary>The captured user-initiated handler override.</summary>
            private readonly HttpMessageHandler? _userInitiatedState;

            /// <summary>The captured background handler override.</summary>
            private readonly HttpMessageHandler? _backgroundState;

            /// <summary>The captured offline handler override.</summary>
            private readonly HttpMessageHandler? _offlineState;

            /// <summary>The captured operation queue override.</summary>
            private readonly OperationQueue? _operationQueueState;

            /// <summary>The captured request cache override.</summary>
            private readonly IRequestCache? _requestCacheState;

            /// <summary>Initializes a new instance of the <see cref="ThreadState"/> class.</summary>
            /// <param name="speculativeState">The speculative handler override.</param>
            /// <param name="userInitiatedState">The user-initiated handler override.</param>
            /// <param name="backgroundState">The background handler override.</param>
            /// <param name="offlineState">The offline handler override.</param>
            /// <param name="operationQueueState">The operation queue override.</param>
            /// <param name="requestCacheState">The request cache override.</param>
            private ThreadState(
                LimitingHttpMessageHandler? speculativeState,
                HttpMessageHandler? userInitiatedState,
                HttpMessageHandler? backgroundState,
                HttpMessageHandler? offlineState,
                OperationQueue? operationQueueState,
                IRequestCache? requestCacheState)
            {
                _speculativeState = speculativeState;
                _userInitiatedState = userInitiatedState;
                _backgroundState = backgroundState;
                _offlineState = offlineState;
                _operationQueueState = operationQueueState;
                _requestCacheState = requestCacheState;
            }

            /// <summary>Captures the current thread's state.</summary>
            /// <returns>The captured state.</returns>
            internal static ThreadState Capture() =>
                new(
                    _unitTestSpeculative,
                    _unitTestUserInitiated,
                    _unitTestBackground,
                    _unitTestOffline,
                    _unitTestOperationQueue,
                    _unitTestRequestCache);

            /// <summary>Restores the current thread's state.</summary>
            internal void Restore()
            {
                _unitTestSpeculative = _speculativeState;
                _unitTestUserInitiated = _userInitiatedState;
                _unitTestBackground = _backgroundState;
                _unitTestOffline = _offlineState;
                _unitTestOperationQueue = _operationQueueState;
                _unitTestRequestCache = _requestCacheState;
            }
        }
    }
}
