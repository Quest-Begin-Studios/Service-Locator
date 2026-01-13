using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Standard implementation of IServiceContainer for managing Global and ScopedContext services.
	///     Handles automatic service discovery, instantiation via reflection, and initialization of services
	///     within a specific lifetime scope. Supports both synchronous and asynchronous service initialization.
	/// </summary>
	public class ServiceContainer : BaseServiceContainer
	{
		public Dictionary<Type, ServiceAttribute> AllServices { get; }
		public event Action ContainerServicesInitialized;

		public ServiceContainer(Lifetime containerLifetime,
			Dictionary<Type, ServiceAttribute> allServices,
			Context context = Context.None)
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
				if (ContainedServices.ContainsKey(serviceType))
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

					ContainedServices.Add(serviceType, serviceInstance);
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

		public override void DisposeContainer()
		{
			base.DisposeContainer();
			ContainerServicesInitialized = null;
		}
	}
}