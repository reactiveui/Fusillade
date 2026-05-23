// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using Fusillade.Helpers;
using Punchclock;
using Splat;

namespace Fusillade;

/// <summary>
/// Handles caching for our Http requests.
/// </summary>
public static class NetCache
{
    /// <summary>
    /// The default speculative read budget (5 MB). Speculative requests stop
    /// fetching once this many bytes have been read across all requests.
    /// </summary>
    private const long DefaultSpeculativeMaxBytesToRead = 5_242_880;

    /// <summary>The process-wide speculative handler.</summary>
    private static LimitingHttpMessageHandler speculative;

    /// <summary>The unit-test override for the speculative handler.</summary>
    [ThreadStatic]
    private static LimitingHttpMessageHandler? unitTestSpeculative;

    /// <summary>The process-wide user-initiated handler.</summary>
    private static HttpMessageHandler userInitiated;

    /// <summary>The unit-test override for the user-initiated handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? unitTestUserInitiated;

    /// <summary>The process-wide background handler.</summary>
    private static HttpMessageHandler background;

    /// <summary>The unit-test override for the background handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? unitTestBackground;

    /// <summary>The process-wide offline handler.</summary>
    private static HttpMessageHandler offline;

    /// <summary>The unit-test override for the offline handler.</summary>
    [ThreadStatic]
    private static HttpMessageHandler? unitTestOffline;

    /// <summary>The process-wide operation queue.</summary>
    private static OperationQueue operationQueue = new();

    /// <summary>The unit-test override for the operation queue.</summary>
    [ThreadStatic]
    private static OperationQueue? unitTestOperationQueue;

    /// <summary>The process-wide request cache.</summary>
    private static IRequestCache? requestCache;

    /// <summary>The unit-test override for the request cache.</summary>
    [ThreadStatic]
    private static IRequestCache? unitTestRequestCache;

    /// <summary>The dependency resolver used to resolve handlers.</summary>
    private static IReadonlyDependencyResolver? Current;

    /// <summary>
    /// Initializes static members of the <see cref="NetCache"/> class.
    /// </summary>
    static NetCache()
    {
        var innerHandler = GetCurrent().GetService<HttpMessageHandler>() ?? new HttpClientHandler();

        // NB: In vNext this value will be adjusted based on the user's
        // network connection, but that requires us to go fully platformy
        // like Splat.
        speculative = new RateLimitedHttpMessageHandler(innerHandler, Priority.Speculative, maxBytesToRead: DefaultSpeculativeMaxBytesToRead);
        userInitiated = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated);
        background = new RateLimitedHttpMessageHandler(innerHandler, Priority.Background);
        offline = new OfflineHttpMessageHandler(null);
    }

    /// <summary>
    /// Gets or sets a handler of that allow a certain number of bytes to be
    /// read before cancelling all future requests. This is designed for
    /// reading data that may or may not be used by the user later, in order
    /// to improve response times should the user later request the data.
    /// </summary>
    public static LimitingHttpMessageHandler Speculative
    {
        get => unitTestSpeculative ?? GetCurrent().GetService<LimitingHttpMessageHandler>("Speculative") ?? speculative;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                unitTestSpeculative = value;
                speculative ??= value;
            }
            else
            {
                speculative = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that should be used for requests initiated by a user
    /// action such as clicking an item, they have the highest priority.
    /// </summary>
    public static HttpMessageHandler UserInitiated
    {
        get => unitTestUserInitiated ?? GetCurrent().GetService<HttpMessageHandler>("UserInitiated") ?? userInitiated;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                unitTestUserInitiated = value;
                userInitiated ??= value;
            }
            else
            {
                userInitiated = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that should be used for requests initiated in the
    /// background, and are scheduled at a lower priority.
    /// </summary>
    public static HttpMessageHandler Background
    {
        get => unitTestBackground ?? GetCurrent().GetService<HttpMessageHandler>("Background") ?? background;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                unitTestBackground = value;
                background ??= value;
            }
            else
            {
                background = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a scheduler that fetches results solely from the cache specified in
    /// RequestCache.
    /// </summary>
    public static HttpMessageHandler Offline
    {
        get => unitTestOffline ?? GetCurrent().GetService<HttpMessageHandler>("Offline") ?? offline;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                unitTestOffline = value;
                offline ??= value;
            }
            else
            {
                offline = value;
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
        get => unitTestOperationQueue ?? GetCurrent().GetService<OperationQueue>("OperationQueue") ?? operationQueue;
        set
        {
            ArgumentExceptionHelper.ThrowIfNull(value);

            if (ModeDetector.InUnitTestRunner())
            {
                unitTestOperationQueue = value;
                operationQueue ??= value;
            }
            else
            {
                operationQueue = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a request cache that if set  indicates that HTTP handlers should save and load
    /// requests from a cached source.
    /// </summary>
    public static IRequestCache? RequestCache
    {
        get => unitTestRequestCache ?? requestCache;
        set
        {
            if (ModeDetector.InUnitTestRunner())
            {
                unitTestRequestCache = value;
                requestCache ??= value;
            }
            else
            {
                requestCache = value;
            }
        }
    }

    /// <summary>
    /// Creates the default instances. This method is just here to force the static constructor to run.
    /// </summary>
    /// <param name="current">The current.</param>
    internal static void CreateDefaultInstances(IReadonlyDependencyResolver? current) => Current = current;

    /// <summary>Gets the current dependency resolver, falling back to <see cref="AppLocator.Current"/>.</summary>
    /// <returns>The dependency resolver.</returns>
    private static IReadonlyDependencyResolver GetCurrent() => Current ??= AppLocator.Current;
}
