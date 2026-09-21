using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using QBS.Core;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Standard implementation of IServiceContainer for managing Global and ScopedContext services.
	///     Discovers the services of its own lifetime, constructs each one by passing the services its
	///     constructor asks for, and initializes them in that same dependency order. A service with a
	///     parameterless constructor is untouched by any of it and may keep fetching what it needs.
	/// </summary>
	public class ServiceContainer : BaseServiceContainer
	{
		public event Action ContainerServicesInitialized;

		//Construction order, which initialisation then follows, so nothing is initialised before what it
		//was handed. Only dependencies inside this container are ordered: an outer Global service was
		//constructed before this container existed.
		private readonly List<IService> _initializationOrder = new();
		private readonly Dictionary<IService, List<IService>> _inContainerDependencies = new();

		private Dictionary<Type, ServiceAttribute> _attributeByServiceType;

		public ServiceContainer(Lifetime containerLifetime,
			Dictionary<Type, ServiceAttribute> allServices,
			Context context = default)
		{
			ContainedServices = new Dictionary<Type, IService>();
			ContainerLifetime = containerLifetime;
			ContainerContext = context;
			AllServicesMap = allServices;
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

			IndexServicesByServiceType();
			PopulateMapWithServicesOfLifetime();
			InitializeServices();
		}

		/// <summary>
		///     AllServicesMap is keyed by concrete type; constructor parameters name interfaces. This is the
		///     other direction, over services of every lifetime, so a parameter can be validated against what
		///     the whole application registers rather than only what this container holds.
		/// </summary>
		private void IndexServicesByServiceType()
		{
			_attributeByServiceType = new Dictionary<Type, ServiceAttribute>(AllServicesMap.Count);
			foreach (var (_, attribute) in AllServicesMap)
			{
				_attributeByServiceType[attribute.ServiceType] = attribute;
			}
		}

		/// <summary>
		///     Populates the ServicesMap with service instances that match this container's context,
		///     in an order where every constructor argument already exists.
		/// </summary>
		private void PopulateMapWithServicesOfLifetime()
		{
			var plans = CollectConstructionPlans();
			foreach (var plan in SortByDependencyOrder(plans))
			{
				Construct(plan);
			}
		}

		private List<ConstructionPlan> CollectConstructionPlans()
		{
			var plans = new List<ConstructionPlan>();
			var claimedServiceTypes = new HashSet<Type>();

			foreach (var (concreteType, serviceAttribute) in AllServicesMap)
			{
				if (serviceAttribute.Lifetime != ContainerLifetime)
				{
					continue;
				}

				if (ContainerLifetime == Lifetime.ScopedContext && serviceAttribute.Context != ContainerContext)
				{
					continue;
				}

				if (!claimedServiceTypes.Add(serviceAttribute.ServiceType))
				{
					continue;
				}

				if (!TrySelectConstructor(concreteType, out var constructor))
				{
					continue;
				}

				var plan = new ConstructionPlan(concreteType, serviceAttribute.ServiceType, constructor);
				if (!TryPlanParameters(plan))
				{
					continue;
				}

				plans.Add(plan);
			}

			return plans;
		}

		private static bool TrySelectConstructor(Type concreteType, out ConstructorInfo constructor)
		{
			constructor = null;
			var constructors = concreteType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

			if (constructors.Length == 0)
			{
				Log.Error($"{concreteType.FullName} has no public constructor, so it cannot be constructed. Skipping registration.");
				return false;
			}

			if (constructors.Length == 1)
			{
				constructor = constructors[0];
				return true;
			}

			foreach (var candidate in constructors)
			{
				if (!candidate.IsDefined(typeof(ServiceConstructorAttribute), false))
				{
					continue;
				}

				if (constructor != null)
				{
					Log.Error($"{concreteType.FullName} marks more than one constructor [ServiceConstructor]. Skipping registration.");
					constructor = null;
					return false;
				}

				constructor = candidate;
			}

			if (constructor == null)
			{
				Log.Error($"{concreteType.FullName} has {constructors.Length} public constructors; mark the one the locator should use with [ServiceConstructor]. Skipping registration.");
				return false;
			}

			return true;
		}

		private bool TryPlanParameters(ConstructionPlan plan)
		{
			foreach (var parameter in plan.Constructor.GetParameters())
			{
				var parameterType = parameter.ParameterType;

				if (!_attributeByServiceType.TryGetValue(parameterType, out var dependencyAttribute))
				{
					Log.Error($"{plan.ConcreteType.FullName} takes {parameterType.FullName}, which no service registers. A service constructor may only take service interfaces. Skipping registration.");
					return false;
				}

				if (!IsResolvableFromHere(dependencyAttribute, out var isInThisContainer))
				{
					Log.Error($"{plan.ConcreteType.FullName} ({DescribeThisContainer()}) depends on {parameterType.FullName} ({DescribeScope(dependencyAttribute)}), which it cannot reach: a service may only depend on its own container or on Global. Skipping registration.");
					return false;
				}

				plan.ParameterServiceTypes.Add(parameterType);
				if (isInThisContainer)
				{
					plan.InContainerDependencies.Add(parameterType);
				}
			}

			return true;
		}

		/// <summary>
		///     Whether this container can hand a service of <paramref name="dependency" />'s lifetime to one of
		///     its own. Global is the outer scope everything may reach; a scoped service may reach its own
		///     context; Scene and PersistentScene services register themselves from Awake and are never
		///     injected, because no discovered service can know when they exist.
		/// </summary>
		private bool IsResolvableFromHere(ServiceAttribute dependency, out bool isInThisContainer)
		{
			isInThisContainer = false;

			switch (dependency.Lifetime)
			{
				case Lifetime.Global:
					isInThisContainer = ContainerLifetime == Lifetime.Global;
					return true;

				case Lifetime.ScopedContext:
					if (ContainerLifetime != Lifetime.ScopedContext || dependency.Context != ContainerContext)
					{
						return false;
					}

					isInThisContainer = true;
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		///     Kahn's algorithm over the dependencies inside this container. A plan whose dependency has no
		///     plan of its own is dropped first, repeatedly, so a service is never constructed against
		///     something that was itself skipped; whatever a cycle leaves behind is reported and dropped.
		/// </summary>
		private List<ConstructionPlan> SortByDependencyOrder(List<ConstructionPlan> plans)
		{
			var planByServiceType = new Dictionary<Type, ConstructionPlan>(plans.Count);
			foreach (var plan in plans)
			{
				planByServiceType[plan.ServiceType] = plan;
			}

			DropPlansWithMissingDependencies(plans, planByServiceType);

			var pendingDependencyCount = new Dictionary<Type, int>(plans.Count);
			var dependents = new Dictionary<Type, List<ConstructionPlan>>(plans.Count);
			foreach (var plan in plans)
			{
				pendingDependencyCount[plan.ServiceType] = plan.InContainerDependencies.Count;
				dependents[plan.ServiceType] = new List<ConstructionPlan>();
			}

			foreach (var plan in plans)
			{
				foreach (var dependencyType in plan.InContainerDependencies)
				{
					dependents[dependencyType].Add(plan);
				}
			}

			var ready = new Queue<ConstructionPlan>();
			foreach (var plan in plans)
			{
				if (pendingDependencyCount[plan.ServiceType] == 0)
				{
					ready.Enqueue(plan);
				}
			}

			var ordered = new List<ConstructionPlan>(plans.Count);
			while (ready.Count > 0)
			{
				var plan = ready.Dequeue();
				ordered.Add(plan);

				foreach (var dependent in dependents[plan.ServiceType])
				{
					if (--pendingDependencyCount[dependent.ServiceType] == 0)
					{
						ready.Enqueue(dependent);
					}
				}
			}

			if (ordered.Count != plans.Count)
			{
				ReportCycles(plans, ordered, planByServiceType);
			}

			return ordered;
		}

		private static void DropPlansWithMissingDependencies(List<ConstructionPlan> plans,
			Dictionary<Type, ConstructionPlan> planByServiceType)
		{
			bool droppedAny;
			do
			{
				droppedAny = false;

				for (var i = plans.Count - 1; i >= 0; i--)
				{
					var plan = plans[i];
					foreach (var dependencyType in plan.InContainerDependencies)
					{
						if (planByServiceType.ContainsKey(dependencyType))
						{
							continue;
						}

						Log.Error($"{plan.ConcreteType.FullName} cannot be constructed: {dependencyType.FullName} belongs to this container but was itself skipped. Skipping registration.");
						plans.RemoveAt(i);
						planByServiceType.Remove(plan.ServiceType);
						droppedAny = true;
						break;
					}
				}
			}
			while (droppedAny);
		}

		private static void ReportCycles(List<ConstructionPlan> plans, List<ConstructionPlan> ordered,
			Dictionary<Type, ConstructionPlan> planByServiceType)
		{
			var constructed = new HashSet<Type>();
			foreach (var plan in ordered)
			{
				constructed.Add(plan.ServiceType);
			}

			foreach (var plan in plans)
			{
				if (constructed.Contains(plan.ServiceType))
				{
					continue;
				}

				var cycle = FindCycle(plan, planByServiceType, constructed);
				Log.Error($"{plan.ConcreteType.FullName} is in a dependency cycle ({cycle}) and every service in it is skipped. A constructor parameter cannot express a mutual reference, because neither side can be built first. Drop the parameter on one side and fetch that service where it is used instead: registration happens before initialization, so the instance is already there.");
			}
		}

		/// <summary>
		///     Walks dependencies from <paramref name="start" /> until a type repeats, and names the loop in
		///     the order it was walked, so the error points at the edge to remove rather than a set of types.
		/// </summary>
		private static string FindCycle(ConstructionPlan start, Dictionary<Type, ConstructionPlan> planByServiceType,
			HashSet<Type> constructed)
		{
			var path = new List<Type>();
			var visited = new HashSet<Type>();
			var current = start;

			while (current != null && visited.Add(current.ServiceType))
			{
				path.Add(current.ServiceType);

				ConstructionPlan next = null;
				foreach (var dependencyType in current.InContainerDependencies)
				{
					if (constructed.Contains(dependencyType) || !planByServiceType.TryGetValue(dependencyType, out var candidate))
					{
						continue;
					}

					next = candidate;
					break;
				}

				current = next;
			}

			var names = new string[path.Count + 1];
			for (var i = 0; i < path.Count; i++)
			{
				names[i] = path[i].Name;
			}

			names[path.Count] = path.Count > 0 ? path[0].Name : "?";
			return string.Join(" -> ", names);
		}

		private void Construct(ConstructionPlan plan)
		{
			try
			{
				object serviceObject;

				if (plan.ParameterServiceTypes.Count == 0)
				{
					serviceObject = Activator.CreateInstance(plan.ConcreteType);
				}
				else
				{
					var arguments = new object[plan.ParameterServiceTypes.Count];
					for (var i = 0; i < arguments.Length; i++)
					{
						if (!TryResolveDependency(plan.ParameterServiceTypes[i], out var dependency))
						{
							Log.Error($"{plan.ConcreteType.FullName} cannot be constructed: {plan.ParameterServiceTypes[i].FullName} is registered but has no live instance. Skipping registration.");
							return;
						}

						arguments[i] = dependency;
					}

					serviceObject = plan.Constructor.Invoke(arguments);
				}

				if (serviceObject is not IService serviceInstance)
				{
					Log.Error($"Service {plan.ServiceType.FullName} does not implement IService");
					return;
				}

				ContainedServices.Add(plan.ServiceType, serviceInstance);
				_initializationOrder.Add(serviceInstance);
				_inContainerDependencies[serviceInstance] = CollectDependencyInstances(plan);
			}
			catch (Exception e)
			{
				//Skip the offending service rather than rethrow: this runs under
				//RuntimeInitializeOnLoadMethod, so propagating takes down the whole boot sequence
				//and leaves every remaining service in this container unregistered.
				Log.Error($"Exception constructing {plan.ConcreteType.FullName}, skipping registration: {e}");
			}
		}

		private bool TryResolveDependency(Type serviceType, out IService dependency)
		{
			if (ContainedServices.TryGetValue(serviceType, out dependency))
			{
				return true;
			}

			//Not in this container, so it is the Global one: TryPlanParameters already refused anything else.
			return ServiceLocator.TryGetGlobalServiceForInjection(serviceType, out dependency);
		}

		private List<IService> CollectDependencyInstances(ConstructionPlan plan)
		{
			var dependencies = new List<IService>(plan.InContainerDependencies.Count);
			foreach (var dependencyType in plan.InContainerDependencies)
			{
				if (ContainedServices.TryGetValue(dependencyType, out var dependency))
				{
					dependencies.Add(dependency);
				}
			}

			return dependencies;
		}

		/// <summary>
		///     Initializes services so that nothing runs before what it was handed: the synchronous ones
		///     inline, in construction order, then the asynchronous ones a level at a time, where a level is
		///     everything whose dependencies finished in an earlier one. Services in a level still run
		///     concurrently. Invokes ContainerServicesInitialized once they have all settled.
		/// </summary>
		private void InitializeServices()
		{
			InitializeSyncServices();

			var asyncLevels = BuildAsyncLevels();
			if (asyncLevels.Count == 0)
			{
				ContainerInitialized = true;
				ContainerServicesInitialized?.Invoke();
				return;
			}

			// Initialize services that require time to be setup
			// but do not block main thread.
			InitializeAsyncLevels(asyncLevels).Forget();
		}

		private void InitializeSyncServices()
		{
			foreach (var serviceInstance in _initializationOrder)
			{
				if (serviceInstance.IsAsyncInit)
				{
					continue;
				}

				var dependencies = _inContainerDependencies[serviceInstance];
				if (HasFailedDependency(serviceInstance, dependencies) || HasAsyncDependency(serviceInstance, dependencies))
				{
					continue;
				}

				serviceInstance.Initialize();
			}
		}

		/// <summary>
		///     Groups the async services by how deep their async dependencies run: level 0 waits for nothing
		///     in this container, level 1 for level 0, and so on. Construction order is topological, so a
		///     dependency's level is always known by the time its dependent is reached.
		/// </summary>
		private List<List<IService>> BuildAsyncLevels()
		{
			var levels = new List<List<IService>>();
			var levelByService = new Dictionary<IService, int>();

			foreach (var serviceInstance in _initializationOrder)
			{
				if (!serviceInstance.IsAsyncInit)
				{
					continue;
				}

				var level = 0;
				foreach (var dependency in _inContainerDependencies[serviceInstance])
				{
					//Sync dependencies are absent from the map: they are all initialized before any of this.
					if (levelByService.TryGetValue(dependency, out var dependencyLevel))
					{
						level = Math.Max(level, dependencyLevel + 1);
					}
				}

				levelByService[serviceInstance] = level;
				while (levels.Count <= level)
				{
					levels.Add(new List<IService>());
				}

				levels[level].Add(serviceInstance);
			}

			return levels;
		}

		/// <summary>
		///     Runs each level to completion before starting the next. Every task is awaited exactly once,
		///     by the WhenAll for its own level: a UniTask carries a single continuation, so having each
		///     dependent await its dependency's task as well would be a second registration on it.
		/// </summary>
		private async UniTaskVoid InitializeAsyncLevels(List<List<IService>> levels)
		{
			try
			{
				foreach (var level in levels)
				{
					var running = new List<UniTask>(level.Count);
					foreach (var serviceInstance in level)
					{
						if (HasFailedDependency(serviceInstance, _inContainerDependencies[serviceInstance]))
						{
							continue;
						}

						running.Add(serviceInstance.InitializeAsyncWrapper());
					}

					if (running.Count > 0)
					{
						await UniTask.WhenAll(running);
					}
				}

				ContainerInitialized = true;
				ContainerServicesInitialized?.Invoke();
			}
			catch (Exception e)
			{
				Log.Error(e.ToString());
			}
		}

		/// <summary>
		///     Whether something this service was handed has already failed, in which case it is Failed too
		///     and its own initialization never runs.
		/// </summary>
		private static bool HasFailedDependency(IService serviceInstance, List<IService> dependencies)
		{
			foreach (var dependency in dependencies)
			{
				if (dependency.GetConfigState() != ConfigurationState.Failed)
				{
					continue;
				}

				serviceInstance.MarkFailedByDependency(dependency);
				return true;
			}

			return false;
		}

		/// <summary>
		///     The one ordering the runtime cannot honour: a synchronous service cannot wait for an async
		///     dependency without blocking the main thread, and running it anyway would hand it something
		///     that is not ready. Failed, with an error saying to make it async.
		/// </summary>
		private static bool HasAsyncDependency(IService serviceInstance, List<IService> dependencies)
		{
			foreach (var dependency in dependencies)
			{
				if (!dependency.IsAsyncInit)
				{
					continue;
				}

				serviceInstance.MarkFailed($"{serviceInstance.GetType().FullName} is sync-init but depends on async-init {dependency.GetType().FullName}; make it async. It is not initialized.");
				return true;
			}

			return false;
		}

		public override void DisposeContainer()
		{
			base.DisposeContainer();
			_initializationOrder.Clear();
			_inContainerDependencies.Clear();
			ContainerServicesInitialized = null;
		}

		/// <summary>
		///     One service's route from its attribute to a live instance: the constructor to call and the
		///     services to hand it, split into the ones this container must construct first and the outer
		///     ones that already exist.
		/// </summary>
		private class ConstructionPlan
		{
			public readonly Type ConcreteType;
			public readonly Type ServiceType;
			public readonly ConstructorInfo Constructor;
			public readonly List<Type> ParameterServiceTypes = new();
			public readonly List<Type> InContainerDependencies = new();

			public ConstructionPlan(Type concreteType, Type serviceType, ConstructorInfo constructor)
			{
				ConcreteType = concreteType;
				ServiceType = serviceType;
				Constructor = constructor;
			}
		}

		private string DescribeThisContainer()
		{
			return ContainerLifetime == Lifetime.ScopedContext
				? $"{Lifetime.ScopedContext}:{ContainerContext.Value}"
				: ContainerLifetime.ToString();
		}

		private static string DescribeScope(ServiceAttribute attribute)
		{
			return attribute.Lifetime == Lifetime.ScopedContext
				? $"{Lifetime.ScopedContext}:{attribute.Context.Value}"
				: attribute.Lifetime.ToString();
		}
	}
}
