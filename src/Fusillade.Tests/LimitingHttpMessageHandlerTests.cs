// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

#if REACTIVE_SHIM
namespace Fusillade.Reactive.Tests;
#else
namespace Fusillade.Tests;
#endif

/// <summary>Checks the limiting handler base class helpers.</summary>
public class LimitingHttpMessageHandlerTests
{
    /// <summary>A non-null byte limit used before reset clears it.</summary>
    private const int InitialByteLimit = 42;

    /// <summary>Verifies that the nullable inner-handler constructor falls back to an HTTP client handler.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ConstructorShouldCreateDefaultInnerHandlerWhenNullAsync()
    {
        using var handler = new TestLimitingHttpMessageHandler(null);

        await Assert.That(handler.InnerHandler).IsNotNull();
    }

    /// <summary>Verifies that the supplied inner handler is retained.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ConstructorShouldKeepProvidedInnerHandlerAsync()
    {
        using var innerHandler = new HttpClientHandler();
        using var handler = new TestLimitingHttpMessageHandler(innerHandler);

        await Assert.That(ReferenceEquals(handler.InnerHandler, innerHandler)).IsTrue();
    }

    /// <summary>Verifies the parameterless constructor path.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ParameterlessConstructorShouldCreateHandlerAsync()
    {
        using var handler = new ParameterlessLimitingHttpMessageHandler();

        await Assert.That(handler).IsNotNull();
    }

    /// <summary>Verifies that ResetLimit clears the limit through the abstract overload.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Test]
    public async Task ResetLimitShouldClearLimitAsync()
    {
        using var handler = new TestLimitingHttpMessageHandler(null);

        handler.ResetLimit(InitialByteLimit);
        handler.ResetLimit();

        await Assert.That(handler.MaxBytesToRead).IsNull();
    }

    /// <summary>Test implementation for the nullable-inner constructor.</summary>
    /// <param name="innerHandler">The inner handler.</param>
    private sealed class TestLimitingHttpMessageHandler(HttpMessageHandler? innerHandler) : LimitingHttpMessageHandler(innerHandler)
    {
        /// <summary>Gets the last byte limit passed to ResetLimit.</summary>
        public long? MaxBytesToRead { get; private set; }

        /// <inheritdoc />
        public override void ResetLimit(long? maxBytesToRead) => MaxBytesToRead = maxBytesToRead;
    }

    /// <summary>Test implementation for the parameterless constructor.</summary>
    private sealed class ParameterlessLimitingHttpMessageHandler : LimitingHttpMessageHandler
    {
        /// <summary>Gets the last byte limit passed to ResetLimit.</summary>
        public long? MaxBytesToRead { get; private set; }

        /// <inheritdoc />
        public override void ResetLimit(long? maxBytesToRead) => MaxBytesToRead = maxBytesToRead;
    }
}
