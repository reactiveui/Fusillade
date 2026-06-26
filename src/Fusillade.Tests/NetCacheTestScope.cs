// Copyright (c) 2019-2026 ReactiveUI Association Incorporated. All rights reserved.
// ReactiveUI Association Incorporated licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Splat;

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Restores NetCache and mode-detector static state around tests that intentionally touch it.</summary>
[SuppressMessage(
    "Major Code Smell",
    "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
    Justification = "Tests need to restore private static singleton state without adding production reset APIs.")]
internal sealed class NetCacheTestScope : IDisposable
{
    /// <summary>The current mode detector backing field.</summary>
    private static readonly FieldInfo? ModeDetectorCurrentField =
        typeof(ModeDetector).GetField("<Current>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>The cached unit-test result backing field.</summary>
    private static readonly FieldInfo? ModeDetectorCacheField =
        typeof(ModeDetector).GetField("_cachedInUnitTestRunnerResult", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>The NetCache fields restored when the scope is disposed.</summary>
    private static readonly FieldInfo?[] TrackedFields =
    [
        GetField("_speculative"),
        GetField("_unitTestSpeculative"),
        GetField("_userInitiated"),
        GetField("_unitTestUserInitiated"),
        GetField("_background"),
        GetField("_unitTestBackground"),
        GetField("_offline"),
        GetField("_unitTestOffline"),
        GetField("_operationQueue"),
        GetField("_unitTestOperationQueue"),
        GetField("_requestCache"),
        GetField("_unitTestRequestCache"),
        GetField("_current"),
    ];

    /// <summary>The NetCache field values captured at construction time.</summary>
    private readonly Dictionary<FieldInfo, object?> _fieldValues;

    /// <summary>The mode-detector state captured at construction time.</summary>
    private readonly IModeDetector? _modeDetector;

    /// <summary>The cached unit-test detection result captured at construction time.</summary>
    private readonly bool? _cachedInUnitTestRunnerResult;

    /// <summary>Initializes a new instance of the <see cref="NetCacheTestScope"/> class.</summary>
    /// <param name="inUnitTestRunner">Optional mode-detector result to force for the scope.</param>
    public NetCacheTestScope(bool? inUnitTestRunner = null)
    {
        _modeDetector = (IModeDetector?)ModeDetectorCurrentField?.GetValue(null);
        _cachedInUnitTestRunnerResult = (bool?)ModeDetectorCacheField?.GetValue(null);
        _fieldValues = [];
        foreach (var field in TrackedFields)
        {
            if (field is not null)
            {
                _fieldValues[field] = field.GetValue(null);
            }
        }

        ClearThreadStaticOverrides();

        if (inUnitTestRunner is null)
        {
            return;
        }

        ModeDetector.OverrideModeDetector(new FixedModeDetector(inUnitTestRunner.Value));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (field, value) in _fieldValues)
        {
            field.SetValue(null, value);
        }

        ModeDetectorCurrentField?.SetValue(null, _modeDetector);
        ModeDetectorCacheField?.SetValue(null, _cachedInUnitTestRunnerResult);
    }

    /// <summary>Sets a tracked private NetCache field.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The value to set.</param>
    private static void SetNetCacheField(string name, object? value) => GetField(name)?.SetValue(null, value);

    /// <summary>Clears the thread-static NetCache test overrides.</summary>
    private static void ClearThreadStaticOverrides()
    {
        SetNetCacheField("_unitTestSpeculative", null);
        SetNetCacheField("_unitTestUserInitiated", null);
        SetNetCacheField("_unitTestBackground", null);
        SetNetCacheField("_unitTestOffline", null);
        SetNetCacheField("_unitTestOperationQueue", null);
        SetNetCacheField("_unitTestRequestCache", null);
    }

    /// <summary>Gets a private static NetCache field.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The matching field.</returns>
    private static FieldInfo? GetField(string name) =>
        typeof(NetCache).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>Mode detector with a fixed unit-test result.</summary>
    /// <param name="result">The result returned by <see cref="IModeDetector.InUnitTestRunner"/>.</param>
    private sealed class FixedModeDetector(bool result) : IModeDetector
    {
        /// <inheritdoc />
        public bool? InUnitTestRunner() => result;
    }
}
