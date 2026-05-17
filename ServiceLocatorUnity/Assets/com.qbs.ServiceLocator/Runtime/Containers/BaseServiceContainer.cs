using System;
using System.Collections.Generic;

namespace QBS.ServiceLocator
{
	
	/// <summary>
	/// Defines the lifetime scope of a service, determining when it is created and destroyed.
	/// Services can be Global (application lifetime), Scene (per-scene lifetime),
	/// ScopedContext (custom context lifetime), or None (invalid/unset).
	/// </summary>
	public enum Lifetime
	{
		None,
		Scene,
		ScopedContext,
		Global,
	}

	/// <summary>
	/// Defines the contract for service containers that manage collections of services.
	/// Provides service storage, retrieval, and lifecycle management for a specific lifetime and context.
	/// </summary>
	public abstract class BaseServiceContainer
	{
		/// <summary>
		/// Gets the lifetime of the container.
		/// </summary>
		protected Lifetime ContainerLifetime;

		/// <summary>
		/// Gets the context of the container.
		/// </summary>
		protected Context ContainerContext;

		/// <summary>
		/// Gets a dictionary of services mapped by their types.
		/// </summary>
		protected Dictionary<Type, IService> ContainedServices;

		/// <summary>
		/// Gets a dictionary of all services with their attributes.
		/// </summary>
		protected Dictionary<Type, ServiceAttribute> AllServicesMap;

		/// <summary>
		/// Gets a value indicating whether the container has been initialized.
		/// </summary>
		public virtual bool ContainerInitialized { get; protected set; }

		public TService GetService<TService>() where TService : class, IService
		{
			return (TService)ContainedServices[typeof(TService)];
		}

		public bool TryGetService<TService>(out TService service) where TService : class, IService
		{
			service = null;
			var found = ContainedServices.TryGetValue(typeof(TService), out var serviceInstance);
			if (!found)
			{
				return false;
			}

			service = (TService)serviceInstance;
			return true;
		}

		public virtual void DisposeContainer()
		{
			foreach (var (_, service) in ContainedServices)
			{
				service.Dispose();
			}
			
			ContainedServices.Clear();
			ContainerInitialized = false;
		}
	}
}