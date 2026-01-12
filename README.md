# QBS Service Locator for Unity

A robust service locator pattern implementation for Unity that provides dependency management across different lifetime scopes with support for both synchronous and asynchronous initialization.

## Features

- **Multiple Lifetime Scopes**: Global, Scene, and Scoped Context lifetimes
- **Async/Sync Initialization**: Services can be initialized synchronously or asynchronously
- **Automatic Discovery**: Services are automatically discovered via reflection at runtime
- **Type-Safe Service Retrieval**: Generic methods for fetching services with compile-time type safety
- **Initialization State Tracking**: Monitor service initialization progress and success
- **Event-Driven**: Subscribe to container initialization events
- **Disposable Containers**: Proper cleanup and disposal of services

## Core Components

### Service Lifetimes

Services can have three different lifetimes:

- **Global**: Lives for the entire application lifetime, initialized at game start
- **Scene**: Lives for the duration of a scene, manually registered by MonoBehaviours
- **ScopedContext**: Lives within a specific context (e.g., gameplay, menu), created and destroyed as needed

### Configuration States

Services track their initialization state through the `ConfigurationState` enum:

- `Uninitialized`: Service has not started initialization
- `InProgress`: Service is currently initializing (async only)
- `Failed`: Service initialization failed
- `Success`: Service initialized successfully

## Getting Started

### 1. Define a Service

Implement the `IService` interface and mark your service with the `ServiceAttribute`:

```csharp
using QBS.ServiceLocator;

// Global service example
[Service(Lifetime.Global, typeof(IAnalyticsService))]
public class AnalyticsService : IService
{
    public bool IsAsyncInit => false;
    public ConfigurationState ConfigState { get; set; }

    protected bool InitializeService()
    {
        // Your initialization logic here
        return true;
    }
}

// Async service example
[Service(Lifetime.Global, typeof(INetworkService))]
public class NetworkService : IService
{
    public bool IsAsyncInit => true;
    public ConfigurationState ConfigState { get; set; }

    protected async Task<bool> InitializeServiceAsync()
    {
        // Your async initialization logic
        await Task.Delay(1000);
        return true;
    }
}

// Scene service example
[Service(Lifetime.Scene, typeof(IPlayerService))]
public class PlayerService : MonoBehaviour, IService
{
    public bool IsAsyncInit => false;
    public ConfigurationState ConfigState { get; set; }

    private void Awake()
    {
        ServiceLocator.RegisterSceneService<IPlayerService>(this);
        Initialize();
    }
}
```

### 2. Define Custom Contexts (Optional)

For scoped context services, extend the `Context` enum:

```csharp
public enum Context
{
    None,
    Gameplay,
    MainMenu,
    _Count, // Keep this as the last entry
}
```

Then create scoped services:

```csharp
[Service(Context.Gameplay, typeof(IGameplayService))]
public class GameplayService : IService
{
    public bool IsAsyncInit => false;
    public ConfigurationState ConfigState { get; set; }
}
```

### 3. Initialize Service Locator

The service locator automatically initializes global services at game start via `[RuntimeInitializeOnLoadMethod]`. To use context-based services:

```csharp
// Discover and initialize services for a specific context
ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, Context.Gameplay);

// Refresh scene services when loading a new scene
ServiceLocator.RefreshSceneServiceContainer();
```

### 4. Retrieve Services

```csharp
// Fetch global service
var analytics = ServiceLocator.FetchGlobalService<IAnalyticsService>();

// Fetch context service
var gameplay = ServiceLocator.FetchContextService<IGameplayService>();

// Fetch scene service
var player = ServiceLocator.FetchSceneService<IPlayerService>();
```

## Advanced Usage

### Awaiting Service Initialization

Services provide a method to wait for initialization to complete:

```csharp
var networkService = ServiceLocator.FetchGlobalService<INetworkService>();
bool initialized = await networkService.AwaitInitialization(maxWait: 5f);

if (initialized)
{
    // Service is ready to use
}
```

### Subscribing to Container Events

```csharp
// Subscribe to global container initialization
ServiceLocator.GlobalServicesInitialized += OnGlobalServicesReady;

// Subscribe to context container initialization
ServiceLocator.SubscribeToContextServiceSetup(Context.Gameplay, OnGameplayServicesReady);

// Unsubscribe
ServiceLocator.UnsubscribeToContextServiceSetup(Context.Gameplay, OnGameplayServicesReady);
```

### Checking Container Status

```csharp
bool globalReady = ServiceLocator.IsGlobalContainerInitialized;
bool sceneReady = ServiceLocator.IsSceneContainerInitialized;
bool contextReady = ServiceLocator.IsContextContainerInitialized(Context.Gameplay);
```

### Container Cleanup

```csharp
// Purge specific containers
ServiceLocator.PurgeContainer(Lifetime.ScopedContext, Context.Gameplay);
ServiceLocator.PurgeContainer(Lifetime.Scene);
ServiceLocator.PurgeContainer(Lifetime.Global);
```

## Architecture Overview

### IService Interface

All services must implement `IService`, which provides:

- Initialization support (sync and async)
- Configuration state tracking
- Awaitable initialization with timeout

### Service Containers

**ServiceContainer**: Manages Global and ScopedContext services. Automatically creates service instances using reflection and initializes them.

**SceneServiceContainer**: Manages Scene-scoped services. Services register themselves manually (typically in MonoBehaviour lifecycle methods).

### Service Discovery

The service locator uses reflection to discover all types marked with `[ServiceAttribute]` across all loaded assemblies at runtime. Services are instantiated using parameterless constructors.

## Best Practices

1. **Interface-based services**: Define an interface for each service and reference it in the `ServiceAttribute`
2. **Parameterless constructors**: Ensure services have a parameterless constructor for automatic instantiation
3. **Async for I/O**: Use `IsAsyncInit = true` for services that perform network calls, file I/O, or other time-consuming operations
4. **Scene service registration**: Register scene services in `Awake()` before they're needed
5. **Dispose pattern**: Implement `IDisposable` on services that need cleanup
6. **Context lifecycle**: Call `DiscoverServicesOfLifetime` when entering a context and `PurgeContainer` when exiting

## Notes

- Services are created using `Activator.CreateInstance`, so they must have a parameterless constructor
- Scene services handle their own initialization; the container only tracks registration
- Async service initialization doesn't block the main thread
- The service locator automatically cleans up on domain reload in the Unity Editor

## License

This service locator implementation is part of the QBS namespace and is designed specifically for Unity projects.
