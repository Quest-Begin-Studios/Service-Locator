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

## Installation

### Via Unity Package Manager (Git URL)

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL…**
3. Enter:

```
https://github.com/Quest-Begin-Studios/Service-Locator.git?path=ServiceLocatorUnity/Assets/com.qbs.ServiceLocator
```

To pin a specific release append `#v1.0.0` to the URL.

### Via `manifest.json`

Open `Packages/manifest.json` and add an entry under `dependencies`:

```json
{
  "dependencies": {
    "com.qbs.service-locator": "https://github.com/Quest-Begin-Studios/Service-Locator.git?path=ServiceLocatorUnity/Assets/com.qbs.ServiceLocator"
  }
}
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
