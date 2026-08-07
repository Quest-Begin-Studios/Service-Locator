using System;
using System.Collections.Generic;
using QBS.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Specialized service container for Scene-lifetime services that require manual registration. <br/><br/>
	///     Unlike other containers, scene services are not auto-discovered and must be explicitly registered,
	///     allowing services to manage their own initialization lifecycle within scene contexts.
	///     One container exists per loaded scene, so the same service interface may be registered
	///     independently by several concurrently loaded scenes.
	///     A container holds exactly one of <see cref="Lifetime.Scene"/> or <see cref="Lifetime.PersistentScene"/>
	///     and rejects services marked with the other, so a service's attribute — not its registration call
	///     site — remains the single source of truth for how long it lives.
	/// </summary>
	public class SceneServiceContainer : BaseServiceContainer
	{
		//Services that register to a scene service container handle their own initializations
		public override bool ContainerInitialized => true;

		/// <summary>
		///     The scene whose services this container owns.
		/// </summary>
		public Scene Scene { get; }

		public event Action<Type, IService> SceneServiceRegistered;

		// Keyed by ServiceType (the interface, not concrete types) against interfaces
		private readonly Dictionary<Type, ServiceAttribute> _serviceAttributeMap;

		public SceneServiceContainer(Dictionary<Type, ServiceAttribute> serviceAttributeMap, Scene scene,
			Lifetime containerLifetime = Lifetime.Scene)
		{
			ContainedServices = new Dictionary<Type, IService>();
			_serviceAttributeMap = serviceAttributeMap;
			Scene = scene;
			ContainerLifetime = containerLifetime;
		}

		public void RegisterService<T>(IService service) where T : class, IService
		{
			var serviceType = typeof(T);
			if (ContainedServices.ContainsKey(serviceType))
			{
				Log.Error($"Service {serviceType.FullName} already registered for this scene context");
				return;
			}

			if (!_serviceAttributeMap.TryGetValue(serviceType, out var serviceAttribute))
			{
				Log.Error($"Service {serviceType.FullName} is not marked with an ServiceAttribute");
				return;
			}

			if (serviceAttribute.Lifetime != ContainerLifetime)
			{
				Log.Error($"Service {serviceType.FullName} is marked {serviceAttribute.Lifetime}, but this container only holds {ContainerLifetime} services");
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