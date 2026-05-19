# QBS Service Locator — Usage Guide

A reflection-driven Service Locator for Unity with automatic service discovery, three lifetime scopes, and first-class support for async initialization.

## Table of Contents

- [Overview](#overview)
- [Core Concepts](#core-concepts)
- [Global Services](#global-services)
- [ScopedContext Services](#scopedcontext-services)
- [Scene Services](#scene-services)
- [Async Initialization](#async-initialization)
- [Container Events](#container-events)
- [Safe Retrieval](#safe-retrieval)
- [Initialization Status](#initialization-status)
- [Disposal](#disposal)
- [API Reference](#api-reference)
- [Best Practices](#best-practices)

---

## Overview

The Service Locator is initialized automatically at `SubsystemRegistration`. Global services are discovered and started immediately. ScopedContext and Scene containers are created on demand by your own code when entering the appropriate game state or scene.

---

## Core Concepts

### IService

Every service must implement `IService`. It provides initialization lifecycle hooks, state tracking, and disposal.

```csharp
public interface IMyService : IService
{
    void DoWork();
}

[ServiceAttribute(Lifetime.Global, typeof(IMyService))]
public class MyService : IMyService
{
    // true  → InitializeServiceAsync is called
    // false → InitializeService is called
    public bool IsAsyncInit => false;

    protected override bool InitializeService()
    {
        // Return false to signal initialization failure.
        return true;
    }

    public void DoWork() { }

    protected override void DisposeService()
    {
        // Optional: clean up resources here.
        // Do NOT override IDisposable.Dispose — override this instead.
    }
}
```

### ServiceAttribute

Marks a class for automatic discovery. The attribute specifies the **lifetime scope** and the **interface type** under which the service is registered.

```csharp
// Global or Scene lifetime — pass the Lifetime enum value
[ServiceAttribute(Lifetime.Global, typeof(IMyService))]

// ScopedContext lifetime — pass an int context constant (implicit conversion to Context)
[ServiceAttribute(GameContexts.Gameplay, typeof(IMyService))]
```

### ConfigurationState

```csharp
public enum ConfigurationState
{
    Uninitialized,  // not yet started
    InProgress,     // async init running
    Failed,         // initialization returned false or threw
    Success,        // ready to use
}
```

---

## Global Services

Global services are auto-discovered and initialized once at application startup. They live for the entire session.

```csharp
[ServiceAttribute(Lifetime.Global, typeof(ISaveService))]
public class SaveService : ISaveService
{
    public bool IsAsyncInit => false;

    protected override bool InitializeService()
    {
        // Load persistent data, etc.
        return true;
    }
}
```

**Retrieve:**

```csharp
var save = ServiceLocator.FetchGlobalService<ISaveService>();
```

**Good candidates:** analytics, audio, networking, save/load, settings.

---

## ScopedContext Services

ScopedContext services belong to a named game-state scope (e.g., `Gameplay`, `MainMenu`). The scope is created when you enter that state and torn down when you leave.

### 1. Define contexts

Add a `partial` declaration of `GameContexts` in your own assembly. **Each value must be a unique** `const int` greater than zero.

```csharp
namespace QBS.ServiceLocator
{
    public static partial class GameContexts
    {
        public const int Gameplay = 1;
        public const int Settings = 2;
    }
}
```

### 2. Implement the service

```csharp
[ServiceAttribute(GameContexts.Gameplay, typeof(IPlayerStatsService))]
public class PlayerStatsService : IPlayerStatsService
{
    public bool IsAsyncInit => false;

    protected override bool InitializeService()
    {
        // Set up gameplay-specific state.
        return true;
    }
}
```

### 3. Manage the container at runtime

```csharp
// When entering gameplay — instantiates and initializes all services for this context.
ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, GameContexts.Gameplay);

// Fetch the service.
var stats = ServiceLocator.FetchContextService<IPlayerStatsService>();

// When leaving gameplay — disposes all services in this context.
ServiceLocator.PurgeContainer(Lifetime.ScopedContext, GameContexts.Gameplay);
```

**Good candidates:** gameplay managers, menu controllers, per-state UI.

---

## Scene Services

Scene services are **not** auto-discovered. A scene's MonoBehaviours register themselves manually. This allows the scene hierarchy to own the service instances directly.

```csharp
[ServiceAttribute(Lifetime.Scene, typeof(ILevelService))]
public class LevelService : MonoBehaviour, ILevelService
{
    public bool IsAsyncInit => false;

    void Awake()
    {
        // Create a fresh scene container (disposes any previous one).
        ServiceLocator.RefreshSceneServiceContainer();

        // Self-register with the container.
        ServiceLocator.RegisterSceneService<ILevelService>(this);
    }

    protected override bool InitializeService()
    {
        // Called implicitly when needed, or drive it yourself.
        return true;
    }
}
```

**Retrieve from other scene objects:**

```csharp
var level = ServiceLocator.FetchSceneService<ILevelService>();
```

Scene containers always report `ContainerInitialized = true` immediately after creation; each service manages its own initialization timing.

**Good candidates:** level managers, scene-specific controllers, per-scene configuration.

---

## Async Initialization

Services can declare themselves async. The container initializes all sync services first, then fires all async initializations in parallel. The `ContainerServicesInitialized` event fires only after every async service has settled.

```csharp
[ServiceAttribute(Lifetime.Global, typeof(IRemoteConfigService))]
public class RemoteConfigService : IRemoteConfigService
{
    public bool IsAsyncInit => true;

    protected override async Task<bool> InitializeServiceAsync()
    {
        var result = await FetchConfigFromServer();
        return result.Success;
    }
}
```

### Waiting for a specific service

`AwaitInitialization` returns `true` once the service reaches `Success`, or `false` after the timeout.

```csharp
async void Start()
{
    var config = ServiceLocator.FetchGlobalService<IRemoteConfigService>();

    bool ready = await config.AwaitInitialization(maxWait: 10f);
    if (!ready)
    {
        Debug.LogError("Remote config failed to load in time.");
        return;
    }

    // Safe to use config here.
}
```

---

## Container Events

### Global container ready

```csharp
void OnEnable()
{
    ServiceLocator.GlobalServicesInitialized += OnGlobalReady;
}

void OnDisable()
{
    ServiceLocator.GlobalServicesInitialized -= OnGlobalReady;
}

void OnGlobalReady()
{
    Debug.Log("All global services are ready.");
}
```

### Context container ready

Subscribe **after** calling `DiscoverServicesOfLifetime` for that context.

```csharp
ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, GameContexts.Gameplay);
ServiceLocator.SubscribeToContextServiceSetup(GameContexts.Gameplay, OnGameplayReady);

void OnGameplayReady()
{
    Debug.Log("Gameplay services are ready.");
}

// Unsubscribe when no longer needed.
ServiceLocator.UnsubscribeToContextServiceSetup(GameContexts.Gameplay, OnGameplayReady);
```

---

## Safe Retrieval

`TryGet*` methods return `false` instead of throwing when a service is not registered or the container does not exist.

```csharp
if (ServiceLocator.TryGetGlobalService<IAudioService>(out var audio))
{
    audio.Play("theme");
}

if (ServiceLocator.TryGetContextService<IPlayerStatsService>(out var stats))
{
    stats.AddScore(100);
}

if (ServiceLocator.TryGetSceneService<ILevelService>(out var level))
{
    level.LoadNextWave();
}
```

---

## Initialization Status

| Property / Method | Returns |
|---|---|
| `IsGlobalContainerInitialized` | `true` once all global services have finished initializing |
| `IsSceneContainerInitialized` | `true` once a scene container has been created |
| `IsContextContainerInitialized(context)` | `true` once all services in that context have finished |

---

## Disposal

The runtime calls `Dispose()` on every service when its container is purged or the session ends. Override `DisposeService()` for custom cleanup — do **not** re-implement `IDisposable.Dispose` directly, as that bypasses internal state-table cleanup.

```csharp
protected override void DisposeService()
{
    _connection?.Close();
    _buffer?.Dispose();
}
```

---

## API Reference

### ServiceLocator — static methods

#### Retrieval

| Method | Description |
|---|---|
| `FetchGlobalService<T>()` | Returns the Global service; throws `KeyNotFoundException` if missing |
| `FetchContextService<T>()` | Returns the service from its registered context container |
| `FetchSceneService<T>()` | Returns the service from the current scene container |
| `TryGetGlobalService<T>(out T)` | Safe variant — returns `false` if not found |
| `TryGetContextService<T>(out T)` | Safe variant — returns `false` if not found |
| `TryGetSceneService<T>(out T)` | Safe variant — returns `false` if not found |

#### Container management

| Method | Description |
|---|---|
| `DiscoverServicesOfLifetime(lifetime, context)` | Creates and initializes a container for the given scope |
| `PurgeContainer(lifetime, context)` | Disposes and removes the container for the given scope |
| `RefreshSceneServiceContainer()` | Disposes any existing scene container and creates a fresh one |
| `RegisterSceneService<T>(service)` | Manually registers a scene service instance |

#### Events

| Member | Description |
|---|---|
| `GlobalServicesInitialized` | Fires when the Global container finishes initializing |
| `SubscribeToContextServiceSetup(context, action)` | Subscribe to a specific context container's init event |
| `UnsubscribeToContextServiceSetup(context, action)` | Unsubscribe from a context container's init event |

### IService — lifecycle

| Member | Description |
|---|---|
| `bool IsAsyncInit { get; }` | Declare whether this service uses async initialization |
| `ConfigurationState ConfigState { get; }` | Current init state |
| `Task<bool> AwaitInitialization(float maxWait)` | Awaits completion; returns `false` on failure or timeout |
| `protected virtual bool InitializeService()` | Override for synchronous init logic |
| `protected virtual Task<bool> InitializeServiceAsync()` | Override for asynchronous init logic |
| `protected virtual void DisposeService()` | Override for custom cleanup |

---

## Best Practices

**Do:**
- Always program to the interface (`IMyService`), never the concrete type
- Use `Global` for things that truly span the whole session
- Call `PurgeContainer` when leaving a scoped context to free resources
- Prefer `TryGet*` in code paths where a service might legitimately be absent
- Override `DisposeService()` (not `Dispose()`) when your service holds resources

**Avoid:**
- Creating circular dependencies between services in the same container; use `AwaitInitialization` if service A must wait for service B
- Storing references to Scene-lifetime services across scene loads
- Using the ServiceLocator in static constructors — `GameStart` may not have run yet
- Values of `0` for context constants — it is reserved as the "no context" default
