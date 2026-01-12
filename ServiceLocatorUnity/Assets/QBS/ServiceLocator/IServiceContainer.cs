using System;
using System.Collections.Generic;

namespace QBS.ServiceLocator
{
	public interface IServiceContainer
	{
		public Lifetime ContainerLifetime { get; }
		public Context ContainerContext { get; }
		public Dictionary<Type, IService> ServicesMap { get; }
		public Dictionary<Type, ServiceAttribute> AllServices { get; }

		public bool ContainerInitialized { get; }

		public TService GetService<TService>() where TService : class, IService
		{
			return (TService)ServicesMap[typeof(TService)];
		}

		public bool TryGetService<TService>(out TService service) where TService : class, IService
		{
			service = null;
			var found = ServicesMap.TryGetValue(typeof(TService), out var serviceInstance);
			if (!found)
			{
				return false;
			}

			service = (TService)serviceInstance;
			return true;
		}

		public void DisposeContainer();
	}
}