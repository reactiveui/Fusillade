[![NuGet Stats](https://img.shields.io/nuget/v/fusillade.svg)](https://www.nuget.org/packages/fusillade) ![Build](https://github.com/reactiveui/Fusillade/workflows/Build/badge.svg) [![Code Coverage](https://codecov.io/gh/reactiveui/fusillade/branch/main/graph/badge.svg)](https://codecov.io/gh/reactiveui/fusillade)

<br />
<a href="https://github.com/reactiveui/fusillade">
  <img width="120" height="120" src="https://raw.githubusercontent.com/reactiveui/styleguide/master/logo_fusillade/main.png">
</a>

## Fusillade: An opinionated HTTP library for .NET apps

Fusillade helps you write efficient, resilient networked apps by composing `HttpMessageHandler` instances for `HttpClient`. It focuses on:

- Request de-duplication for relevant HTTP methods
- Priority-aware concurrency limiting through Punchclock
- Request prioritization for predictable UX
- Speculative background fetching with byte-budget limits
- Optional response caching and offline replay

Design inspirations include Android's Volley and Picasso.

Supported targets: `net8.0`, `net9.0`, `net10.0`, `net11.0`, `net462`, `net472`, `net48`, and `net481`.

## V6.0.x Breaking Changes

Fusillade `6.0.x` moved the primary package from a System.Reactive-based dependency stack to `ReactiveUI.Primitives`.

The main `fusillade` package:

- Uses `ReactiveUI.Primitives` and the lean `punchclock` package.
- Does not require `System.Reactive`.
- Keeps the public Fusillade API handler-first: `HttpMessageHandler`, `HttpClient`, `Task`, `CancellationToken`, and `IRequestCache`.
- Keeps the namespace as `Fusillade`.

The new `fusillade.reactive` package:

- Uses `ReactiveUI.Primitives.Reactive` and `Punchclock.Reactive`.
- Is intended for System.Reactive-first applications.
- Uses the namespace `Fusillade.Reactive`.
- Mirrors the `fusillade` API surface so Rx applications can migrate with minimal code changes.

If your application only uses `HttpClient` handlers, install `fusillade`. If your application also relies on System.Reactive conventions through the surrounding queue or reactive infrastructure, install `fusillade.reactive`.

### Package Selection

| Scenario | Package | Namespace | Queue type |
| --- | --- | --- | --- |
| New code or non-Rx applications | `fusillade` | `Fusillade` | `Punchclock.OperationQueue` |
| Existing System.Reactive applications | `fusillade.reactive` | `Fusillade.Reactive` | `Punchclock.Reactive.OperationQueue` |
| R3 applications | `fusillade` plus `ReactiveUI.Primitives` and `R3` | `Fusillade`, `ReactiveUI.Primitives.R3Bridge` | `Punchclock.OperationQueue` |

### Migration From System.Reactive To V6

For the lean `fusillade` package:

```csharp
using Fusillade;
using Punchclock;

NetCache.OperationQueue = new OperationQueue(maxConcurrency: 6);

using var client = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
var json = await client.GetStringAsync("https://example.com/api/items");
```

For the System.Reactive-compatible package:

```csharp
using Fusillade.Reactive;
using Punchclock.Reactive;
using System.Reactive.Linq;

NetCache.OperationQueue = new OperationQueue(maxConcurrency: 6);

using var client = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
IObservable<string> json = Observable.FromAsync(
    ct => client.GetStringAsync("https://example.com/api/items", ct));
```

If both packages are referenced in the same project, use aliases at the boundary to avoid namespace ambiguity:

```csharp
using LeanNetCache = Fusillade.NetCache;
using RxNetCache = Fusillade.Reactive.NetCache;

using var leanClient = new HttpClient(LeanNetCache.Background, disposeHandler: false);
using var rxClient = new HttpClient(RxNetCache.Background, disposeHandler: false);
```

### R3 Source Generator Bridge

Do not reference the ReactiveUI.Primitives R3 bridge generator package directly. The generator is packed as an analyzer by `ReactiveUI.Primitives` and emits bridge methods when the consuming project references the required R3 symbols. Add a direct `ReactiveUI.Primitives` package reference in the consuming project so the analyzer is present.

```bash
dotnet add package fusillade
dotnet add package ReactiveUI.Primitives
dotnet add package R3
```

```csharp
using Fusillade;
using ReactiveUI.Primitives.R3Bridge;

R3.Observable<Uri> selectedUris = GetSelectedUris();

// Generated when R3 and ReactiveUI.Primitives are both referenced.
System.IObservable<Uri> primitivesUris = selectedUris.AsPrimitivesSignal();

// Convert back to R3 at a boundary if the rest of the app remains R3-first.
R3.Observable<Uri> r3Uris = primitivesUris.AsR3Observable();

using var client = new HttpClient(NetCache.Background, disposeHandler: false);
```

Keep bridge calls at package or application boundaries. Keep the internal pipeline in one reactive model after conversion.

## Install

```bash
dotnet add package fusillade
```

For System.Reactive-first applications:

```bash
dotnet add package fusillade.reactive
```

## Quick Start

Create `HttpClient` instances by selecting the right handler from `NetCache`.

```csharp
using Fusillade;

using var client = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
var json = await client.GetStringAsync("https://httpbin.org/get");
```

Available built-ins:

- `NetCache.UserInitiated`: foreground work the user is waiting for.
- `NetCache.Background`: background work that should not block UI work.
- `NetCache.Speculative`: background prefetching with a byte budget.
- `NetCache.Offline`: fetch from cache only.

By default, requests are processed four at a time through an `OperationQueue`.

## Core Ideas

### Request De-Duplication

Fusillade de-duplicates concurrent `GET`, `HEAD`, and `OPTIONS` requests for the same resource. If multiple callers request the same URL concurrently through the same `RateLimitedHttpMessageHandler`, one network request is made and the callers receive independent response instances.

```csharp
using Fusillade;

var handler = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated);
using var client = new HttpClient(handler);

var first = client.GetAsync("https://example.com/profile/42");
var second = client.GetAsync("https://example.com/profile/42");

using var firstResponse = await first;
using var secondResponse = await second;
```

### Concurrency Limiting And Prioritization

All rate-limited work is scheduled through an `OperationQueue`. The effective priority is the selected `Priority` value plus the optional `priority` offset.

```csharp
using Fusillade;
using Punchclock;

var queue = new OperationQueue(maxConcurrency: 2);

var handler = new RateLimitedHttpMessageHandler(
    handler: new HttpClientHandler(),
    basePriority: Priority.Explicit,
    priority: 500,
    maxBytesToRead: null,
    operationQueue: queue);

using var client = new HttpClient(handler);
```

Priority values:

```csharp
var explicitPriority = (int)Priority.Explicit;      // 0
var speculative = (int)Priority.Speculative;        // 10
var background = (int)Priority.Background;          // 20
var userInitiated = (int)Priority.UserInitiated;    // 100
```

### Speculative Background Fetching

Use `NetCache.Speculative` for prefetching. Reset its byte budget when a new prefetch window starts.

```csharp
using Fusillade;

NetCache.Speculative.ResetLimit(5 * 1024 * 1024);

using var prefetch = new HttpClient(NetCache.Speculative, disposeHandler: false);
_ = prefetch.GetStringAsync("https://example.com/likely-next-screen");
```

Stop further speculative work:

```csharp
NetCache.Speculative.ResetLimit(-1);
```

Clear the byte limit:

```csharp
NetCache.Speculative.ResetLimit();
```

## Public API Reference

The `fusillade.reactive` package mirrors these APIs under `Fusillade.Reactive` and uses `Punchclock.Reactive.OperationQueue` where the lean package uses `Punchclock.OperationQueue`.

### `NetCache`

`NetCache` owns the shared handlers and app-wide queue/cache configuration.

```csharp
using Fusillade;
using Punchclock;

NetCache.OperationQueue = new OperationQueue(maxConcurrency: 6);
NetCache.RequestCache = new MemoryRequestCache();

using var foreground = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
using var background = new HttpClient(NetCache.Background, disposeHandler: false);
using var speculative = new HttpClient(NetCache.Speculative, disposeHandler: false);
using var offline = new HttpClient(NetCache.Offline, disposeHandler: false);
```

Public members:

```csharp
public static LimitingHttpMessageHandler Speculative { get; set; }
public static HttpMessageHandler UserInitiated { get; set; }
public static HttpMessageHandler Background { get; set; }
public static HttpMessageHandler Offline { get; set; }
public static OperationQueue OperationQueue { get; set; }
public static IRequestCache? RequestCache { get; set; }
```

### `RateLimitedHttpMessageHandler`

`RateLimitedHttpMessageHandler` performs prioritization, de-duplication, optional byte limiting, and optional cache saving.

```csharp
using Fusillade;
using Punchclock;

var handler = new RateLimitedHttpMessageHandler(
    handler: new HttpClientHandler(),
    basePriority: Priority.Background,
    priority: 25,
    maxBytesToRead: 10 * 1024 * 1024,
    operationQueue: new OperationQueue(maxConcurrency: 4),
    cacheResultFunc: async (request, response, key, ct) =>
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await File.WriteAllBytesAsync($"{key}.bin", bytes, ct);
    });

using var client = new HttpClient(handler);
```

Generate the same cache key Fusillade uses internally:

```csharp
using Fusillade;

using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/data");
var key = RateLimitedHttpMessageHandler.UniqueKeyForRequest(request);
```

Public members:

```csharp
public RateLimitedHttpMessageHandler(
    HttpMessageHandler? handler,
    Priority basePriority,
    int priority = 0,
    long? maxBytesToRead = null,
    OperationQueue? operationQueue = null,
    Func<HttpRequestMessage, HttpResponseMessage, string, CancellationToken, Task>? cacheResultFunc = null);

public static string UniqueKeyForRequest(HttpRequestMessage request);
public override void ResetLimit(long? maxBytesToRead);
```

### `LimitingHttpMessageHandler`

`LimitingHttpMessageHandler` is the base class for handlers that can limit the total number of response bytes read.

```csharp
using Fusillade;

LimitingHttpMessageHandler handler = NetCache.Speculative;

handler.ResetLimit(1_000_000);
handler.ResetLimit();
```

Public members:

```csharp
public void ResetLimit();
public abstract void ResetLimit(long? maxBytesToRead);
```

### `IRequestCache`

Implement `IRequestCache` when you want `NetCache.RequestCache`, `RateLimitedHttpMessageHandler`, and `OfflineHttpMessageHandler` to share the same cache.

```csharp
using System.Collections.Concurrent;
using Fusillade;

public sealed class MemoryRequestCache : IRequestCache
{
    private readonly ConcurrentDictionary<string, byte[]> _responses = new();

    public async Task SaveAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        string key,
        CancellationToken ct)
    {
        if (response.Content is null)
        {
            return;
        }

        _responses[key] = await response.Content.ReadAsByteArrayAsync(ct);
    }

    public Task<byte[]?> FetchAsync(
        HttpRequestMessage request,
        string key,
        CancellationToken ct)
    {
        _responses.TryGetValue(key, out var bytes);
        return Task.FromResult<byte[]?>(bytes);
    }
}
```

Public members:

```csharp
public interface IRequestCache
{
    Task SaveAsync(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct);
    Task<byte[]?> FetchAsync(HttpRequestMessage request, string key, CancellationToken ct);
}
```

### `OfflineHttpMessageHandler`

`OfflineHttpMessageHandler` serves cached data without touching the network. It returns `200 OK` when a cached body is found and `503 Service Unavailable` when no cached body exists.

Use the shared `NetCache.RequestCache`:

```csharp
using Fusillade;

NetCache.RequestCache = new MemoryRequestCache();

using var offline = new HttpClient(NetCache.Offline, disposeHandler: false);
var cached = await offline.GetStringAsync("https://example.com/data");
```

Or pass a cache lookup directly:

```csharp
using Fusillade;

var handler = new OfflineHttpMessageHandler(
    retrieveBodyFunc: async (request, key, ct) =>
    {
        var path = Path.Combine("cache", $"{key}.bin");
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, ct)
            : null;
    });

using var offline = new HttpClient(handler);
```

Public member:

```csharp
public OfflineHttpMessageHandler(
    Func<HttpRequestMessage, string, CancellationToken, Task<byte[]?>>? retrieveBodyFunc);
```

### `Priority`

`Priority` defines the base scheduling priorities used by `RateLimitedHttpMessageHandler`.

```csharp
using Fusillade;

var userWork = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.UserInitiated);
var backgroundWork = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.Background);
var prefetchWork = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.Speculative);
var customWork = new RateLimitedHttpMessageHandler(new HttpClientHandler(), Priority.Explicit, priority: 250);
```

Public values:

```csharp
public enum Priority
{
    Explicit = 0,
    Speculative = 10,
    Background = 20,
    UserInitiated = 100,
}
```

### Splat Builder Integration

Call `CreateFusilladeNetCache` when using Splat's builder pipeline. This initializes the shared `NetCache` instances from the current resolver.

```csharp
using Splat.Builder;

var app = AppBuilder.CreateSplatBuilder().Build();
app.CreateFusilladeNetCache();
```

Public member:

```csharp
public static IAppInstance CreateFusilladeNetCache(this IAppInstance builder);
```

## Caching And Offline

Fusillade can optionally cache response bodies and replay them when offline.

There are two supported cache paths:

1. Pass `cacheResultFunc` to `RateLimitedHttpMessageHandler`.
2. Set `NetCache.RequestCache` to an `IRequestCache` implementation.

When caching through `RateLimitedHttpMessageHandler`, Fusillade buffers the response content and forwards the buffered response to your cache callback. The key is generated by `RateLimitedHttpMessageHandler.UniqueKeyForRequest(request)`.

### File-Based Cache Example

```csharp
using Fusillade;

var cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "fusillade-cache");

Directory.CreateDirectory(cacheDirectory);

var cachingHandler = new RateLimitedHttpMessageHandler(
    new HttpClientHandler(),
    Priority.UserInitiated,
    cacheResultFunc: async (request, response, key, ct) =>
    {
        var data = await response.Content.ReadAsByteArrayAsync(ct);
        var path = Path.Combine(cacheDirectory, $"{key}.bin");
        await File.WriteAllBytesAsync(path, data, ct);
    });

using var client = new HttpClient(cachingHandler);
var fresh = await client.GetStringAsync("https://httpbin.org/get");
```

```csharp
using Fusillade;

public sealed class FileRequestCache(string cacheDirectory) : IRequestCache
{
    public async Task SaveAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        string key,
        CancellationToken ct)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = Path.Combine(cacheDirectory, $"{key}.bin");
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public Task<byte[]?> FetchAsync(
        HttpRequestMessage request,
        string key,
        CancellationToken ct)
    {
        var path = Path.Combine(cacheDirectory, $"{key}.bin");
        return File.Exists(path)
            ? File.ReadAllBytesAsync(path, ct)
            : Task.FromResult<byte[]?>(null);
    }
}
```

## Usage Recipes

### Image Gallery Or Avatars

Use `RateLimitedHttpMessageHandler` for visible images and `NetCache.Background` for preloading. De-duplication prevents duplicate downloads for the same URL while the first request is still in flight.

```csharp
using Fusillade;

using var visibleImages = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
using var preloadImages = new HttpClient(NetCache.Background, disposeHandler: false);

var avatar = await visibleImages.GetByteArrayAsync("https://example.com/users/42/avatar.png");
_ = preloadImages.GetByteArrayAsync("https://example.com/users/43/avatar.png");
```

### Boot-Time Warmup

On app start or resume, reset `NetCache.Speculative` to a sensible byte budget and queue likely-next requests.

```csharp
using Fusillade;

NetCache.Speculative.ResetLimit(2 * 1024 * 1024);

using var warmup = new HttpClient(NetCache.Speculative, disposeHandler: false);
_ = warmup.GetAsync("https://example.com/bootstrap/menu");
_ = warmup.GetAsync("https://example.com/bootstrap/profile");
```

### Offline-First Views

Populate `NetCache.RequestCache` during online sessions and switch to `NetCache.Offline` when network access is unavailable.

```csharp
using Fusillade;

NetCache.RequestCache = new MemoryRequestCache();

using var online = new HttpClient(NetCache.UserInitiated, disposeHandler: false);
_ = await online.GetStringAsync("https://example.com/data");

using var offline = new HttpClient(NetCache.Offline, disposeHandler: false);
var cached = await offline.GetStringAsync("https://example.com/data");
```

## FAQ

### How many requests run at once?

The default `OperationQueue` processes four requests at a time. Override it with `NetCache.OperationQueue` or pass an explicit queue to `RateLimitedHttpMessageHandler`.

### Which methods are de-duplicated?

`GET`, `HEAD`, and `OPTIONS`.

### How are cache keys generated?

`RateLimitedHttpMessageHandler.UniqueKeyForRequest(request)` generates keys from the request URI, method, selected headers, referrer, user agent, and authorization header. Treat keys as implementation details; persist and reuse the key passed to your cache callback.

### Can I cancel a request?

Use `CancellationToken` with normal `HttpClient` APIs. If several callers share a de-duplicated request, the underlying request is cancelled only after every joined caller has cancelled.

### Should I dispose `HttpClient` instances that use `NetCache` handlers?

Use `new HttpClient(NetCache.UserInitiated, disposeHandler: false)` for shared `NetCache` handlers. This lets you dispose the client without disposing the process-wide handler.

## Contribute

Fusillade is developed under an OSI-approved open source license, making it freely usable and distributable, even for commercial use. We welcome contributors of all experience levels.

- Answer questions on StackOverflow: https://stackoverflow.com/questions/tagged/fusillade
- Share knowledge and mentor the next generation of developers
- Donations: https://reactiveui.net/donate and Corporate Sponsorships: https://reactiveui.net/sponsorship
- Ask your employer to support open-source: https://github.com/github/balanced-employee-ip-agreement
- Improve documentation and examples
- Contribute features and bugfixes via PRs

## What's With The Name?

"Fusillade" is a synonym for Volley.
