// Copyright (c) 2016-2026 ReactiveUI and Contributors. All rights reserved.
// ReactiveUI and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using Splat;
using Splat.Builder;

namespace HttpCacheExamples;

/// <summary>Shares a foreground download and reads its saved body offline.</summary>
internal static class Program
{
    /// <summary>Identifies the local service's profile.</summary>
    private static readonly Uri Profile = new("https://people.example/profile");

    /// <summary>Exercises the shared handlers with a local transport.</summary>
    /// <returns>Completion after the cached body and transfer budget have been checked.</returns>
    internal static async Task Main()
    {
        const int TimeoutSeconds = 30;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var resolver = new ModernDependencyResolver();
        _ = new AppBuilder(resolver).Build().CreateFusilladeNetCache();
        using var queue = new OperationQueue(1);
        NetCache.OperationQueue = queue;
        var cache = new ProfileCache();
        NetCache.RequestCache = cache;
        using var transport = new ProfileTransport();
        using var foreground = new RateLimitedHttpMessageHandler(transport, Priority.UserInitiated, operationQueue: queue);
        NetCache.UserInitiated = foreground;
        using var client = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
        Task<string> first;
        Task<string> second;
        using (queue.PauseQueue())
        {
            first = client.GetStringAsync(Profile, deadline.Token);
            second = client.GetStringAsync(Profile, deadline.Token);
        }

        string[] names = await Task.WhenAll(first, second);
        if (transport.Sends != 1 || names[0] != names[1])
        {
            throw new InvalidOperationException("Both callers should share one download.");
        }

        using var offlineHandler = new OfflineHttpMessageHandler(cache.FetchAsync);
        NetCache.Offline = offlineHandler;
        using var offline = new HttpClient(NetCache.Offline, disposeHandler: false);
        await ReadOfflineAsync(cache, offline, deadline.Token);

        using var speculative = new RateLimitedHttpMessageHandler(new ProfileTransport(), Priority.Speculative, maxBytesToRead: 1);
        using var budget = new PrefetchBudgetHandler(speculative);
        NetCache.Speculative = budget;
        using var prefetch = new HttpClient(NetCache.Speculative, disposeHandler: false);
        await LimitPrefetchAsync(prefetch, deadline.Token);
        if (budget.ConfiguredBudget != 1)
        {
            throw new InvalidOperationException("The prefetch budget should report the limit set after resuming.");
        }

        using var background = new RateLimitedHttpMessageHandler(
            new ProfileTransport(),
            Priority.Explicit,
            priority: (int)Priority.Background,
            cacheResultFunc: cache.SaveAsync);
        using var backgroundBudget = new PrefetchBudgetHandler { InnerHandler = background };
        backgroundBudget.ResetLimit();
        NetCache.Background = backgroundBudget;
        using var refresh = new HttpClient(NetCache.Background, disposeHandler: false);
        _ = await refresh.GetStringAsync(Profile, deadline.Token);
    }

    /// <summary>Checks both a cache hit and a missing entry.</summary>
    /// <param name="cache">The cache populated by the foreground handler.</param>
    /// <param name="offline">The client that only reads saved bodies.</param>
    /// <param name="token">Cancels the cache reads.</param>
    /// <returns>Completion after the offline replies have been checked.</returns>
    private static async Task ReadOfflineAsync(ProfileCache cache, HttpClient offline, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Profile);
        string key = RateLimitedHttpMessageHandler.UniqueKeyForRequest(request);
        byte[]? saved = await cache.FetchAsync(request, key, token);
        var name = await offline.GetStringAsync(Profile, token);
        using var miss = await offline.GetAsync(new Uri(Profile, "/unknown"), token);
        if (saved is not null && name == "Ada" && miss.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        throw new InvalidOperationException("Only the saved profile should be available offline.");
    }

    /// <summary>Spends a speculative byte budget and replenishes it for another read.</summary>
    /// <param name="client">The client backed by the limited handler.</param>
    /// <param name="token">Cancels the local HTTP calls.</param>
    /// <returns>Completion after the budget is exhausted and reset.</returns>
    private static async Task LimitPrefetchAsync(HttpClient client, CancellationToken token)
    {
        _ = await client.GetStringAsync(Profile, token);
        try
        {
            _ = await client.GetStringAsync(Profile, token);
            throw new InvalidOperationException("A spent budget should cancel the next read.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            NetCache.Speculative.ResetLimit();
        }

        _ = await client.GetStringAsync(Profile, token);
        NetCache.Speculative.ResetLimit(1);
    }
}
