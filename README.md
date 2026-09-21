# QBS Service Locator

A lightweight, reflection-driven Service Locator for Unity. Provides centralized service management with automatic discovery, three lifetime scopes, and first-class async initialization support — no boilerplate wiring required.

## Features

- **Automatic discovery** — tag a class with `[ServiceAttribute]` and it is found, instantiated, and initialized at runtime; no manual registration needed
- **Three lifetime scopes** — `Global` (application lifetime), `ScopedContext` (custom game-state scopes), and `Scene` (per-scene, manually registered)
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
`#v2.1.0` to the locator's own URL to pin it to a release.

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

A service may take the services it needs as constructor parameters. The container resolves them, builds
in dependency order, and initializes in that same order, so a dependency is always initialized before the
service that was handed it.

```csharp
[Service(Lifetime.Global, typeof(IInventoryService))]
public class InventoryService : IInventoryService
{
    private readonly ISaveService _save;

    public InventoryService(ISaveService save) => _save = save;

    public bool IsAsyncInit => false;
}
```

The rules, each of which is an error that skips the service rather than a surprise at runtime:

- A parameter must be an interface that some service registers.
- It must be reachable: Global from anywhere, a context's own services from that context. A sibling
  context is not reachable, and neither are Scene or PersistentScene services, which register themselves
  from `Awake` — nothing discovered can know when they exist. Those stay on `Fetch*`.
- Several public constructors need `[ServiceConstructor]` on the one to use.
- A cycle is reported with the loop named, and every service in it is skipped.
- A synchronous service cannot depend on an asynchronous one; make it async. Waiting for it would mean
  blocking the main thread.

### Not every dependency belongs in a constructor

A constructor parameter says *I cannot be built without this*. That is why two services that need each
other cannot both declare it: neither can be constructed first, so the cycle rule above skips both, and
no amount of ordering can fix it.

A reference a service uses *after* boot rather than *during* construction is not that kind of
dependency. Leave it out of the constructor and fetch it at the point of use. Registration happens
before initialization, so the instance is there by the time any of your code runs, and two services can
hold each other perfectly well that way.

```csharp
[Service(Lifetime.Global, typeof(IQuestService))]
public class QuestService : IQuestService
{
    //Not a constructor parameter: IInventoryService fetches this one back, and a pair of parameters
    //would be a cycle neither side could be built out of.
    public void Grant(Reward reward) => ServiceLocator.FetchGlobalService<IInventoryService>().Add(reward);

    public bool IsAsyncInit => false;
}
```

The rule of thumb: take it as a parameter when you need it to be *initialized* before you are, and fetch
it at use time when you only need it to *exist*.

A parameterless constructor keeps working exactly as before, `Fetch*` included.

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

    protected override bool InitializeService()
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

For full usage documentation — scoped contexts, scene services, async initialization, the complete API reference, and best practices — see the [package README](ServiceLocatorUnity/Assets/com.qbs.ServiceLocator/README.md).

## License

See [LICENSE.md](ServiceLocatorUnity/Assets/com.qbs.ServiceLocator/LICENSE.md).
