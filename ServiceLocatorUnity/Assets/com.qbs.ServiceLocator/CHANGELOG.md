# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
