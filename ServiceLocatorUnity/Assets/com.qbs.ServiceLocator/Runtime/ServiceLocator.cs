using System;
using System.Collections.Generic;
using QBS.Core;
using UnityEngine;
using UnityEngine.Assemblies;

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

        #endregion

        #region Containers

        private static ServiceContainer _globalServiceContainer;
        private static Dictionary<Context, ServiceContainer> _contextServiceContainers;
        private static SceneServiceContainer _sceneServiceContainer;

        #endregion

        private static Dictionary<Type, ServiceAttribute> _allServicesMap;
        private static Dictionary<Type, Context> _serviceContextMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void GameStart()
        {
            CleanupStatics();
            GetAllServiceAttributedTypes();
            DiscoverServicesOfLifetime(Lifetime.Global);
        }

        private static void CleanupStatics()
        {
            _allServicesMap?.Clear();
            _globalServiceContainer?.DisposeContainer();

            if (_contextServiceContainers != null)
            {
                foreach (var (_, container) in _contextServiceContainers)
                {
                    container?.DisposeContainer();
                }
            }
            
            Context.ClearRegisteredContexts();
            IService.CleanupConfigStateTable();

            _sceneServiceContainer?.DisposeContainer();

            _allServicesMap = null;
            _globalServiceContainer = null;
            _contextServiceContainers = null;
            _sceneServiceContainer = null;
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
                    _sceneServiceContainer?.DisposeContainer();
                    _sceneServiceContainer = null;
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
        ///     <see cref="Lifetime.Scene"/> is not supported here. Use <see cref="RefreshSceneServiceContainer"/> and
        ///     <see cref="RegisterSceneService{TService}"/> to manage scene-lifetime services instead.
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
                        _allServicesMap,
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

                    _globalServiceContainer = new ServiceContainer(Lifetime.Global, _allServicesMap);
                    _globalServiceContainer.OnEnteredContainerLifetime();
                    break;
                }
            }
        }

        private static void GetAllServiceAttributedTypes()
        {
            _allServicesMap = new Dictionary<Type, ServiceAttribute>();
            _serviceContextMap = new Dictionary<Type, Context>();
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

                    _allServicesMap.Add(type, attribute);
                    _serviceContextMap.Add(attribute.ServiceType, attribute.Context);
                }
            }
        }

        #region Fetching Utilities

        public static TService FetchContextService<TService>() where TService : class, IService
        {
            if (_serviceContextMap.TryGetValue(typeof(TService), out var context))
            {
                var serviceContainer = _contextServiceContainers[context];
                return serviceContainer.GetService<TService>();
            }

            return null;
        }

        public static TService FetchSceneService<TService>() where TService : class, IService
        {
            return _sceneServiceContainer?.GetService<TService>();
        }

        public static TService FetchGlobalService<TService>() where TService : class, IService
        {
            return _globalServiceContainer.GetService<TService>();
        }

        public static bool TryGetContextService<TService>(out TService service) where TService : class, IService
        {
            service = null;
            if (_serviceContextMap.TryGetValue(typeof(TService), out var context))
            {
                var serviceContainer = _contextServiceContainers[context];
                return serviceContainer.TryGetService(out service);
            }

            return false;
        }

        public static bool TryGetSceneService<TService>(out TService service) where TService : class, IService
        {
            service = null;
            return _sceneServiceContainer != null && _sceneServiceContainer.TryGetService(out service);
        }

        public static bool TryGetGlobalService<TService>(out TService service) where TService : class, IService
        {
            return _globalServiceContainer.TryGetService(out service);
        }

        #endregion

        #region Container Initialization Status

        public static bool IsGlobalContainerInitialized => _globalServiceContainer.ContainerInitialized;

        public static bool IsSceneContainerInitialized => _sceneServiceContainer?.ContainerInitialized ?? false;

        public static bool IsContextContainerInitialized(Context context)
        {
            if (_contextServiceContainers.TryGetValue(context, out var container) && container != null)
            {
                return container.ContainerInitialized;
            }

            return false;
        }

        #endregion

        public static void RegisterSceneService<TService>(TService service) where TService : class, IService
        {
            if (_sceneServiceContainer == null)
            {
                Log.Error("Scene Service Container for scene was not initialized");
                return;
            }

            _sceneServiceContainer.RegisterService<TService>(service);
        }

        public static void RefreshSceneServiceContainer()
        {
            //Force dispose scene container
            _sceneServiceContainer?.DisposeContainer();
            _sceneServiceContainer = new SceneServiceContainer(_allServicesMap);
        }

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