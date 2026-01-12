using System;
using System.Collections.Generic;
using UnityEngine;

namespace QBS.ServiceLocator
{
	public class SceneServiceContainer : IServiceContainer
	{
		public Lifetime ContainerLifetime => Lifetime.Scene;
		public Context ContainerContext => Context.None;
		public Dictionary<Type, IService> ServicesMap { get; }
		public Dictionary<Type, ServiceAttribute> AllServices { get; }

		//Services that register to a scene service container handle their own initializations 
		public bool ContainerInitialized => true;

		public event Action<Type, IService> SceneServiceRegistered;

		public SceneServiceContainer(Dictionary<Type, ServiceAttribute> allServices)
		{
			ServicesMap = new Dictionary<Type, IService>();
			AllServices = allServices;
		}

		public void RegisterService<T>(IService service) where T : class, IService
		{
			var serviceType = typeof(T);
			if (ServicesMap.ContainsKey(serviceType))
			{
				Debug.LogError($"Service {serviceType.FullName} already registered for this scene context");
			}

			if (!AllServices.TryGetValue(typeof(T), out var serviceAttribute))
			{
				Debug.Log($"Service {serviceType.FullName} is not marked with an ServiceAttribute");
				return;
			}

			if (serviceAttribute.Lifetime != Lifetime.Scene)
			{
				Debug.LogError($"Service {serviceType.FullName} is not a Scene context service");
				return;
			}

			ServicesMap.Add(serviceType, service);
			SceneServiceRegistered?.Invoke(serviceType, service);
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
			SceneServiceRegistered = null;
		}
	}
}