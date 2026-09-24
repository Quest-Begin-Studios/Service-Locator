# QBS Service Locator

A lightweight, reflection-driven Service Locator for Unity. Provides centralized service management with automatic discovery, four lifetime scopes, and first-class async initialization support — no boilerplate wiring required.

## Features

- **Automatic discovery** — tag a class with `[ServiceAttribute]` and it is found, instantiated, and initialized at runtime; no manual registration needed
- **Four lifetime scopes** — `Global` (application lifetime), `ScopedContext` (custom game-state scopes), `Scene` (per-scene, manually registered), and `PersistentScene` (app-lived GameObjects, in `DontDestroyOnLoad`)
- **Declared dependencies** — mark a field `[Inject]` and the container fills it and finishes that dependency first; initialization follows the dependency graph, in parallel wherever it can
- **Replaceable implementations** — `ServicePriority` lets a game override a package's service, or a test fake override both, instead of assembly order deciding
- **Sync and async initialization** — services declare `IsAsyncInit`; async services initialize in parallel without blocking the main thread
- **State tracking** — each service exposes a `ConfigurationState` (`Uninitialized` → `InProgress` → `Success` / `Failed`)
- **Safe retrieval** — `TryGet*` variants return `false` instead of throwing when a service is missing
- **Container events** — subscribe to `GlobalServicesInitialized` or per-context equivalents to react when a scope is ready
- **Extensible contexts** — define named scoped contexts via a `partial` class in your own assembly; no package modification required
- **Editor-safe** — all statics are flushed via `SubsystemRegistration`, so no state leaks between play sessions

## Requirements

- Unity 6000.0 or later
- [QBS Core](https://github.com/Quest-Begin-Studios/QBS-Core) 1.1.1 or later — service discovery resolves loaded assemblies through its `AssemblyCompat`, which covers the Unity 6000.4 assembly API change

## Installation

**A project names every dependency itself.** UPM does not resolve a git package's own dependencies
transitively, and a `dependencies` entry in `package.json` cannot be a git URL. QBS Core is therefore not
declared there at all: it is deliberately unpinned, so the locator takes whichever revision the project
supplies and stays current with Core by default. A project that adds only the locator fails to compile on
the missing `QBS.Core` assembly rather than reporting a missing package, so add both.

### Via `manifest.json`

Open `Packages/manifest.json` and add all three. This is the layout this repository's own Unity project
uses: UniTask through the OpenUPM scoped registry, QBS Core and the locator by git URL.

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp.unitask"
      ]
    }
  ],
  "dependencies": {
    "com.cysharp.unitask": "2.5.11",
    "com.qbs.core": "https://github.com/Quest-Begin-Studios/QBS-Core.git?path=/CoreUnity/Assets/com.qbs.core",
    "com.qbs.service-locator": "https://github.com/Quest-Begin-Studios/Service-Locator.git?path=ServiceLocatorUnity/Assets/com.qbs.ServiceLocator"
  }
}
```

Leave the QBS Core URL untagged, as above, to track its latest revision — that is the intended setup, and
the locator is kept working against Core's default branch. Pin it with `#v1.1.1` or later only when you
need a fixed revision; anything earlier has no `AssemblyCompat`, which service discovery needs. Append
`#v2.2.1` to the locator's own URL to pin it to a release.

### Via Unity Package Manager (Git URL)

Add the two git URLs above in order — QBS Core first, then the locator — with UniTask already installed:

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL…**
3. Enter:

```
https://github.com/Quest-Begin-Studios/Service-Locator.git?path=ServiceLocatorUnity/Assets/com.qbs.ServiceLocator
```

### Local path

Clone or download the repository, then reference it by local path:

```json
{
  "dependencies": {
    "com.qbs.service-locator": "file:../path/to/ServiceLocatorUnity/Assets/com.qbs.ServiceLocator"
  }
}
```

## Declaring dependencies

A service declares what it needs with `[Inject]` on a field. The container constructs every service
first, fills the injected fields, and then initializes each service as soon as everything it injected
has finished initializing.

```csharp
[Service(Lifetime.Global, typeof(IInventoryService))]
public class InventoryService : IInventoryService
{
    [Inject] private ISaveService _save = default;

    public bool IsAsyncInit => false;

    bool IService.InitializeService()
    {
        //_save is filled, and already initialized, by the time this runs.
        return _save.Load("inventory");
    }
}
```

The `= default` is only there to silence CS0649: nothing in your own code assigns the field, so the
compiler warns that it will always be null. A `csc.rsp` containing `-nowarn:0649` removes the need for
it project-wide.

The rules, each of which is an error that skips the service rather than a surprise at runtime:

- The field's type must be an interface that some service registers.
- It must be reachable: Global from anywhere, a context's own services from that context. A sibling
  context is not reachable, and neither are Scene or PersistentScene services, which register themselves
  from `Awake` — nothing discovered can know when they exist. Those stay on `Fetch*`.
- The field cannot be `readonly`, because it is written after the constructor has run.
- A cycle is reported with the loop named, and every service in it is skipped.

Initialization follows the graph rather than any batching: a service starts the moment the services it
injected have settled, so an unrelated slow service never holds it up. A **synchronous** service may
inject an asynchronous one — the container simply withholds it until the dependency is done, so it
never waits on anything itself and never blocks the main thread.

### Injecting is not the only way to reach a service

`[Inject]` says *do not initialize me until this one is ready*. That is the stronger of the two claims,
and it is why two services cannot inject each other: each would wait for the other forever, so the cycle
rule skips both.

When a service only needs the **reference** — to use later, at some point after boot — fetch it where it
is used instead. Every service is constructed and registered before any of them is initialized, so the
instance is always there, and two services can hold each other perfectly well this way.

```csharp
[Service(Lifetime.Global, typeof(IQuestService))]
public class QuestService : IQuestService
{
    //Not injected: InventoryService reaches back for this one, and a mutual [Inject] pair would be a
    //cycle that skips them both.
    public void Grant(Reward reward) => ServiceLocator.FetchGlobalService<IInventoryService>().Add(reward);

    public bool IsAsyncInit => false;
}
```

The rule of thumb: **inject it when you need it initialized before you are; fetch it when you only need
it to exist.**

A service with no injected field waits for nothing, and `Fetch*` works exactly as it always did.

## Quick Start

```csharp
// 1. Define a service interface
public interface IAudioService : IService
{
    void Play(string clip);
}

// 2. Implement and mark it for auto-discovery
[ServiceAttribute(Lifetime.Global, typeof(IAudioService))]
public class AudioService : IAudioService
{
    public bool IsAsyncInit => false;

    bool IService.InitializeService()
    {
        // one-time setup
        return true;
    }

    public void Play(string clip) { /* ... */ }
}

// 3. Retrieve it anywhere after startup
var audio = ServiceLocator.FetchGlobalService<IAudioService>();
audio.Play("theme");
```

For full usage documentation — scoped contexts, scene services, async initialization, the complete API reference, and best practices — see the [usage guide](ServiceLocatorUnity/Assets/com.qbs.ServiceLocator/Documentation~/README.md).

## License

See [LICENSE.md](ServiceLocatorUnity/Assets/com.qbs.ServiceLocator/LICENSE.md).
