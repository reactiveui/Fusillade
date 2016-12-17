## Fusillade: An opinionated HTTP library for Mobile Development

Fusillade helps you write efficient code in mobile and desktop
applications, written in C#. Its design goals and feature set are inspired by
[Volley](http://arnab.ch/blog/2013/08/asynchronous-http-requests-in-android-using-volley/)
as well as [Picasso](http://square.github.io/picasso/).

### What does this do for me?

Fusillade is a set of HttpMessageHandlers (i.e. "drivers" for HttpClient) that
make your mobile applications more efficient and responsive:

* **Auto-deduplication of relevant requests** - if every instance of your TweetView
  class requests the same avatar image, Fusillade will only do *one* request
  and give the result to every instance. All `GET`, `HEAD`, and `OPTIONS` requests are deduplicated.

* **Request Limiting** - Requests are always dispatched 4 at a time (the
  Volley default) - issue lots of requests without overwhelming the network
  connection.

* **Request Prioritization** - background requests should run at a lower
  priority than requests initiated by the user, but actually *implementing*
  this is quite difficult. With a few changes to your app, you can hint to
  Fusillade which requests should skip to the front of the queue.

* **Speculative requests** - On page load, many apps will try to speculatively
  cache data (i.e. try to pre-download data that the user *might* click on).
  Marking requests as speculative will allow requests until a certain data
  limit is reached, then cancel future requests (i.e. "Keep downloading data
  in the background until we've got 5MB of cached data")

### How do I use it?

The easiest way to interact with Fusillade is via a class called `NetCache`,
which has a number of built-in scenarios:

```cs
public static class NetCache
{
    // Use to fetch data into a cache when a page loads. Expect that
    // these requests will only get so far then give up and start failing
    public static HttpMessageHandler Speculative { get; set; }

    // Use for network requests that are running in the background
    public static HttpMessageHandler Background { get; set; }

    // Use for network requests that are fetching data that the user is
    // waiting on *right now*
    public static HttpMessageHandler UserInitiated { get; set; }
}
```

To use them, just create an `HttpClient` with the given handler:

```cs
var client = new HttpClient(NetCache.UserInitiated);
var response = await client.GetAsync("http://httpbin.org/get");
var str = await response.Content.ReadAsStringAsync();

Console.WriteLine(str);
```

### Where does it work?

Everywhere! Fusillade is a Portable Library, it works on:

* Xamarin.Android
* Xamarin.iOS
* Xamarin.Mac
* Windows Desktop apps
* WinRT / Windows Phone 8.1 apps
* Windows Phone 8

### More on speculative requests

Generally, on a mobile app, you'll want to *reset* the Speculative limit every
time the app resumes from standby. How you do this depends on the platform,
but in that callback, you need to call:

```cs
NetCache.Speculative.ResetLimit(1048576 * 5/*MB*/);
```

### Offline Support

Fusillade can optionally cache responses that it sees, then play them back to
you when your app is offline (or you just want to speed up your app by fetching
cached data). Here's how to set it up:

* Implement the IRequestCache interface:

```cs
public interface IRequestCache
{
    /// <summary>
    /// Implement this method by saving the Body of the response. The
    /// response is already downloaded as a ByteArrayContent so you don't
    /// have to worry about consuming the stream.
    /// <param name="request">The originating request.</param>
    /// <param name="response">The response whose body you should save.</param>
    /// <param name="key">A unique key used to identify the request details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completion.</returns>
    Task Save(HttpRequestMessage request, HttpResponseMessage response, string key, CancellationToken ct);

    /// <summary>
    /// Implement this by loading the Body of the given request / key.
    /// </summary>
    /// <param name="request">The originating request.</param>
    /// <param name="key">A unique key used to identify the request details,
    /// that was given in Save().</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Body of the given request, or null if the search
    /// completed successfully but the response was not found.</returns>
    Task<byte[]> Fetch(HttpRequestMessage request, string key, CancellationToken ct);
}
```

* Set an instance to `NetCache.RequestCache`, and make some requests:

```cs
NetCache.RequestCache = new MyCoolCache();

var client = new HttpClient(NetCache.UserInitiated);
await client.GetStringAsync("https://httpbin.org/get");
```

* Now you can use `NetCache.Offline` to get data even when the Internet is disconnected:

```cs
// This will never actually make an HTTP request, it will either succeed via
// reading from MyCoolCache, or return an HttpResponseMessage with a 503 Status code.
var client = new HttpClient(NetCache.Offline);
await client.GetStringAsync("https://httpbin.org/get");
```

### How do I use this with ModernHttpClient?

Add this line to a **static constructor** of your app's startup class:

```cs
using Splat;

Locator.CurrentMutable.RegisterConstant(new NativeMessageHandler(), typeof(HttpMessageHandler));
```

### Statics? That sucks! I like $OTHER_THING! Your priorities suck, I want to come up with my own scheme!

`NetCache` is just a nice pre-canned default, the interesting code is in a
class called `RateLimitedHttpMessageHandler`. You can create it explicitly and
configure it as-needed.

### What's with the name?

The word 'Fusillade' is a synonym for Volley :)
