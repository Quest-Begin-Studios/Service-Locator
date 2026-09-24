# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.1] - 2026-09-24

### Changed

- **Synchronous services initialize before asynchronous ones.** Services that are ready to start now wait in two queues, one for synchronous and one for asynchronous initialization. The synchronous queue is emptied first, including any synchronous services released while it drains. Only then does an asynchronous service start, and the synchronous queue is drained again after each start. Cheap synchronous work finishes on the frame it becomes ready instead of queueing behind async starts, while each service still waits for its own dependencies.

## [2.2.0] - 2026-09-21

### Added

- **`[Inject]` fields.** A discovered service may declare its dependencies by marking fields `[Inject]` instead of calling `Fetch*` inside `InitializeService`. The container constructs every service of the lifetime, fills those fields, and only then initializes anything, so an injected field is never null and never half-initialized when `InitializeService` runs. Declaring a dependency is now a statement about *initialization order*, which is what the ordering actually needs; a service that only wants the reference keeps fetching it at the point of use. Fields are collected from base classes too, since a private field is not visible through a derived type.
- **Dependency-ordered initialization.** Every service that injected nothing starts at once, and each service that settles releases the services waiting on it, so a service begins the moment its own dependencies are done rather than when a batch it happens to share a depth with is. Independent work runs in parallel and a slow service delays only what actually depends on it. Each initialization task is awaited exactly once, by the service's own runner: a UniTask carries a single continuation, so dependents are released by a counter rather than by awaiting someone else's task.
- **Failure propagation.** When a dependency's initialization fails, its dependents are marked `Failed` without being initialized, and the log names the dependency rather than the symptom. Previously a dependent ran against a half-initialized dependency.

### Changed

- **A synchronous service may now depend on an asynchronous one.** The container withholds it until the dependency has settled and then runs it, so it never waits and never blocks the main thread. Previously this was the one ordering the runtime could not honour and the dependent was marked `Failed`. `IsAsyncInit` now describes only whether a service's own work is asynchronous, rather than doubling as a claim about what it waits for.
- An injected field's type must be an interface some service registers, and must be reachable from the service's own container: Global for anything, or the service's own context for a `ScopedContext` service. Anything else — a concrete type, a `string`, a sibling context, a Scene or PersistentScene service — is an error at discovery and the service is skipped. Scene and PersistentScene services register themselves from `Awake` and are never injected, because no discovered service can know when they exist.
- An injected field cannot be `readonly`. The container writes it after the constructor has run, and writing an initonly field by reflection is not something every runtime honours; it is refused at discovery rather than left to fail on one platform.
- A cycle among injected fields is reported with the loop spelled out (`IA -> IB -> IA`) and every service in it is skipped; the rest of the container is unaffected. The error names the way out, which is to drop `[Inject]` on one side and fetch that service where it is used.

## [2.1.0] - 2026-09-21

### Added

- `ServicePriority` (`Default`, `Override`, `Tests`) and `ServiceAttribute.Priority`, an optional third argument on both constructors defaulting to `ServicePriority.Default`. When two concrete types claim the same `ServiceType`, the higher priority wins and assembly enumeration order no longer decides which one: the lower-priority type is skipped silently, and a higher-priority type evicts an owner the scan happened to meet first. Equal priorities remain an error that keeps the first type met, so neither two packages nor two game services can silently fight over one interface. Precedence is the declaration order of the enum, so a tier can be added later without renumbering anything: a package ships `Default`, a game replaces it with `Override`, a test fake takes `Tests`.

### Changed

- `ServiceAttribute` derives from `UnityEngine.Scripting.PreserveAttribute`, so managed stripping keeps every type marked `[Service]` and the constructor discovery calls on it. A service is found by reflection and referenced statically by nothing, which is precisely what the linker deletes; measured on 6000.5.1f1 at stripping High, a plain `[Service]` type is gone from the player assembly and one whose attribute derives from `PreserveAttribute` survives with its members. Nothing in consuming code changes, and no `link.xml` is needed. `AttributeUsage` is now declared explicitly on `ServiceAttribute` so it does not adopt `PreserveAttribute`'s `Inherited = false`, which would have quietly changed how discovery reads the attribute.
- `FetchGlobalService`, `TryGetGlobalService` and `IsGlobalContainerInitialized` throw `InvalidOperationException` naming `GameStart()` when the Global container does not exist yet, instead of dereferencing null. Calling before `SubsystemRegistration`, or from an EditMode test that never started the locator, now reports the reason rather than a `NullReferenceException` from inside the package.
- `SceneServiceContainer.RegisterService<T>` names the already-registered concrete type and the priority its attribute won at, so a scene override that did not take effect is visible in the error.

### Fixed

- The unresolvable `com.qbs.core` git URL is gone from `dependencies`. UPM only resolves a `dependencies` entry against a registry or against a package the project manifest already names, and a git URL is neither. The entry is dropped rather than replaced with a version range: the locator is meant to track Core's latest revision, and a floor in the metadata is one more number to remember to bump. Consuming projects list `com.qbs.core` in their own `Packages/manifest.json` — untagged, to stay current — as the README now shows.
- `ImplementsDisposeCorrectly` no longer skips every service in a player built at a high IL2CPP stripping level. `Type.GetInterfaceMap` is unavailable there and the throw propagated out of discovery; it is now wrapped, logs a warning naming the type, and assumes the implementation is correct.

## [2.0.1] - 2026-09-10

### Fixed

- Service discovery called `UnityEngine.Assemblies.CurrentAssemblies`, which exists only in Unity 6000.4 and later, so the package failed to compile on 6000.0 through 6000.3. It now goes through `QBS.Core.AssemblyCompat`, which selects the API the running editor actually has.

### Changed

- `QBS.ServiceLocator` references the `QBS.Core` assembly, so `com.qbs.core` 1.1.1 or later is now required. Consumers pinning an older core revision in their `Packages/manifest.json` need to move to `v1.1.1` or a later commit.

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
