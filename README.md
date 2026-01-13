# Service Locator Pattern for Unity

A robust, flexible Service Locator implementation for Unity that provides centralized service management with support for multiple lifetime scopes, automatic service discovery, and both synchronous and asynchronous initialization.

## Table of Contents
- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
- [Service Lifetimes](#service-lifetimes)
- [Usage Examples](#usage-examples)
- [API Reference](#api-reference)
- [Best Practices](#best-practices)
- [Advanced Topics](#advanced-topics)

## Overview

The Service Locator pattern provides a centralized registry for accessing services throughout your application. This implementation offers:

- **Automatic service discovery** via reflection and attributes
- **Three lifetime scopes**: Global, ScopedContext, and Scene
- **Async/sync initialization** support
- **Type-safe service retrieval**
- **Proper cleanup** between Unity editor play sessions
- **Event-driven initialization** notifications

## Features

✅ **Multiple Lifetime Scopes** - Services can be Global (app lifetime), ScopedContext (custom contexts), or Scene-specific  
✅ **Automatic Discovery** - Services are automatically discovered and registered via `[ServiceAttribute]`  
✅ **Flexible Initialization** - Support for both synchronous and asynchronous service initialization  
✅ **State Tracking** - Built-in configuration state tracking (Uninitialized, InProgress, Success, Failed)  

## Architecture

### Core Components

```
ServiceLocator/
├── ServiceLocator.cs              # Central static access point
├── Services/
│   ├── IService.cs                # Core service interface
│   └── ServiceAttribute.cs        # Attribute for service discovery
└── Containers/
    ├── BaseServiceContainer.cs    # Abstract base for containers
    ├── ServiceContainer.cs        # Auto-discovery container
    └── SceneServiceContainer.cs   # Manual registration container
```

### Component Responsibilities

#### **ServiceLocator** (Static Class)
- Central access point for all services
- Manages service discovery and container lifecycle
- Provides retrieval methods for all lifetime scopes
- Handles cleanup between play sessions

#### **IService** (Interface)
- Core interface all services must implement
- Provides initialization lifecycle methods
- Tracks configuration state
- Supports both sync and async initialization

#### **BaseServiceContainer** (Abstract Class)
- Base class for all container implementations
- Manages service storage and retrieval
- Handles disposal and cleanup
- Defines `Lifetime` and `Context` enums

#### **ServiceContainer** (Class)
- Auto-discovers and instantiates services via reflection
- Manages Global and ScopedContext services
- Handles automatic initialization
- Supports async initialization workflows

#### **SceneServiceContainer** (Class)
- Manual registration for Scene-lifetime services
- Services manage their own initialization
- Allows per-scene service instances

#### **ServiceAttribute** (Attribute)
- Marks classes for automatic service discovery
- Specifies lifetime scope and service interface type
- Enables reflection-based registration

## Getting Started

### 1. Define a Service Interface

```csharp
public interface IMyService : IService
{
    void DoSomething();
}
```

### 2. Implement the Service

```csharp
[Service(Lifetime.Global, typeof(IMyService))]
public class MyService : IMyService
{
    public bool IsAsyncInit => false;

    protected override bool InitializeService()
    {
        // Synchronous initialization logic
        Debug.Log("MyService initialized!");
        return true;
    }

    public void DoSomething()
    {
        Debug.Log("Doing something!");
    }
}
```

### 3. Retrieve and Use the Service

```csharp
public class GameController : MonoBehaviour
{
    private IMyService _myService;

    void Start()
    {
        // Fetch the service
        _myService = ServiceLocator.FetchGlobalService<IMyService>();
        
        // Use the service
        _myService?.DoSomething();
    }
}
```

## Service Lifetimes

### Global Lifetime
Services that live for the entire application lifetime.

```csharp
[Service(Lifetime.Global, typeof(IAnalyticsService))]
public class AnalyticsService : IAnalyticsService
{
    // Initialized at game start, persists until app closes
}
```

**Use Cases:**
- Analytics services
- Save/Load managers
- Audio managers
- Network managers

### ScopedContext Lifetime
Services scoped to specific game contexts (e.g., MainMenu, Gameplay, Settings).

```csharp
// First, extend the Context enum in BaseServiceContainer.cs:
public enum Context
{
    None,
    MainMenu,
    Gameplay,
    Settings,
    _Count,
}

// Then create a scoped service:
[Service(Context.Gameplay, typeof(IPlayerStatsService))]
public class PlayerStatsService : IPlayerStatsService
{
    // Only exists during Gameplay context
}

// Discover services when entering context:
ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, Context.Gameplay);

// Retrieve the service:
var stats = ServiceLocator.FetchContextService<IPlayerStatsService>();

// Clean up when leaving context:
ServiceLocator.PurgeContainer(Lifetime.ScopedContext, Context.Gameplay);
```

**Use Cases:**
- Menu-specific services
- Gameplay-only managers
- Context-specific UI controllers

### Scene Lifetime
Services that are manually registered per scene.

```csharp
[Service(Lifetime.Scene, typeof(ILevelService))]
public class LevelService : MonoBehaviour, ILevelService
{
    public bool IsAsyncInit => false;

    void Awake()
    {
        // Refresh scene container when scene loads
        ServiceLocator.RefreshSceneServiceContainer();
        
        // Manually register this service
        ServiceLocator.RegisterSceneService<ILevelService>(this);
    }

    protected override bool InitializeService()
    {
        // Scene-specific initialization
        return true;
    }
}

// Retrieve in other scene objects:
var levelService = ServiceLocator.FetchSceneService<ILevelService>();
```

**Use Cases:**
- Level-specific managers
- Scene controllers
- Per-scene configuration services

## Usage Examples

### Asynchronous Service Initialization

```csharp
[Service(Lifetime.Global, typeof(IDataService))]
public class DataService : IDataService
{
    public bool IsAsyncInit => true;

    protected override async Task<bool> InitializeServiceAsync()
    {
        // Simulate loading data from disk or network
        await Task.Delay(1000);
        
        Debug.Log("Data loaded successfully!");
        return true;
    }
}

// Wait for initialization before using:
public class GameStarter : MonoBehaviour
{
    async void Start()
    {
        var dataService = ServiceLocator.FetchGlobalService<IDataService>();
        
        // Wait up to 10 seconds for initialization
        bool initialized = await dataService.AwaitInitialization(maxWait: 10f);
        
        if (initialized)
        {
            Debug.Log("Service ready!");
        }
        else
        {
            Debug.LogError("Service failed to initialize!");
        }
    }
}
```

### Subscribing to Container Initialization

```csharp
void Start()
{
    // Subscribe to global container initialization
    ServiceLocator.GlobalServicesInitialized += OnGlobalServicesReady;
    
    // Subscribe to context-specific initialization
    ServiceLocator.SubscribeToContextServiceSetup(Context.Gameplay, OnGameplayServicesReady);
}

void OnGlobalServicesReady()
{
    Debug.Log("All global services initialized!");
}

void OnGameplayServicesReady()
{
    Debug.Log("Gameplay services initialized!");
}

void OnDestroy()
{
    ServiceLocator.GlobalServicesInitialized -= OnGlobalServicesReady;
    ServiceLocator.UnsubscribeToContextServiceSetup(Context.Gameplay, OnGameplayServicesReady);
}
```

### Safe Service Retrieval with TryGet

```csharp
void Start()
{
    // Safe retrieval - returns false if service not found
    if (ServiceLocator.TryGetGlobalService<IAudioService>(out var audioService))
    {
        audioService.PlayMusic("MainTheme");
    }
    else
    {
        Debug.LogWarning("Audio service not available!");
    }
}
```

### Checking Container Initialization Status

```csharp
void Update()
{
    if (!ServiceLocator.IsGlobalContainerInitialized)
    {
        // Show loading screen
        return;
    }
    
    if (ServiceLocator.IsContextContainerInitialized(Context.Gameplay))
    {
        // Gameplay services ready
    }
}
```

## API Reference

### ServiceLocator Static Methods

#### Service Retrieval
```csharp
// Fetch services (throws if not found)
TService FetchGlobalService<TService>()
TService FetchContextService<TService>()
TService FetchSceneService<TService>()

// Try get services (returns false if not found)
bool TryGetGlobalService<TService>(out TService service)
bool TryGetContextService<TService>(out TService service)
bool TryGetSceneService<TService>(out TService service)
```

#### Container Management
```csharp
void DiscoverServicesOfLifetime(Lifetime lifetime, Context context = Context.None)
void PurgeContainer(Lifetime lifetime, Context context = Context.None)
void RefreshSceneServiceContainer()
void RegisterSceneService<TService>(TService service)
```

#### Initialization Status
```csharp
bool IsGlobalContainerInitialized { get; }
bool IsSceneContainerInitialized { get; }
bool IsContextContainerInitialized(Context context)
```

#### Events
```csharp
event Action GlobalServicesInitialized
void SubscribeToContextServiceSetup(Context context, Action onSetup)
void UnsubscribeToContextServiceSetup(Context context, Action onSetup)
```

### IService Interface

```csharp
// Properties
bool IsAsyncInit { get; }
ConfigurationState ConfigState { get; }

// Methods
void Initialize()
Task InitializeAsync()
Task<bool> AwaitInitialization(float maxWait = 5f)

// Override in implementations
protected virtual bool InitializeService()
protected virtual Task<bool> InitializeServiceAsync()
```

### ConfigurationState Enum

```csharp
public enum ConfigurationState
{
    Uninitialized,  // Service not yet initialized
    InProgress,     // Async initialization in progress
    Failed,         // Initialization failed
    Success,        // Successfully initialized
}
```

### Lifetime Enum

```csharp
public enum Lifetime
{
    None,           // Invalid/unset
    Scene,          // Per-scene lifetime
    ScopedContext,  // Custom context lifetime
    Global,         // Application lifetime
}
```

## Best Practices

### ✅ DO

- **Use interfaces** for service contracts to enable testability and flexibility
- **Initialize services early** in the application lifecycle
- **Dispose properly** by implementing `IDisposable` for services with resources
- **Use appropriate lifetimes** - Global for app-wide, Context for game states, Scene for level-specific

### ❌ DON'T

- **Avoid circular dependencies** between services. Resolvable by awaiting initialization using `AwaitInitialization` 
- **Don't store service references** across scene loads for Scene-lifetime services
- **Don't forget to purge context containers** when leaving game states
- **Don't mix lifetime scopes** - keep service dependencies within the same or broader scope
- **Don't use ServiceLocator in static constructors** - timing issues may occur

## Usage Tips

### Custom Context Scopes

Extend the `Context` enum in `BaseServiceContainer.cs` to define your own contexts:

```csharp
public enum Context
{
    None, // Reserved, do not use
    MainMenu,
    Gameplay,
    PauseMenu,
    Settings,
    Shop,
    _Count,  // Keep this last!
}
```

### Service Dependencies

Services can depend on other services, but be mindful of initialization order:

```csharp
[Service(Lifetime.Global, typeof(IGameService))]
public class GameService : IGameService
{
    private IAudioService _audioService;
    
    public bool IsAsyncInit => true;
    
    protected override async Task<bool> InitializeServiceAsync()
    {
        // Wait for audio service to initialize first
        _audioService = ServiceLocator.FetchGlobalService<IAudioService>();
        await _audioService.AwaitInitialization();
        
        // Now safe to use audio service
        return true;
    }
}
```

### Testing with Service Locator

For unit testing, consider creating mock services:

```csharp
// Test setup
[Service(Lifetime.Global, typeof(IAnalyticsService))]
public class MockAnalyticsService : IAnalyticsService
{
    public List<string> TrackedEvents = new();
    
    public void TrackEvent(string eventName)
    {
        TrackedEvents.Add(eventName);
    }
}

// In tests
[Test]
public void TestEventTracking()
{
    var analytics = ServiceLocator.FetchGlobalService<IAnalyticsService>() as MockAnalyticsService;
    
    // Perform action that should track event
    SomeGameAction();
    
    Assert.Contains("GameStarted", analytics.TrackedEvents);
}
```

### Performance Considerations

- **Service discovery** happens once at startup via reflection - minimal runtime overhead
- **Service retrieval** uses dictionary lookups - O(1) performance
- **Async initialization** doesn't block the main thread
- **Container disposal** properly cleans up resources

### Editor Play Session Cleanup

The system automatically cleans up between editor play sessions using:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
public static void GameStart()
{
    CleanupStatics();  // Clears all containers and state
    // ... reinitialize
}
```

This ensures no state leakage between play sessions in the Unity Editor.

## Troubleshooting

### Service Not Found
**Problem:** `NullReferenceException` when fetching service  
**Solution:** 
- Ensure service class has `[ServiceAttribute]`
- Check that service implements `IService`
- Verify container for that lifetime has been initialized
- Use `TryGet` methods for safer retrieval

### Context Services Not Available
**Problem:** Context service returns null  
**Solution:**
- Call `DiscoverServicesOfLifetime(Lifetime.ScopedContext, yourContext)` first
- Ensure you're using the correct `Context` enum value
- Verify the service attribute specifies the correct context

## License

This Service Locator implementation is part of the QBS framework.

---

**Version:** 1.1
**Last Updated:** January 2026  
**Unity Version:** 6000.0.62f1
