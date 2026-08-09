// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Splat;

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Restores NetCache and mode-detector static state around tests that intentionally touch it.</summary>
internal sealed class NetCacheTestScope : IDisposable
{
    /// <summary>The mode-detector result captured when this scope overrides the detector.</summary>
    private readonly bool? _modeDetectorResult;

    /// <summary>The NetCache state captured at construction time.</summary>
    private readonly NetCacheState _netCacheState;

    /// <summary>Whether this scope replaced the mode detector and must restore its observed result.</summary>
    private readonly bool _restoreModeDetector;

    /// <summary>Initializes a new instance of the <see cref="NetCacheTestScope"/> class.</summary>
    /// <param name="inUnitTestRunner">Optional mode-detector result to force for the scope.</param>
    public NetCacheTestScope(bool? inUnitTestRunner = null)
    {
        _netCacheState = NetCacheState.Capture();
        NetCacheState.ClearThreadOverrides();

        if (inUnitTestRunner is not { } result)
        {
            return;
        }

        _modeDetectorResult = ModeDetector.InUnitTestRunner();
        _restoreModeDetector = true;
        ModeDetector.OverrideModeDetector(new FixedModeDetector(result));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _netCacheState.Restore();

        if (!_restoreModeDetector)
        {
            return;
        }

        ModeDetector.OverrideModeDetector(new FixedModeDetector(_modeDetectorResult));
    }

    /// <summary>A snapshot of all NetCache process-wide and current-thread state touched by tests.</summary>
    private sealed class NetCacheState
    {
        /// <summary>The captured process-wide state.</summary>
        private readonly GlobalState _globalState;

        /// <summary>The captured current-thread state.</summary>
        private readonly ThreadState _threadState;

        /// <summary>Initializes a new instance of the <see cref="NetCacheState"/> class.</summary>
        /// <param name="globalState">The captured process-wide state.</param>
        /// <param name="threadState">The captured current-thread state.</param>
        private NetCacheState(GlobalState globalState, ThreadState threadState)
        {
            _globalState = globalState;
            _threadState = threadState;
        }

        /// <summary>Captures all NetCache process-wide and current-thread state.</summary>
        /// <returns>The captured state.</returns>
        public static NetCacheState Capture() => new(GlobalState.Capture(), ThreadState.Capture());

        /// <summary>Clears current-thread NetCache overrides so a test starts with process-wide state.</summary>
        public static void ClearThreadOverrides() => ThreadState.Clear();

        /// <summary>Restores all NetCache process-wide and current-thread state.</summary>
        public void Restore()
        {
            _globalState.Restore();
            _threadState.Restore();
        }

        /// <summary>A snapshot of process-wide NetCache state.</summary>
        private sealed class GlobalState
        {
            /// <summary>The captured speculative handler.</summary>
            private readonly LimitingHttpMessageHandler _speculative;

            /// <summary>The captured user-initiated handler.</summary>
            private readonly HttpMessageHandler _userInitiated;

            /// <summary>The captured background handler.</summary>
            private readonly HttpMessageHandler _background;

            /// <summary>The captured offline handler.</summary>
            private readonly HttpMessageHandler _offline;

            /// <summary>The captured operation queue.</summary>
            private readonly OperationQueue _operationQueue;

            /// <summary>The captured request cache.</summary>
            private readonly IRequestCache? _requestCache;

            /// <summary>The captured dependency resolver.</summary>
            private readonly IReadonlyDependencyResolver? _current;

            /// <summary>Initializes a new instance of the <see cref="GlobalState"/> class.</summary>
            /// <param name="speculative">The speculative handler.</param>
            /// <param name="userInitiated">The user-initiated handler.</param>
            /// <param name="background">The background handler.</param>
            /// <param name="offline">The offline handler.</param>
            /// <param name="operationQueue">The operation queue.</param>
            /// <param name="requestCache">The request cache.</param>
            /// <param name="current">The dependency resolver.</param>
            private GlobalState(
                LimitingHttpMessageHandler speculative,
                HttpMessageHandler userInitiated,
                HttpMessageHandler background,
                HttpMessageHandler offline,
                OperationQueue operationQueue,
                IRequestCache? requestCache,
                IReadonlyDependencyResolver? current)
            {
                _speculative = speculative;
                _userInitiated = userInitiated;
                _background = background;
                _offline = offline;
                _operationQueue = operationQueue;
                _requestCache = requestCache;
                _current = current;
            }

            /// <summary>Captures process-wide NetCache state.</summary>
            /// <returns>The captured state.</returns>
            internal static GlobalState Capture() =>
                new(
                    NetCache.SpeculativeState,
                    NetCache.UserInitiatedState,
                    NetCache.BackgroundState,
                    NetCache.OfflineState,
                    NetCache.OperationQueueState,
                    NetCache.RequestCacheState,
                    NetCache.CurrentState);

            /// <summary>Restores process-wide NetCache state.</summary>
            internal void Restore()
            {
                NetCache.SpeculativeState = _speculative;
                NetCache.UserInitiatedState = _userInitiated;
                NetCache.BackgroundState = _background;
                NetCache.OfflineState = _offline;
                NetCache.OperationQueueState = _operationQueue;
                NetCache.RequestCacheState = _requestCache;
                NetCache.CurrentState = _current;
            }
        }

        /// <summary>A snapshot of current-thread NetCache overrides.</summary>
        private sealed class ThreadState
        {
            /// <summary>The captured speculative handler override.</summary>
            private readonly LimitingHttpMessageHandler? _speculative;

            /// <summary>The captured user-initiated handler override.</summary>
            private readonly HttpMessageHandler? _userInitiated;

            /// <summary>The captured background handler override.</summary>
            private readonly HttpMessageHandler? _background;

            /// <summary>The captured offline handler override.</summary>
            private readonly HttpMessageHandler? _offline;

            /// <summary>The captured operation queue override.</summary>
            private readonly OperationQueue? _operationQueue;

            /// <summary>The captured request cache override.</summary>
            private readonly IRequestCache? _requestCache;

            /// <summary>Initializes a new instance of the <see cref="ThreadState"/> class.</summary>
            /// <param name="speculative">The speculative handler override.</param>
            /// <param name="userInitiated">The user-initiated handler override.</param>
            /// <param name="background">The background handler override.</param>
            /// <param name="offline">The offline handler override.</param>
            /// <param name="operationQueue">The operation queue override.</param>
            /// <param name="requestCache">The request cache override.</param>
            private ThreadState(
                LimitingHttpMessageHandler? speculative,
                HttpMessageHandler? userInitiated,
                HttpMessageHandler? background,
                HttpMessageHandler? offline,
                OperationQueue? operationQueue,
                IRequestCache? requestCache)
            {
                _speculative = speculative;
                _userInitiated = userInitiated;
                _background = background;
                _offline = offline;
                _operationQueue = operationQueue;
                _requestCache = requestCache;
            }

            /// <summary>Captures current-thread NetCache overrides.</summary>
            /// <returns>The captured state.</returns>
            internal static ThreadState Capture() =>
                new(
                    NetCache.UnitTestSpeculativeState,
                    NetCache.UnitTestUserInitiatedState,
                    NetCache.UnitTestBackgroundState,
                    NetCache.UnitTestOfflineState,
                    NetCache.UnitTestOperationQueueState,
                    NetCache.UnitTestRequestCacheState);

            /// <summary>Clears current-thread NetCache overrides.</summary>
            internal static void Clear()
            {
                NetCache.UnitTestSpeculativeState = null;
                NetCache.UnitTestUserInitiatedState = null;
                NetCache.UnitTestBackgroundState = null;
                NetCache.UnitTestOfflineState = null;
                NetCache.UnitTestOperationQueueState = null;
                NetCache.UnitTestRequestCacheState = null;
            }

            /// <summary>Restores current-thread NetCache overrides.</summary>
            internal void Restore()
            {
                NetCache.UnitTestSpeculativeState = _speculative;
                NetCache.UnitTestUserInitiatedState = _userInitiated;
                NetCache.UnitTestBackgroundState = _background;
                NetCache.UnitTestOfflineState = _offline;
                NetCache.UnitTestOperationQueueState = _operationQueue;
                NetCache.UnitTestRequestCacheState = _requestCache;
            }
        }
    }

    /// <summary>Mode detector with a fixed unit-test result.</summary>
    /// <param name="result">The result returned by <see cref="IModeDetector.InUnitTestRunner"/>.</param>
    private sealed class FixedModeDetector(bool? result) : IModeDetector
    {
        /// <inheritdoc />
        public bool? InUnitTestRunner() => result;
    }
}
