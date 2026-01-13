using System;
using System.Collections.Generic;
using UnityEngine;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Specialized service container for Scene-lifetime services that require manual registration. <br/><br/>
	///     Unlike other containers, scene services are not auto-discovered and must be explicitly registered,
	///     allowing services to manage their own initialization lifecycle within scene contexts.
	/// </summary>
	public class SceneServiceContainer : BaseServiceContainer
	{
		//Services that register to a scene service container handle their own initializations 
		public override bool ContainerInitialized => true;

		public event Action<Type, IService> SceneServiceRegistered;

		public SceneServiceContainer(Dictionary<Type, ServiceAttribute> allServices)
		{
			ContainedServices = new Dictionary<Type, IService>();
			AllServicesMap = allServices;
		}

		public void RegisterService<T>(IService service) where T : class, IService
		{
			var serviceType = typeof(T);
			if (ContainedServices.ContainsKey(serviceType))
			{
				Debug.LogError($"Service {serviceType.FullName} already registered for this scene context");
			}

			if (!AllServicesMap.TryGetValue(typeof(T), out var serviceAttribute))
			{
				Debug.Log($"Service {serviceType.FullName} is not marked with an ServiceAttribute");
				return;
			}

			if (serviceAttribute.Lifetime != Lifetime.Scene)
			{
				Debug.LogError($"Service {serviceType.FullName} is not a Scene context service");
				return;
			}

			ContainedServices.Add(serviceType, service);
			SceneServiceRegistered?.Invoke(serviceType, service);
		}

		public override void DisposeContainer()
		{
			base.DisposeContainer();
			SceneServiceRegistered = null;
		}
	}
}