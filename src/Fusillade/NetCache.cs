// Copyright (c) 2025 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using Punchclock;
using Splat;

namespace Fusillade;

/// <summary>
/// Handles caching for our Http requests.
/// </summary>
public static class NetCache
{
    private static LimitingHttpMessageHandler speculative;
    [ThreadStatic]
    private static LimitingHttpMessageHandler? unitTestSpeculative;
    private static HttpMessageHandler userInitiated;
    [ThreadStatic]
    private static HttpMessageHandler? unitTestUserInitiated;
    private static HttpMessageHandler background;
    [ThreadStatic]
    private static HttpMessageHandler? unitTestBackground;
    private static HttpMessageHandler offline;
    [ThreadStatic]
    private static HttpMessageHandler? unitTestOffline;
    private static OperationQueue operationQueue = new(4);
    [ThreadStatic]
    private static OperationQueue? unitTestOperationQueue;
    private static IRequestCache? requestCache;
    [ThreadStatic]
    private static IRequestCache? unitTestRequestCache;
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
        speculative = new RateLimitedHttpMessageHandler(innerHandler, Priority.Speculative, 0, 1048576 * 5);
        userInitiated = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, 0);
        background = new RateLimitedHttpMessageHandler(innerHandler, Priority.Background, 0);
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

    internal static void CreateDefaultInstances(IReadonlyDependencyResolver? current)
    {
        // This method is just here to force the static constructor to run
        Current = current;
    }

    private static IReadonlyDependencyResolver GetCurrent()
    {
        return Current ??= Locator.Current;
    }
}
