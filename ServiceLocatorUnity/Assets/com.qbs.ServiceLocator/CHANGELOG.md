# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-08-08

Major version because the `Removed` section below deletes four public members. Every existing consumer breaks at compile time on upgrade; the migration table maps each one to its replacement.

### Added

- Multi-scene support: the locator now holds **one scene container per loaded scene**, keyed by the `Scene` itself, instead of a single global one. Additively-loaded scenes can each own their own scene-lifetime services, and two scenes may register the same service interface without colliding.
- `RegisterSceneService<T>(service)` now infers the owning scene from the service's own `Component.gameObject.scene` and creates that scene's container lazily on first registration — valid during `Awake`, so no setup call is needed before a scene's objects register.
- `RegisterSceneService<T>(service, scene)` — explicit-scene registration, required for non-`Component` scene services.
- `FetchSceneService<T>(Scene)` / `FetchSceneService<T>(Component)` and `TryGetSceneService<T>(Scene, out T)` / `TryGetSceneService<T>(Component, out T)` — scene resolution is now a deterministic `(Scene, Type)` lookup and is strictly local, with no fallback to another scene's container.
- `DisposeSceneContainer(Scene)` — disposes exactly that scene's services and drops its container. Scene teardown stays caller-driven, matching `PurgeContainer` for ScopedContext: the locator does not subscribe to `SceneManager.sceneUnloaded`. Call it *before* unloading the scene, while its services are still live objects.
- `IsSceneContainerInitialized(Scene)`.
- `Lifetime.PersistentScene` and persistent scene services — `RegisterPersistentSceneService<T>(service)`, `FetchPersistentSceneService<T>()`, `TryGetPersistentSceneService<T>(out T)`. Fills the gap for services that must be authored GameObjects but are app-lived (loading overlays, audio rigs), which previously had to be duplicated into every scene because `Global`/`ScopedContext` services are reflection-instantiated POCOs. They live in the `DontDestroyOnLoad` scene, which reuses the ordinary per-scene container keyed by that scene. Registration performs the `DontDestroyOnLoad` move itself, so there is no ordering to get wrong; the service must be on a root GameObject. Resolution is a separate explicit call — the persistent container is never a fallback for `FetchSceneService`, so `(Scene, Type)` stays strictly local.
- A scene container now holds exactly one of `Lifetime.Scene` or `Lifetime.PersistentScene` and rejects services marked with the other, in both directions. The `[Service]` attribute therefore remains the single source of truth for how long a service lives, and one interface can no longer end up live in a scene container *and* the persistent container at the same time. `Lifetime.PersistentScene` is appended to the end of the enum rather than grouped beside `Scene`, so existing serialized `Lifetime` values keep pointing at the same members.
- `PurgeContainer(Lifetime.PersistentScene)` — disposes just the persistent container, leaving ordinary scene containers alone. The captured DontDestroyOnLoad scene is kept, so a later persistent registration reuses it.
- `SceneContainerCreated`, `SceneContainerDisposed` (`Action<Scene>`) and `SceneServiceRegistered` (`Action<Scene, Type, IService>`) events, so consumers can maintain cross-scene indexes without the package knowing about them.
- `SceneServiceContainer.Scene` — the scene a container owns, supplied via its constructor.

### Changed

- `PurgeContainer(Lifetime.Scene)` now disposes **all** scene containers rather than the single one — the persistent container included. Use `DisposeSceneContainer(scene)` for one scene, or `PurgeContainer(Lifetime.PersistentScene)` for just the persistent one.
- `IsSceneContainerInitialized` changed from a property to `IsSceneContainerInitialized(Scene)`.
- `SceneServiceContainer`'s constructor now takes the owning `Scene` as a second argument, and an optional `Lifetime` as a third (defaults to `Lifetime.Scene`).

### Removed

The scene members that resolved against `SceneManager.GetActiveScene()` are gone rather than deprecated — the active scene is not a reliable owner under additive loads, so there is no correct behaviour for them to fall back to. Migrate call sites to the scene- or component-keyed replacements:

- `FetchSceneService<T>()` → `FetchSceneService<T>(scene)` or `(component)`
- `TryGetSceneService<T>(out T)` → `TryGetSceneService<T>(scene, out T)` or `(component, out T)`
- `IsSceneContainerInitialized` (property) → `IsSceneContainerInitialized(scene)`
- `RefreshSceneServiceContainer()` → delete the call site; containers are now created on first registration, and you call `DisposeSceneContainer(scene)` before unloading the scene

### Fixed

- `RegisterSceneService<T>` no longer fails when nothing has pre-created a scene container — the container is created on demand by the registration itself.
- `RegisterPersistentSceneService<T>` validated the service *after* calling `DontDestroyOnLoad` on it, so a registration refused for any reason — missing `[Service]` attribute, wrong lifetime, duplicate — left its GameObject permanently outside every scene, alive and unreachable for the rest of the session. All validation now runs before the move.
- `RegisterSceneService<T>(service, scene)` accepted `null` and stored it, turning into a `NullReferenceException` inside `DisposeContainer` long after the call that caused it. It is now rejected with an error.
- Service discovery threw `ArgumentException` out of `[RuntimeInitializeOnLoadMethod]` when two concrete types were attributed to the same `ServiceType` — the ordinary mock/editor-implementation pattern — abandoning the whole scan and leaving every remaining service unregistered. The collision is now logged, naming both types, and the later type is skipped.
- `FetchContextService<T>` and `TryGetContextService<T>` threw `KeyNotFoundException` for any `Global`- or `Scene`-lifetime service: the context map was populated for every lifetime, so those types resolved to `Context` 0 and indexed a container that is never created. Only `ScopedContext` services are mapped now.
- `FetchContextService<T>` and `TryGetContextService<T>` also threw when their context had been purged, since the context map is built once at discovery and still named the dead container. Both fail safe now — `null` and `false` respectively — so `TryGet*` honours its contract.
- `ServiceContainer` rethrew any exception from a service constructor after logging it, propagating out of `[RuntimeInitializeOnLoadMethod]` and taking down boot. The offending service is now skipped and the rest of the container is populated.
- Scene container lookups allocated on every call: `UnityEngine.SceneManagement.Scene` implements no interfaces (notably not `IEquatable<Scene>`), so `Dictionary<Scene, _>` fell back to the reflection-based object comparer and boxed the key on each probe. The dictionary now uses an explicit `IEqualityComparer<Scene>`.

## [1.1.0] - 2026-07-21

### Changed

- Async service initialization (`InitializeServiceAsync`, `AwaitInitialization`, container-level async handling) migrated from `System.Threading.Tasks.Task` to `Cysharp.Threading.Tasks.UniTask`. Package now depends on `com.cysharp.unitask`, resolved via an OpenUPM scoped registry (see README Installation).
- Service initialization/disposal state tracking (`ConfigState`, `AsyncInitTask`) moved out of `IService`'s default interface implementations into a new internal `ServiceExtensions` static class, exposed via `GetConfigState()` / `GetAsyncInitTask()` extension methods.
- `InitializeService`, `InitializeServiceAsync`, and `DisposeService` are now `protected internal` interface members, requiring explicit interface implementation (e.g. `bool IService.InitializeService()`) instead of `protected virtual` + `override`.
- Service discovery now rejects any service type that re-implements `IDisposable.Dispose` directly rather than relying on `IService`'s default implementation, logging an error and skipping registration instead of silently leaking state.
- Package now also depends on `com.qbs.core` (git dependency).

### Added

- Exceptions thrown during synchronous or asynchronous service initialization are now caught, logged, and recorded as `ConfigurationState.Failed` instead of propagating and blocking initialization of the rest of the container.

### Removed

- `GameContexts` static class removed from the package; application-specific `Context` values are now defined in the consuming project's own assembly (documented in README).

### Fixed

- `DiscoverServicesOfLifetime(ScopedContext, ...)`, `IsContextContainerInitialized`, `FetchContextService`/`TryGetContextService`, and `Subscribe`/`UnsubscribeToContextServiceSetup` threw `NullReferenceException` on a fresh session — the ScopedContext container map was never initialized, only ever nulled out during cleanup. ScopedContext lifetime is now functional from a cold start.
- `RegisterSceneService<T>(service)` always failed with `"...is not marked with an ServiceAttribute"` when called exactly as documented (registering by interface) — the internal lookup was keyed by concrete implementation type instead of the interface. Scene registration now works via the documented interface-based pattern.
- `AwaitInitialization` threw `InvalidOperationException: Already continuation registered...` when called on a service the container itself was still concurrently initializing — i.e. the documented "service A waits for service B" use case. It now polls initialization state instead of re-awaiting the same in-flight task from two places at once.

## [1.0.0] - 2026-05-19

### Added

- `ServiceLocator` static class — central access point for all service lifetimes; auto-initializes at `SubsystemRegistration` and flushes state cleanly between editor play sessions
- `IService` interface with default interface implementations for sync and async initialization, `ConfigurationState` tracking, `AwaitInitialization`, and safe `IDisposable` integration via `DisposeService`
- `ConfigurationState` enum (`Uninitialized`, `InProgress`, `Failed`, `Success`)
- `Lifetime` enum (`Global`, `Scene`, `ScopedContext`) — defines when a service is created and destroyed
- `ServiceAttribute` — marks service implementation classes for reflection-based auto-discovery; accepts a `Lifetime` for Global/Scene services or an `int` context constant for ScopedContext services
- `Context` readonly struct — integer-backed scope identifier with implicit `int` conversion, a static registry that prevents duplicate values, and a reserved zero for the unset/default state
- `GameContexts` partial static class — extend it in your own assembly with additional `const int` values to define application-specific scopes
- `BaseServiceContainer` abstract class — provides type-safe `GetService<T>` / `TryGetService<T>` lookup and shared `DisposeContainer` logic
- `ServiceContainer` — discovers and instantiates services via reflection for `Global` and `ScopedContext` lifetimes; initializes sync services first then fires all async services in parallel; raises `ContainerServicesInitialized` when all services settle
- `SceneServiceContainer` — manual-registration container for `Scene`-lifetime services; raises `SceneServiceRegistered` per registration; always reports `ContainerInitialized = true`
- `FetchGlobalService<T>`, `FetchContextService<T>`, `FetchSceneService<T>` — direct retrieval methods
- `TryGetGlobalService<T>`, `TryGetContextService<T>`, `TryGetSceneService<T>` — safe retrieval returning `false` instead of throwing when a service is absent
- `IsGlobalContainerInitialized`, `IsSceneContainerInitialized`, `IsContextContainerInitialized(context)` — initialization state checks for each scope
- `GlobalServicesInitialized` event, `SubscribeToContextServiceSetup` / `UnsubscribeToContextServiceSetup` — subscribe to container-ready notifications
- `DiscoverServicesOfLifetime(lifetime, context)` — creates and initializes a container for a given scope on demand
- `PurgeContainer(lifetime, context)` — disposes and removes the container for a given scope
- `RefreshSceneServiceContainer()` / `RegisterSceneService<T>(service)` — scene container lifecycle management
