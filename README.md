## Fusillade: An opinionated HTTP library for Mobile Development 

Fusillade helps you to write more efficient code in mobile and desktop
applications written in C#. Its design goals and feature set are inspired by
[Volley](http://arnab.ch/blog/2013/08/asynchronous-http-requests-in-android-using-volley/)
as well as [Picasso](http://square.github.io/picasso/).

### What even does this do for me?

Fusillade is a set of HttpMessageHandlers (i.e. "drivers" for HttpClient) that
make your mobile applications more efficient and responsive:

* **Auto-deduplication of requests** - if every instance of your TweetView
  class requests the same avatar image, Fusillade will only do *one* request
  and give the result to every instance.

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

Then, create an `HttpClient` with the given handler:

```cs
var client = new HttpClient(NetCache.UserInitiated);
var response = await client.GetAsync("http://httpbin.org/get");
var str = await client.Content.ReadAsStringAsync();

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

### How do I use this with ModernHttpClient?

Add this line to a **static constructor** of your app's startup class:

```cs
using Splat;

// Android
Locator.CurrentMutable.Register(() => new OkHttpNetworkHandler(), typeof(HttpMessageHandler));

// iOS
Locator.CurrentMutable.Register(() => new NSUrlSessionHandler(), typeof(HttpMessageHandler));
```

### Statics? That sucks! I like $OTHER_THING! Your priorities suck, I want to come up with my own scheme!

`NetCache` is just a nice pre-canned default, the interesting code is in a
class called `RateLimitedHttpMessageHandler`. You can create it explicitly and
configure it as-needed.

### What's with the name?

The word 'Fusillade' is a synonym for Volley :)
