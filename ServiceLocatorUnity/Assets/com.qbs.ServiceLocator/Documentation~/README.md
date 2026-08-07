# QBS Service Locator — Usage Guide

A reflection-driven Service Locator for Unity with automatic service discovery, four lifetime scopes, and first-class support for async initialization.

## Table of Contents

- [Overview](#overview)
- [Installation](#installation)
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

The Service Locator is initialized automatically at `SubsystemRegistration`. Global services are discovered and started immediately. ScopedContext containers are created on demand by your own code when entering the appropriate game state. Scene containers — one per loaded scene — are created on that scene's first service registration and disposed by your own code when you tear the scene down.

---

## Installation

This package depends on [UniTask](https://github.com/Cysharp/UniTask), resolved via the [OpenUPM](https://openupm.com/packages/com.cysharp.unitask/) registry. Add the scoped registry to your project's `Packages/manifest.json` once, before installing this package:

```json
"scopedRegistries": [
  {
    "name": "OpenUPM",
    "url": "https://package.openupm.com",
    "scopes": [
      "com.cysharp.unitask"
    ]
  }
]
```

Without this, Unity Package Manager will not be able to resolve the `com.cysharp.unitask` dependency and the package will fail to compile.

This package also depends on [QBS Core](https://github.com/QuestBeginStudios/QBS-Core). It isn't distributed through a registry, so add it directly to the `dependencies` block of your project's `Packages/manifest.json`:

```json
"dependencies": {
  "com.qbs.core": "https://github.com/QuestBeginStudios/QBS-Core.git?path=/CoreUnity/Assets/com.qbs.core"
}
```

Unity Package Manager does not resolve git-URL dependencies transitively, so this step can't be skipped even though it's also listed in this package's own `package.json`.

---

## Core Concepts

### IService

Every service must implement `IService`. It provides initialization lifecycle hooks, state tracking, and disposal.

`InitializeService`, `InitializeServiceAsync`, and `DisposeService` are `protected internal` members of the interface, so C# requires implementing them via **explicit interface implementation** — not a plain `override` (interfaces don't support `override` for class members at all, and non-public interface members can only be implemented explicitly).

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

    bool IService.InitializeService()
    {
        // Return false to signal initialization failure.
        return true;
    }

    public void DoWork() { }

    void IService.DisposeService()
    {
        // Optional: clean up resources here.
        // Do NOT implement IDisposable.Dispose — implement this instead.
    }
}
```

### ServiceAttribute

Marks a class for automatic discovery. The attribute specifies the **lifetime scope** and the **interface type** under which the service is registered.

```csharp
// Global, Scene or PersistentScene lifetime — pass the Lifetime enum value
[ServiceAttribute(Lifetime.Global, typeof(IMyService))]

// ScopedContext lifetime — pass an int context constant (implicit conversion to Context)
[ServiceAttribute(GameContexts.Gameplay, typeof(IMyService))]
```

The attribute is the **single source of truth** for a service's lifetime — registration validates against it and refuses a mismatch, rather than letting the call site decide.

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

    bool IService.InitializeService()
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

Define your own context constants in your own assembly using `const int` values and implicit conversion to `Context`. **Each value must be unique and greater than zero** — `0` is reserved as the unset default.

```csharp
public static class GameContexts
{
    public const int Gameplay = 1;
    public const int Settings = 2;
}
```

### 2. Implement the service

```csharp
[ServiceAttribute(GameContexts.Gameplay, typeof(IPlayerStatsService))]
public class PlayerStatsService : IPlayerStatsService
{
    public bool IsAsyncInit => false;

    bool IService.InitializeService()
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

**There is one container per loaded scene.** The container for a scene is created on that scene's first registration, so nothing needs setting up before a scene's objects register. Tearing it down is yours to drive: call `DisposeSceneContainer(scene)` before you unload the scene.

```csharp
[ServiceAttribute(Lifetime.Scene, typeof(ILevelService))]
public class LevelService : MonoBehaviour, ILevelService
{
    public bool IsAsyncInit => false;

    void Awake()
    {
        // Registers into this component's own scene, creating that scene's container if needed.
        ServiceLocator.RegisterSceneService<ILevelService>(this);
    }

    bool IService.InitializeService()
    {
        // Called implicitly when needed, or drive it yourself.
        return true;
    }
}
```

**Retrieve from other scene objects** — pass `this`, and the caller's own scene is used:

```csharp
var level = ServiceLocator.FetchSceneService<ILevelService>(this);
```

Scene containers always report `ContainerInitialized = true` immediately after creation; each service manages its own initialization timing.

**Good candidates:** level managers, scene-specific controllers, per-scene configuration.

### Resolution contract

Scene resolution is a deterministic `(Scene, Type)` lookup, and it is **strictly local**: a scene's container is never searched on behalf of another scene, and there is no fallback to a "current" scene. Two additively-loaded scenes may each register the same interface; the two instances are independent and unambiguous.

```csharp
// Primary — an explicit scene.
var level = ServiceLocator.FetchSceneService<ILevelService>(someScene);

// Ergonomic — infers someComponent.gameObject.scene.
var level = ServiceLocator.FetchSceneService<ILevelService>(this);
```

Both return `null` when that scene has no container. `TryGetSceneService` has the same two overloads and returns `false` instead.

**Non-MonoBehaviour callers must pass an explicit `Scene`.** A non-MonoBehaviour resolving a scene service is discouraged — it almost always wants a `Global` or `ScopedContext` service instead. If one genuinely needs a scene service, it must be handed the `Scene` it belongs to and pass it explicitly; the same applies to registration, via `RegisterSceneService<T>(service, scene)`.

### Lifetime and disposal

| Trigger | Effect |
|---|---|
| First `RegisterSceneService` for a scene | That scene's container is created; `SceneContainerCreated` fires |
| `DisposeSceneContainer(scene)` | Disposes only that scene's services and drops its container; `SceneContainerDisposed` fires |
| `PurgeContainer(Lifetime.Scene)` | Disposes **every** scene container, the persistent one included |
| `PurgeContainer(Lifetime.PersistentScene)` | Disposes only the persistent container, leaving ordinary scenes alone |

**The locator does not watch `SceneManager.sceneUnloaded`.** Deciding when a scene's services die is the job of whatever owns the scene load — the same division as `PurgeContainer` for a ScopedContext. Call `DisposeSceneContainer(scene)` yourself, and call it *before* `UnloadSceneAsync`: at that point the scene's GameObjects are still alive, so a MonoBehaviour service's `DisposeService()` can still touch Unity state. (Had the package hooked `sceneUnloaded`, disposal would always land after `OnDestroy`, with every service already destroyed.)

The flip side is that a container you never dispose outlives its scene, keyed by a `Scene` handle that Unity is free to recycle — a later scene reusing that handle would inherit the stale container. Treat "dispose before you unload" as a hard contract, not a nicety.

### Persistent scene services

Some services have to be GameObjects but must not die with a scene — a loading overlay that draws over everything, an audio rig built from `AudioSource`s. Making them ordinary scene services forces a copy into every scene, and `Global`/`ScopedContext` can't help because those are reflection-instantiated POCOs.

Mark them `Lifetime.PersistentScene` instead. They live in Unity's `DontDestroyOnLoad` scene, which is just another scene as far as the container model is concerned — so this reuses the same container, keyed by that scene:

```csharp
[ServiceAttribute(Lifetime.PersistentScene, typeof(ILoaderService))]
public class LoaderScreen : MonoBehaviour, ILoaderService
{
    void Awake()
    {
        // Moves this object to DontDestroyOnLoad and registers it there.
        ServiceLocator.RegisterPersistentSceneService<ILoaderService>(this);
    }
}
```

```csharp
var loader = ServiceLocator.FetchPersistentSceneService<ILoaderService>();
```

- **`Lifetime.PersistentScene` and `Lifetime.Scene` are not interchangeable.** `RegisterSceneService` refuses a `PersistentScene` service and `RegisterPersistentSceneService` refuses a `Scene` one, so the attribute — not the call site — decides how long a service lives, and one interface cannot end up live in a scene container *and* the persistent container at once.
- **You don't call `DontDestroyOnLoad` yourself** — registration does it, so there is no ordering to get wrong and no way to end up with a "persistent" service that still dies with its scene. Re-registering something already there is a harmless no-op move.
- **A refused registration is never moved.** Every check runs before the `DontDestroyOnLoad` call, because that move can't be undone: a rejected object that had already been moved would sit outside every scene, alive and unreachable, for the rest of the session.
- **The service must be on a root GameObject**, because Unity only honours `DontDestroyOnLoad` for roots. A nested component is rejected with an error rather than silently left behind in a scene that unloads.
- **Resolution is a separate, explicit call.** `FetchSceneService<T>(scene)` will *not* find a persistent service, and `FetchPersistentSceneService<T>()` will not find an ordinary one. Ordinary scene resolution stays a strictly local `(Scene, Type)` answer, and reaching across the boundary is visible at the call site.
- **Lifetime is Unity's.** Nothing unloads the DDOL scene, so the persistent container lives until `PurgeContainer(Lifetime.PersistentScene)`, `PurgeContainer(Lifetime.Scene)`, or a session reset. Unity exposes no handle for that scene, so the locator learns it from the first accepted persistent registration and forgets it again on the next session.

### Cross-scene indexes

If a consumer needs a registry spanning scenes, build it on the package's events rather than on cross-scene fallback:

```csharp
ServiceLocator.SceneContainerCreated  += scene => { /* ... */ };
ServiceLocator.SceneContainerDisposed += scene => { /* drop this scene's entries */ };
ServiceLocator.SceneServiceRegistered += (scene, serviceType, service) => { /* index it */ };
```

---

## Async Initialization

Services can declare themselves async. The container initializes all sync services first, then fires all async initializations in parallel. The `ContainerServicesInitialized` event fires only after every async service has settled.

```csharp
[ServiceAttribute(Lifetime.Global, typeof(IRemoteConfigService))]
public class RemoteConfigService : IRemoteConfigService
{
    public bool IsAsyncInit => true;

    async UniTask<bool> IService.InitializeServiceAsync()
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

if (ServiceLocator.TryGetSceneService<ILevelService>(this, out var level))
{
    level.LoadNextWave();
}
```

---

## Initialization Status

| Property / Method | Returns |
|---|---|
| `IsGlobalContainerInitialized` | `true` once all global services have finished initializing |
| `IsSceneContainerInitialized(scene)` | `true` once that scene's container has been created and not yet disposed |
| `IsContextContainerInitialized(context)` | `true` once all services in that context have finished |

---

## Disposal

The runtime calls `Dispose()` on every service when its container is purged or the session ends. Implement `DisposeService()` for custom cleanup — do **not** re-implement `IDisposable.Dispose` directly, as that bypasses internal state-table cleanup. Service discovery checks for this and rejects (with a logged error) any service type that does.

```csharp
void IService.DisposeService()
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
| `FetchContextService<T>()` | Returns the service from its registered context container; `null` if that context has no live container, `KeyNotFoundException` if the container has no such service |
| `FetchSceneService<T>(scene)` | Returns the service from that scene's container; `null` if the scene has no container |
| `FetchSceneService<T>(component)` | Same, resolving `component.gameObject.scene` |
| `FetchPersistentSceneService<T>()` | Returns the service from the `DontDestroyOnLoad` container; `null` if none registered |
| `TryGetGlobalService<T>(out T)` | Safe variant — returns `false` if not found |
| `TryGetContextService<T>(out T)` | Safe variant — returns `false` if not found |
| `TryGetSceneService<T>(scene, out T)` | Safe variant — returns `false` if not found |
| `TryGetSceneService<T>(component, out T)` | Same, resolving `component.gameObject.scene` |
| `TryGetPersistentSceneService<T>(out T)` | Safe variant — returns `false` if not found |

#### Container management

| Method | Description |
|---|---|
| `DiscoverServicesOfLifetime(lifetime, context)` | Creates and initializes a container for the given scope |
| `PurgeContainer(lifetime, context)` | Disposes and removes the container for the given scope; `Lifetime.Scene` disposes every scene container, `Lifetime.PersistentScene` only the persistent one |
| `RegisterSceneService<T>(service)` | Registers a `Lifetime.Scene` service into its own `Component`'s scene, creating that container if needed |
| `RegisterSceneService<T>(service, scene)` | Registers against an explicit scene — required for non-`Component` services |
| `RegisterPersistentSceneService<T>(service)` | Validates a `Lifetime.PersistentScene` service, then moves its root GameObject to `DontDestroyOnLoad` and registers it there |
| `DisposeSceneContainer(scene)` | Disposes just that scene's services; call it before unloading the scene |

#### Events

| Member | Description |
|---|---|
| `GlobalServicesInitialized` | Fires when the Global container finishes initializing |
| `SceneContainerCreated` | `Action<Scene>` — fires when a scene's container is created |
| `SceneContainerDisposed` | `Action<Scene>` — fires after a scene's container and services are disposed |
| `SceneServiceRegistered` | `Action<Scene, Type, IService>` — fires per scene service registration |
| `SubscribeToContextServiceSetup(context, action)` | Subscribe to a specific context container's init event |
| `UnsubscribeToContextServiceSetup(context, action)` | Unsubscribe from a context container's init event |

### IService — lifecycle

| Member | Description |
|---|---|
| `bool IsAsyncInit { get; }` | Declare whether this service uses async initialization |
| `ConfigurationState GetConfigState()` | Current init state |
| `UniTask<bool> AwaitInitialization(float maxWait)` | Awaits completion; returns `false` on failure or timeout |
| `protected internal bool InitializeService()` | Implement (explicit interface implementation) for synchronous init logic |
| `protected internal UniTask<bool> InitializeServiceAsync()` | Implement (explicit interface implementation) for asynchronous init logic |
| `protected internal void DisposeService()` | Implement (explicit interface implementation) for custom cleanup |

---

## Best Practices

**Do:**
- Always program to the interface (`IMyService`), never the concrete type
- Use `Global` for things that truly span the whole session
- Call `PurgeContainer` when leaving a scoped context to free resources
- Prefer `TryGet*` in code paths where a service might legitimately be absent
- Implement `DisposeService()` (not `Dispose()`) when your service holds resources

**Avoid:**
- Creating circular dependencies between services in the same container; use `AwaitInitialization` if service A must wait for service B
- Storing references to Scene-lifetime services across scene loads
- Reaching into another scene's services; coordinate through a `Global`/`ScopedContext` service or an orchestrator instead
- Using the ServiceLocator in static constructors — `GameStart` may not have run yet
- Values of `0` for context constants — it is reserved as the "no context" default
