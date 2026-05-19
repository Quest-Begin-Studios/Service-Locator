# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
