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
    /// <summary>The mode-detector result captured at construction time.</summary>
    private readonly bool? _modeDetectorResult;

    /// <summary>The NetCache state captured at construction time.</summary>
    private readonly NetCache.NetCacheState _netCacheState;

    /// <summary>Initializes a new instance of the <see cref="NetCacheTestScope"/> class.</summary>
    /// <param name="inUnitTestRunner">Optional mode-detector result to force for the scope.</param>
    public NetCacheTestScope(bool? inUnitTestRunner = null)
    {
        _modeDetectorResult = ModeDetector.InUnitTestRunner();
        if (inUnitTestRunner is not null)
        {
            ModeDetector.OverrideModeDetector(new FixedModeDetector(inUnitTestRunner.Value));
        }

        _netCacheState = NetCache.CaptureState();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        NetCache.RestoreState(_netCacheState);
        ModeDetector.OverrideModeDetector(new FixedModeDetector(_modeDetectorResult));
    }

    /// <summary>Mode detector with a fixed unit-test result.</summary>
    /// <param name="result">The result returned by <see cref="IModeDetector.InUnitTestRunner"/>.</param>
    private sealed class FixedModeDetector(bool? result) : IModeDetector
    {
        /// <inheritdoc />
        public bool? InUnitTestRunner() => result;
    }
}
