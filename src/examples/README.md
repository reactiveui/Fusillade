# Download a profile and read it offline

Run either sample from `src`. Both use a local HTTP handler and an in-memory cache.

```console
dotnet run --project examples/HttpCache/HttpCache.csproj --framework net11.0
dotnet run --project examples/ReactiveHttpCache/ReactiveHttpCache.csproj --framework net11.0
```

Use `net10.0` to run either sample on .NET 10. The first project uses Fusillade and
the second uses Fusillade.Reactive. They compile the same scenario against their
respective packages.

- [Program.cs](HttpCache/Program.cs) configures the shared queue and handlers, combines two concurrent reads into one download, checks an offline hit and miss, and exhausts and resets a speculative byte budget.
- [ProfileCache.cs](HttpCache/ProfileCache.cs) implements the request cache used by both the foreground and offline clients. The background client supplies the save callback directly.
- [ProfileTransport.cs](HttpCache/ProfileTransport.cs) counts sends and returns the profile without opening a socket.
- [PrefetchBudgetHandler.cs](HttpCache/PrefetchBudgetHandler.cs) extends the limiting handler to report the budget an app last set. It supports supplying the inner limiter at construction or when assembling the handler pipeline.
