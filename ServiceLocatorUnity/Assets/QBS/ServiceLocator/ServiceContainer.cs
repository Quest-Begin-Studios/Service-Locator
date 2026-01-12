using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace QBS.ServiceLocator
{
	public class ServiceContainer : IServiceContainer
	{
		public Context ContainerContext { get; }
		public Dictionary<Type, IService> ServicesMap { get; }
		public Dictionary<Type, ServiceAttribute> AllServices { get; }
		public bool ContainerInitialized { get; private set; }
		public Lifetime ContainerLifetime { get; }
		public event Action ContainerServicesInitialized;

		public ServiceContainer(Lifetime containerLifetime, Dictionary<Type, ServiceAttribute> allServices,
			Context context = Context.None)
		{
			ServicesMap = new Dictionary<Type, IService>();
			ContainerLifetime = containerLifetime;
			ContainerContext = context;
			AllServices = allServices;
		}

        /// <summary>
        ///     Called when entering this container's context. Populates the service map and initializes all services.
        /// </summary>
        public void OnEnteredContainerLifetime()
		{
			if (ContainerLifetime == Lifetime.None)
			{
				Debug.LogError("Container Setup Incorrectly");
				return;
			}

			PopulateMapWithServicesOfLifetime();
			InitializeServices();
		}

        /// <summary>
        ///     Populates the ServicesMap with service instances that match this container's context.
        ///     Creates instances using parameterless constructors via reflection.
        /// </summary>
        private void PopulateMapWithServicesOfLifetime()
		{
			foreach (var (actualType, serviceAttribute) in AllServices)
			{
				if (serviceAttribute.Lifetime != ContainerLifetime)
				{
					continue;
				}

				if (ContainerLifetime == Lifetime.ScopedContext)
				{
					if (serviceAttribute.Context != ContainerContext)
					{
						continue;
					}
				}

				var serviceType = serviceAttribute.ServiceType;
				if (ServicesMap.ContainsKey(serviceType))
				{
					continue;
				}

				try
				{

					var serviceObject = Activator.CreateInstance(actualType);
					if (serviceObject is not IService serviceInstance)
					{
						Debug.LogError($"Service {serviceType.FullName} does not implement IService");
						continue;
					}

					ServicesMap.Add(serviceType, serviceInstance);
				}
				catch (Exception e)
				{
					Debug.LogError(e.Message);
					throw;
				}
			}
		}

        /// <summary>
        ///     Initializes all services in the container. Synchronous services are initialized first,
        ///     followed by asynchronous services. Invokes ContainerServicesInitialized event when complete.
        /// </summary>
        private void InitializeServices()
		{
			List<Task> asyncInitializations = new();
			foreach (var (_, serviceInstance) in ServicesMap)
			{
				if (!serviceInstance.IsAsyncInit)
				{
					serviceInstance.Initialize();
				}
			}

			foreach (var (_, serviceInstance) in ServicesMap)
			{
				if (serviceInstance.IsAsyncInit)
				{
					asyncInitializations.Add(serviceInstance.InitializeAsync());
				}
			}

			if (asyncInitializations.Count == 0)
			{
				ContainerInitialized = true;
				ContainerServicesInitialized?.Invoke();
			}
			else
			{
				// Initialize services that require time to be setup
				// but do not block main thread. 
				_ = HandleAsyncInitializations(asyncInitializations);
			}
		}

		private async Task HandleAsyncInitializations(List<Task> asyncInitializations)
		{
			await Task.WhenAll(asyncInitializations);
			ContainerInitialized = true;
			ContainerServicesInitialized?.Invoke();
		}

		public void DisposeContainer()
		{
			foreach (var (_, service) in ServicesMap)
			{
				if (service is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}

			ServicesMap.Clear();
			ContainerInitialized = false;
			ContainerServicesInitialized = null;
		}
	}
}