using System;
using System.Collections.Generic;
using QBS.Core;
using UnityEngine;
using UnityEngine.Assemblies;
using UnityEngine.SceneManagement;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Central service locator providing global access to services across different lifetimes and contexts.
	///     Manages service discovery, registration, and retrieval for Global, ScopedContext, and Scene lifetime services.
	///     Automatically initializes global services at runtime and provides container management for different service
	///     scopes.
	/// </summary>
	public static class ServiceLocator
    {
        #region Events

        // Fired when global service container finishes initializing all services
        public static event Action GlobalServicesInitialized
        {
            add => _globalServiceContainer.ContainerServicesInitialized += value;
            remove => _globalServiceContainer.ContainerServicesInitialized -= value;
        }

        // Fired when a scene's container is created, i.e. on that scene's first service registration
        public static event Action<Scene> SceneContainerCreated;

        // Fired after a scene's container and every service in it has been disposed
        public static event Action<Scene> SceneContainerDisposed;

        // Fired for every scene service registration, carrying the scene that owns it
        public static event Action<Scene, Type, IService> SceneServiceRegistered;

        #endregion

        #region Containers

        private static ServiceContainer _globalServiceContainer;
        private static Dictionary<Context, ServiceContainer> _contextServiceContainers;

        //One container per loaded scene. Scene's equality is its handle, so a scene instance is the key.
        private static Dictionary<Scene, SceneServiceContainer> _sceneServiceContainers;

        //The DontDestroyOnLoad scene, learned from the first persistent registration. See RegisterPersistentSceneService.
        private static Scene _persistentScene;

        #endregion

        private static Dictionary<Type, Context> _serviceContextMap;

        //Type-key (Concrete classes) against ServiceAttributes
        private static Dictionary<Type, ServiceAttribute> _allServicesConcreteMap;

        //Type-key (Interfaces) against ServiceAttributes
        private static Dictionary<Type, ServiceAttribute> _allServicesInterfaceMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void GameStart()
        {
            CleanupStatics();
            GetAllServiceAttributedTypes();
            DiscoverServicesOfLifetime(Lifetime.Global);
        }

        private static void CleanupStatics()
        {
            _allServicesConcreteMap?.Clear();
            _globalServiceContainer?.DisposeContainer();

            if (_contextServiceContainers != null)
            {
                foreach (var (_, container) in _contextServiceContainers)
                {
                    container?.DisposeContainer();
                }
            }

            Context.ClearRegisteredContexts();
            ServiceExtensions.CleanupConfigStateTable();

            //Subscribers from the previous session are about to be dropped, so they are not notified
            DisposeAllSceneContainers(false);

            SceneContainerCreated = null;
            SceneContainerDisposed = null;
            SceneServiceRegistered = null;

            _allServicesConcreteMap = null;
            _globalServiceContainer = null;
            _contextServiceContainers = new Dictionary<Context, ServiceContainer>();
            _sceneServiceContainers = new Dictionary<Scene, SceneServiceContainer>(SceneComparer.Instance);
            _persistentScene = default;
        }

        public static void PurgeContainer(Lifetime lifetime, Context context = default)
        {
            switch (lifetime)
            {
                case Lifetime.Global:
                {
                    _globalServiceContainer?.DisposeContainer();
                    _globalServiceContainer = null;
                    break;
                }
                case Lifetime.ScopedContext:
                {
                    if (_contextServiceContainers == null)
                    {
                        Log.Error("Context Service Map not initialized");
                        return;
                    }

                    if (_contextServiceContainers.TryGetValue(context, out var container) && container != null)
                    {
                        container.DisposeContainer();
                        _contextServiceContainers.Remove(context);
                    }

                    break;
                }
                case Lifetime.Scene:
                {
                    //There is no per-scene form here — DisposeSceneContainer(scene) is that.
                    //This disposes every scene container, the persistent one included.
                    DisposeAllSceneContainers(true);
                    break;
                }
                case Lifetime.PersistentScene:
                {
                    //_persistentScene stays captured: the DontDestroyOnLoad scene has not gone anywhere,
                    //so a later persistent registration should land in it rather than re-learn it.
                    DisposeSceneContainer(_persistentScene);
                    break;
                }
            }
        }

        
        /// <summary>
        ///     Initializes and populates the service container for the given <paramref name="lifetime"/>.
        ///     Call this when entering a new lifetime scope to instantiate and initialize all matching services.
        /// </summary>
        /// <param name="lifetime">The lifetime scope to discover services for.</param>
        /// <param name="context">Required when <paramref name="lifetime"/> is <see cref="Lifetime.ScopedContext"/>; ignored otherwise.</param>
        /// <remarks>
        ///     <see cref="Lifetime.Scene"/> is not supported here. Scene containers are created on the first
        ///     <see cref="RegisterSceneService{TService}(TService)"/> call for a scene and torn down by
        ///     <see cref="DisposeSceneContainer"/>.
        /// </remarks>
        public static void DiscoverServicesOfLifetime(Lifetime lifetime, Context context = default)
        {
            switch (lifetime)
            {
                case Lifetime.ScopedContext:
                {
                    if (context == default)
                    {
                        Log.Error("Context cannot be None for scoped context");
                        return;
                    }

                    if (_contextServiceContainers.TryGetValue(context, out var value) && value != null)
                    {
                        //Context Services already discovered.
                        return;
                    }

                    var newContextContainer = new ServiceContainer
                    (
                        Lifetime.ScopedContext,
                        _allServicesConcreteMap,
                        context
                    );
                    
                    _contextServiceContainers[context] = newContextContainer;
                    newContextContainer.OnEnteredContainerLifetime();

                    break;
                }
                case Lifetime.Global:
                {
                    if (_globalServiceContainer != null)
                    {
                        Log.Error("Global Services already Discovered");
                        return;
                    }

                    _globalServiceContainer = new ServiceContainer(Lifetime.Global, _allServicesConcreteMap);
                    _globalServiceContainer.OnEnteredContainerLifetime();
                    break;
                }
            }
        }

        private static void GetAllServiceAttributedTypes()
        {
            _allServicesConcreteMap = new Dictionary<Type, ServiceAttribute>();
            _serviceContextMap = new Dictionary<Type, Context>();
            _allServicesInterfaceMap = new Dictionary<Type, ServiceAttribute>();

            //Which concrete type already claimed each ServiceType, so a collision can name both sides
            var serviceTypeOwners = new Dictionary<Type, Type>();
            var assemblies = CurrentAssemblies.GetLoadedAssemblies();

            foreach (var assembly in assemblies)
            {
                var assemblyTypes = assembly.GetTypes();
                foreach (var type in assemblyTypes)
                {
                    if (!type.IsClass || type.IsAbstract)
                    {
                        continue;
                    }

                    if (!typeof(IService).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var attribute = (ServiceAttribute) Attribute.GetCustomAttribute(type, typeof(ServiceAttribute));
                    if (attribute == null)
                    {
                        continue;
                    }

                    if (!ImplementsDisposeCorrectly(type))
                    {
                        Log.Error($"{type.FullName} implements IDisposable.Dispose directly; implement DisposeService() instead. Skipping registration.");
                        continue;
                    }

                    //A bare Add here would throw out of RuntimeInitializeOnLoadMethod on the first
                    //collision and abandon discovery entirely, taking every other service with it.
                    if (serviceTypeOwners.TryGetValue(attribute.ServiceType, out var owner))
                    {
                        Log.Error($"{type.FullName} and {owner.FullName} are both registered as {attribute.ServiceType.FullName}. Keeping {owner.FullName} and skipping {type.FullName}.");
                        continue;
                    }

                    serviceTypeOwners.Add(attribute.ServiceType, type);
                    _allServicesConcreteMap.Add(type, attribute);
                    _allServicesInterfaceMap.Add(attribute.ServiceType, attribute);

                    //Only ScopedContext services have a meaningful Context. Mapping the others would
                    //point them at Context 0 and send context lookups into a container that never exists.
                    if (attribute.Lifetime == Lifetime.ScopedContext)
                    {
                        _serviceContextMap.Add(attribute.ServiceType, attribute.Context);
                    }
                }
            }
        }

        /// <summary>
        ///     Checks whether <paramref name="type"/> still relies on <see cref="IService"/>'s default
        ///     <see cref="IDisposable.Dispose"/> implementation rather than re-implementing it directly,
        ///     which would bypass service state-table cleanup.
        /// </summary>
        private static bool ImplementsDisposeCorrectly(Type type)
        {
            var map = type.GetInterfaceMap(typeof(IDisposable));
            var disposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));
            var index = Array.IndexOf(map.InterfaceMethods, disposeMethod);
            return map.TargetMethods[index].DeclaringType == typeof(IService);
        }

        #region Fetching Utilities

        /// <summary>
        ///     Resolves a ScopedContext service from the container for the context its
        ///     <see cref="ServiceAttribute"/> named. Returns <c>null</c> when that context has no live
        ///     container — it was purged, or never discovered.
        /// </summary>
        public static TService FetchContextService<TService>() where TService : class, IService
        {
            if (!_serviceContextMap.TryGetValue(typeof(TService), out var context))
            {
                return null;
            }

            //The context map is built once at discovery, so it still names a context whose container
            //has since been purged. Indexing that would throw rather than fail safe.
            if (!_contextServiceContainers.TryGetValue(context, out var serviceContainer) || serviceContainer == null)
            {
                Log.Error($"Context {context} has no live container, so {typeof(TService).FullName} cannot be resolved. Call DiscoverServicesOfLifetime for it first.");
                return null;
            }

            return serviceContainer.GetService<TService>();
        }

        /// <summary>
        ///     Resolves a Scene-lifetime service from <paramref name="scene"/>'s own container.
        ///     Strictly local: no other loaded scene's container is searched. Returns <c>null</c> when
        ///     <paramref name="scene"/> has no container.
        /// </summary>
        public static TService FetchSceneService<TService>(Scene scene) where TService : class, IService
        {
            return GetSceneContainer(scene)?.GetService<TService>();
        }

        /// <summary>
        ///     Resolves a Scene-lifetime service from the scene <paramref name="caller"/> lives in.
        ///     The ergonomic overload for MonoBehaviours resolving their own scene's services.
        /// </summary>
        public static TService FetchSceneService<TService>(this Component caller) where TService : class, IService
        {
            if (caller == null)
            {
                Log.Error($"Cannot resolve {typeof(TService).FullName} from a null Component. Pass an explicit Scene instead.");
                return null;
            }

            return FetchSceneService<TService>(caller.gameObject.scene);
        }

        /// <summary>
        ///     Resolves a <see cref="Lifetime.PersistentScene"/> service from the DontDestroyOnLoad scene's
        ///     container — the one scene container that outlives every real scene. Returns <c>null</c> until a
        ///     persistent service has registered (see <see cref="RegisterPersistentSceneService{TService}"/>).
        /// </summary>
        /// <remarks>
        ///     This is a separate, explicit lookup rather than a fallback inside the scene overloads: ordinary
        ///     scene resolution stays a strictly local <c>(Scene, Type)</c> answer, and reaching across into the
        ///     persistent container is visible at the call site.
        /// </remarks>
        public static TService FetchPersistentSceneService<TService>() where TService : class, IService
        {
            return FetchSceneService<TService>(_persistentScene);
        }

        public static TService FetchGlobalService<TService>() where TService : class, IService
        {
            return _globalServiceContainer.GetService<TService>();
        }

        /// <summary>
        ///     Safe variant of <see cref="FetchContextService{TService}"/> — returns <c>false</c> rather
        ///     than throwing when the context has no live container.
        /// </summary>
        public static bool TryGetContextService<TService>(out TService service) where TService : class, IService
        {
            service = null;
            if (!_serviceContextMap.TryGetValue(typeof(TService), out var context))
            {
                return false;
            }

            return _contextServiceContainers.TryGetValue(context, out var serviceContainer)
                   && serviceContainer != null
                   && serviceContainer.TryGetService(out service);
        }

        /// <summary>
        ///     Safe variant of <see cref="FetchSceneService{TService}(Scene)"/> — returns <c>false</c> when
        ///     <paramref name="scene"/> has no container or no <typeparamref name="TService"/> in it.
        /// </summary>
        public static bool TryGetSceneService<TService>(Scene scene, out TService service) where TService : class, IService
        {
            service = null;
            var container = GetSceneContainer(scene);
            return container != null && container.TryGetService(out service);
        }

        /// <summary>
        ///     Safe variant of <see cref="FetchSceneService{TService}(Component)"/>.
        /// </summary>
        public static bool TryGetSceneService<TService>(Component caller, out TService service) where TService : class, IService
        {
            service = null;
            if (caller == null)
            {
                Log.Error($"Cannot resolve {typeof(TService).FullName} from a null Component. Pass an explicit Scene instead.");
                return false;
            }

            return TryGetSceneService(caller.gameObject.scene, out service);
        }

        /// <summary>
        ///     Safe variant of <see cref="FetchPersistentSceneService{TService}"/> — returns <c>false</c> when no
        ///     persistent service of this type has registered.
        /// </summary>
        public static bool TryGetPersistentSceneService<TService>(out TService service) where TService : class, IService
        {
            return TryGetSceneService(_persistentScene, out service);
        }

        public static bool TryGetGlobalService<TService>(out TService service) where TService : class, IService
        {
            return _globalServiceContainer.TryGetService(out service);
        }

        #endregion

        #region Container Initialization Status

        public static bool IsGlobalContainerInitialized => _globalServiceContainer.ContainerInitialized;

        /// <summary>
        ///     <c>true</c> once <paramref name="scene"/> has a container, i.e. once at least one of its
        ///     services has registered and the scene has not been unloaded since.
        /// </summary>
        public static bool IsSceneContainerInitialized(Scene scene)
        {
            return GetSceneContainer(scene)?.ContainerInitialized ?? false;
        }

        public static bool IsContextContainerInitialized(Context context)
        {
            if (_contextServiceContainers.TryGetValue(context, out var container) && container != null)
            {
                return container.ContainerInitialized;
            }

            return false;
        }

        #endregion

        #region Scene Containers

        /// <summary>
        ///     Registers a Scene-lifetime service against the scene its own <see cref="GameObject"/> lives in.
        ///     The scene's container is created on the first such registration, so this is safe to call from
        ///     <c>Awake</c> with no prior setup.
        /// </summary>
        /// <remarks>
        ///     <paramref name="service"/> must be a <see cref="Component"/>. A non-Component scene service has
        ///     no scene to infer and must use <see cref="RegisterSceneService{TService}(TService, Scene)"/>.
        /// </remarks>
        public static void RegisterSceneService<TService>(TService service) where TService : class, IService
        {
            if (service is not Component component)
            {
                Log.Error($"{typeof(TService).FullName} is not a Component, so its scene cannot be inferred. Call RegisterSceneService(service, scene) with an explicit scene.");
                return;
            }

            //`is Component` is a CLR check, so a destroyed component passes it — Unity's null operator catches that
            if (component == null)
            {
                Log.Error($"Cannot infer a scene for {typeof(TService).FullName} from a destroyed Component. Call RegisterSceneService(service, scene) with an explicit scene.");
                return;
            }

            RegisterSceneService(service, component.gameObject.scene);
        }

        /// <summary>
        ///     Registers a Scene-lifetime service against an explicit <paramref name="scene"/>, creating that
        ///     scene's container if it does not exist yet.
        /// </summary>
        public static void RegisterSceneService<TService>(TService service, Scene scene) where TService : class, IService
        {
            //A stored null survives until DisposeContainer walks the map and dereferences it, so the
            //failure would surface at teardown with nothing left to point at the registration.
            //TService is an unconstrained type parameter here, so this is a plain reference check —
            //a destroyed Component is caught by the single-argument overload instead.
            if (service == null)
            {
                Log.Error($"Cannot register a null {typeof(TService).FullName} as a Scene service.");
                return;
            }

            GetOrCreateSceneContainer(scene, Lifetime.Scene)?.RegisterService<TService>(service);
        }

        /// <summary>
        ///     Registers a <see cref="Lifetime.PersistentScene"/> service into the DontDestroyOnLoad scene's
        ///     container, so one instance serves the whole session instead of being duplicated per scene. This is
        ///     the home for authored objects that must be GameObjects but are app-lived — loading overlays, audio
        ///     rigs — and it is resolved with <see cref="FetchPersistentSceneService{TService}"/>, never by the
        ///     scene overloads.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         This calls <see cref="UnityEngine.Object.DontDestroyOnLoad"/> on <paramref name="service"/>
        ///         itself, so there is no ordering to remember and no way to register a "persistent" service that
        ///         still dies with its scene. Registering an object that is already there is a no-op move.
        ///     </para>
        ///     <para>
        ///         That move cannot be undone, so every reason to refuse is checked before it: a rejected service
        ///         that had already been moved would sit outside every scene, alive and unreachable, for the rest
        ///         of the session.
        ///     </para>
        ///     <para>
        ///         Unity only honours DontDestroyOnLoad for root GameObjects, so <paramref name="service"/> must
        ///         be on one; a nested component is rejected rather than left behind in a scene that unloads.
        ///     </para>
        ///     <para>
        ///         Unity exposes no handle for the DontDestroyOnLoad scene, so <b>the first accepted call to this
        ///         method is what teaches the locator which scene that is</b> — it is read off the registrant once
        ///         the move above has put it there. The captured scene is cleared by <see cref="GameStart"/>, so
        ///         it is re-learned once per session.
        ///     </para>
        /// </remarks>
        public static void RegisterPersistentSceneService<TService>(TService service) where TService : class, IService
        {
            if (service is not Component component)
            {
                Log.Error($"{typeof(TService).FullName} is not a Component, so it cannot be moved to DontDestroyOnLoad. A persistent service must be a Component on a root GameObject.");
                return;
            }

            if (component == null)
            {
                Log.Error($"Cannot register {typeof(TService).FullName} as a persistent service from a destroyed Component.");
                return;
            }

            if (component.transform.parent != null)
            {
                Log.Error($"{typeof(TService).FullName} is not on a root GameObject, so Unity will not move it to DontDestroyOnLoad. Put the service on a root object, or register it against its own scene with RegisterSceneService.");
                return;
            }

            //Last line at which refusing is still free — everything past DontDestroyOnLoad is irreversible.
            if (!CanRegisterPersistentService<TService>())
            {
                return;
            }

            UnityEngine.Object.DontDestroyOnLoad(component.gameObject);

            //Only correct after the move above: the registrant now defines what the persistent scene is.
            if (!_persistentScene.IsValid())
            {
                _persistentScene = component.gameObject.scene;
            }

            GetOrCreateSceneContainer(_persistentScene, Lifetime.PersistentScene)?.RegisterService<TService>(service);
        }

        /// <summary>
        ///     Runs the checks <see cref="SceneServiceContainer.RegisterService{T}"/> would run, ahead of the
        ///     irreversible DontDestroyOnLoad move. Deliberately duplicated rather than delegated: the container
        ///     is public API and has to keep validating its own registrations, but by the time it does, the
        ///     GameObject has already left its scene for good.
        /// </summary>
        private static bool CanRegisterPersistentService<TService>() where TService : class, IService
        {
            var serviceType = typeof(TService);
            if (_allServicesInterfaceMap == null || !_allServicesInterfaceMap.TryGetValue(serviceType, out var attribute))
            {
                Log.Error($"Service {serviceType.FullName} is not marked with a ServiceAttribute");
                return false;
            }

            if (attribute.Lifetime != Lifetime.PersistentScene)
            {
                Log.Error($"Service {serviceType.FullName} is marked {attribute.Lifetime}, not {Lifetime.PersistentScene}. Mark it [Service(Lifetime.PersistentScene, ...)], or register it against its own scene with RegisterSceneService.");
                return false;
            }

            var persistentContainer = _persistentScene.IsValid() ? GetSceneContainer(_persistentScene) : null;
            if (persistentContainer != null && persistentContainer.TryGetService<TService>(out _))
            {
                Log.Error($"Service {serviceType.FullName} is already registered as a persistent service");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Disposes every service owned by <paramref name="scene"/> and drops its container.
        /// </summary>
        /// <remarks>
        ///     Call this before unloading <paramref name="scene"/>, while its services are still live objects.
        ///     The locator does not watch <see cref="SceneManager.sceneUnloaded"/> — scene lifetime is the
        ///     caller's to drive, exactly like <see cref="PurgeContainer"/> for a <see cref="Lifetime.ScopedContext"/>.
        ///     A scene left undisposed keeps its container alive under a <see cref="Scene"/> handle that Unity
        ///     may recycle, so a later scene reusing that handle would inherit the stale container.
        /// </remarks>
        public static void DisposeSceneContainer(Scene scene)
        {
            if (_sceneServiceContainers == null || !_sceneServiceContainers.Remove(scene, out var container))
            {
                return;
            }

            container?.DisposeContainer();
            SceneContainerDisposed?.Invoke(scene);
        }

        private static SceneServiceContainer GetSceneContainer(Scene scene)
        {
            if (_sceneServiceContainers != null && _sceneServiceContainers.TryGetValue(scene, out var container))
            {
                return container;
            }

            return null;
        }

        private static SceneServiceContainer GetOrCreateSceneContainer(Scene scene, Lifetime containerLifetime)
        {
            if (_sceneServiceContainers == null)
            {
                Log.Error("Scene Service Containers were not initialized");
                return null;
            }

            //Every invalid scene shares the same empty handle, so they would all collapse into one container
            if (!scene.IsValid())
            {
                Log.Error("Cannot create a Scene Service Container for an invalid scene");
                return null;
            }

            if (_sceneServiceContainers.TryGetValue(scene, out var container) && container != null)
            {
                return container;
            }

            container = new SceneServiceContainer(_allServicesInterfaceMap, scene, containerLifetime);
            container.SceneServiceRegistered += (serviceType, service) => SceneServiceRegistered?.Invoke(scene, serviceType, service);
            _sceneServiceContainers[scene] = container;
            SceneContainerCreated?.Invoke(scene);
            return container;
        }

        private static void DisposeAllSceneContainers(bool notify)
        {
            if (_sceneServiceContainers == null)
            {
                return;
            }

            //Snapshot and clear first, so handlers observe a settled map and cannot mutate the one being iterated
            var containers = new List<SceneServiceContainer>(_sceneServiceContainers.Values);
            _sceneServiceContainers.Clear();

            foreach (var container in containers)
            {
                if (container == null)
                {
                    continue;
                }

                var scene = container.Scene;
                container.DisposeContainer();

                if (notify)
                {
                    SceneContainerDisposed?.Invoke(scene);
                }
            }
        }

        /// <summary>
        ///     <see cref="Scene"/> implements no interfaces — notably not <see cref="IEquatable{T}"/> — so
        ///     <c>EqualityComparer&lt;Scene&gt;.Default</c> falls back to the reflection-based object comparer and
        ///     boxes the key on every single lookup. Scene resolution is called from gameplay code, so it must not
        ///     allocate; comparing the struct directly keeps it free.
        /// </summary>
        private sealed class SceneComparer : IEqualityComparer<Scene>
        {
            public static readonly SceneComparer Instance = new SceneComparer();

            public bool Equals(Scene x, Scene y)
            {
                return x == y;
            }

            public int GetHashCode(Scene scene)
            {
                return scene.GetHashCode();
            }
        }

        #endregion

        public static void SubscribeToContextServiceSetup(Context context, Action onSetup)
        {
            if (!_contextServiceContainers.TryGetValue(context, out var container) || container == null)
            {
                Log.Error($"Service Container for Context: {context} has not been created yet.");
                return;
            }

            container.ContainerServicesInitialized += onSetup;
        }

        public static void UnsubscribeToContextServiceSetup(Context context, Action onSetup)
        {
            if (!_contextServiceContainers.TryGetValue(context, out var container) || container == null)
            {
                return;
            }

            container.ContainerServicesInitialized -= onSetup;
        }
    }
}