using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using QBS.Core;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Standard implementation of IServiceContainer for managing Global and ScopedContext services.
	///     Handles automatic service discovery, instantiation via reflection, and initialization of services
	///     within a specific lifetime scope. Supports both synchronous and asynchronous service initialization.
	/// </summary>
	public class ServiceContainer : BaseServiceContainer
	{
		public IReadOnlyDictionary<Type, ServiceAttribute> AllServices { get; }
		public event Action ContainerServicesInitialized;

		public ServiceContainer(Lifetime containerLifetime,
			Dictionary<Type, ServiceAttribute> allServices,
			Context context = default)
		{
			ContainedServices = new Dictionary<Type, IService>();
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
				Log.Error("Container Setup Incorrectly");
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
				if (ContainedServices.ContainsKey(serviceType))
				{
					continue;
				}

				try
				{

					var serviceObject = Activator.CreateInstance(actualType);
					if (serviceObject is not IService serviceInstance)
					{
						Log.Error($"Service {serviceType.FullName} does not implement IService");
						continue;
					}

					ContainedServices.Add(serviceType, serviceInstance);
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
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
			List<UniTask> asyncInitializations = new();
			foreach (var (_, serviceInstance) in ContainedServices)
			{
				if (!serviceInstance.IsAsyncInit)
				{
					serviceInstance.Initialize();
				}
			}

			foreach (var (_, serviceInstance) in ContainedServices)
			{
				if (serviceInstance.IsAsyncInit)
				{
					asyncInitializations.Add(serviceInstance.InitializeAsyncWrapper());
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
				HandleAsyncInitializations(asyncInitializations).Forget();
			}
		}
		
		private async UniTaskVoid HandleAsyncInitializations(List<UniTask> asyncInitializations)
		{
			try
			{
				await UniTask.WhenAll(asyncInitializations);
				ContainerInitialized = true;
				ContainerServicesInitialized?.Invoke();
			}
			catch (Exception e)
			{
				Log.Error(e.ToString());
			}
		}

		public override void DisposeContainer()
		{
			base.DisposeContainer();
			ContainerServicesInitialized = null;
		}
	}
}