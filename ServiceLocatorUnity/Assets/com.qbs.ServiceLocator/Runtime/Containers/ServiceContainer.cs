using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using QBS.Core;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Standard implementation of IServiceContainer for managing Global and ScopedContext services.
	///     Discovers the services of its own lifetime, constructs each one, writes the services its
	///     <see cref="InjectAttribute" /> fields ask for, and then initializes each one as soon as
	///     everything it injected has finished. A service with no injected field waits for nothing and may
	///     keep fetching what it needs.
	/// </summary>
	public class ServiceContainer : BaseServiceContainer
	{
		public event Action ContainerServicesInitialized;

		private const BindingFlags InjectableFields = BindingFlags.Instance | BindingFlags.Public
			| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		//Only dependencies inside this container gate anything: an outer Global service finished
		//initializing before this container existed.
		private readonly Dictionary<IService, List<IService>> _inContainerDependencies = new();
		private readonly Dictionary<IService, List<IService>> _dependents = new();
		private readonly Dictionary<IService, int> _pendingDependencies = new();
		private readonly List<IService> _initializationCandidates = new();
		private readonly Queue<IService> _readyToSyncInitialize = new();
		private readonly Queue<IService> _readyToAsyncInitialize = new();

		private Dictionary<Type, ServiceAttribute> _attributeByServiceType;
		private int _settledCount;
		private bool _disposed;

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
		///     AllServicesMap is keyed by concrete type; injected fields name interfaces. This is the other
		///     direction, over services of every lifetime, so a field can be validated against what the
		///     whole application registers rather than only what this container holds.
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
		///     Constructs every service of this lifetime and fills its injected fields.
		/// </summary>
		/// <remarks>
		///     Injection happens as each service is built rather than in a pass of its own, which lets it
		///     run in dependency order and makes a dependency that was skipped skip its dependents too. An
		///     order always exists because a cycle among injected fields is refused before this runs.
		/// </remarks>
		private void PopulateMapWithServicesOfLifetime()
		{
			var plans = CollectServicePlans();
			var built = new List<BuiltService>(plans.Count);
			var skipped = new HashSet<Type>();

			foreach (var plan in SortByDependencyOrder(plans))
			{
				if (TryBuild(plan, skipped, out var builtService))
				{
					built.Add(builtService);
				}
			}

			RecordDependencyGraph(built);
		}

		private List<ServicePlan> CollectServicePlans()
		{
			var plans = new List<ServicePlan>();
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

				var plan = new ServicePlan(concreteType, serviceAttribute.ServiceType);
				if (!TryPlanInjections(plan))
				{
					continue;
				}

				plans.Add(plan);
			}

			return plans;
		}

		/// <summary>
		///     Collects the injected fields of a service and of everything it inherits, since a base class
		///     may declare its own and a private field is not visible through a derived type.
		/// </summary>
		private bool TryPlanInjections(ServicePlan plan)
		{
			for (var type = plan.ConcreteType; type != null && type != typeof(object); type = type.BaseType)
			{
				foreach (var field in type.GetFields(InjectableFields))
				{
					if (!field.IsDefined(typeof(InjectAttribute), false))
					{
						continue;
					}

					if (!TryPlanInjection(plan, field))
					{
						return false;
					}
				}
			}

			return true;
		}

		private bool TryPlanInjection(ServicePlan plan, FieldInfo field)
		{
			if (field.IsInitOnly)
			{
				Log.Error($"{plan.ConcreteType.FullName}.{field.Name} is [Inject] and readonly. An injected field is written after the constructor has run, so it cannot be readonly. Skipping registration.");
				return false;
			}

			var fieldType = field.FieldType;

			if (!_attributeByServiceType.TryGetValue(fieldType, out var dependencyAttribute))
			{
				Log.Error($"{plan.ConcreteType.FullName}.{field.Name} is [Inject] but {fieldType.FullName} is not a service interface that any service registers. Skipping registration.");
				return false;
			}

			if (!IsResolvableFromHere(dependencyAttribute, out var isInThisContainer))
			{
				Log.Error($"{plan.ConcreteType.FullName} ({DescribeThisContainer()}) injects {fieldType.FullName} ({DescribeScope(dependencyAttribute)}), which it cannot reach: a service may only inject its own container or Global. Skipping registration.");
				return false;
			}

			plan.Injections.Add(new InjectionSite(field, fieldType));
			if (isInThisContainer)
			{
				plan.InContainerDependencies.Add(fieldType);
			}

			return true;
		}

		/// <summary>
		///     Whether this container can hand a service of <paramref name="dependency" />'s lifetime to one
		///     of its own. Global is the outer scope everything may reach; a scoped service may reach its own
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
		///     Kahn's algorithm over the injected dependencies inside this container. A plan whose
		///     dependency has no plan of its own is dropped first, repeatedly, so a service never waits on
		///     something that was itself skipped; whatever a cycle leaves behind is reported and dropped.
		/// </summary>
		private List<ServicePlan> SortByDependencyOrder(List<ServicePlan> plans)
		{
			var planByServiceType = new Dictionary<Type, ServicePlan>(plans.Count);
			foreach (var plan in plans)
			{
				planByServiceType[plan.ServiceType] = plan;
			}

			DropPlansWithMissingDependencies(plans, planByServiceType);

			var pendingDependencyCount = new Dictionary<Type, int>(plans.Count);
			var dependents = new Dictionary<Type, List<ServicePlan>>(plans.Count);
			foreach (var plan in plans)
			{
				pendingDependencyCount[plan.ServiceType] = plan.InContainerDependencies.Count;
				dependents[plan.ServiceType] = new List<ServicePlan>();
			}

			foreach (var plan in plans)
			{
				foreach (var dependencyType in plan.InContainerDependencies)
				{
					dependents[dependencyType].Add(plan);
				}
			}

			var ready = new Queue<ServicePlan>();
			foreach (var plan in plans)
			{
				if (pendingDependencyCount[plan.ServiceType] == 0)
				{
					ready.Enqueue(plan);
				}
			}

			var ordered = new List<ServicePlan>(plans.Count);
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

		private static void DropPlansWithMissingDependencies(List<ServicePlan> plans,
			Dictionary<Type, ServicePlan> planByServiceType)
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

						Log.Error($"{plan.ConcreteType.FullName} cannot be initialized: {dependencyType.FullName}, which it injects, belongs to this container but was itself skipped. Skipping registration.");
						plans.RemoveAt(i);
						planByServiceType.Remove(plan.ServiceType);
						droppedAny = true;
						break;
					}
				}
			}
			while (droppedAny);
		}

		private static void ReportCycles(List<ServicePlan> plans, List<ServicePlan> ordered,
			Dictionary<Type, ServicePlan> planByServiceType)
		{
			var sorted = new HashSet<Type>();
			foreach (var plan in ordered)
			{
				sorted.Add(plan.ServiceType);
			}

			foreach (var plan in plans)
			{
				if (sorted.Contains(plan.ServiceType))
				{
					continue;
				}

				var cycle = FindCycle(plan, planByServiceType, sorted);
				Log.Error($"{plan.ConcreteType.FullName} is in an [Inject] cycle ({cycle}) and every service in it is skipped. An injected field decides initialization order, so a mutual pair would each wait for the other forever. Drop [Inject] on one side and fetch that service where it is used instead: every service is constructed and registered before any is initialized, so the instance is already there.");
			}
		}

		/// <summary>
		///     Walks dependencies from <paramref name="start" /> until a type repeats, and names the loop in
		///     the order it was walked, so the error points at the edge to remove rather than a set of types.
		/// </summary>
		private static string FindCycle(ServicePlan start, Dictionary<Type, ServicePlan> planByServiceType,
			HashSet<Type> sorted)
		{
			var path = new List<Type>();
			var visited = new HashSet<Type>();
			var current = start;

			while (current != null && visited.Add(current.ServiceType))
			{
				path.Add(current.ServiceType);

				ServicePlan next = null;
				foreach (var dependencyType in current.InContainerDependencies)
				{
					if (sorted.Contains(dependencyType) || !planByServiceType.TryGetValue(dependencyType, out var candidate))
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

		/// <summary>
		///     Resolves what a service injects, constructs it, writes the fields and registers it. The
		///     dependencies are resolved before the instance exists so that one that cannot be reached skips
		///     the service rather than registering it with a null field.
		/// </summary>
		private bool TryBuild(ServicePlan plan, HashSet<Type> skipped, out BuiltService builtService)
		{
			builtService = default;

			foreach (var dependencyType in plan.InContainerDependencies)
			{
				if (!skipped.Contains(dependencyType))
				{
					continue;
				}

				Log.Error($"{plan.ConcreteType.FullName} is skipped: {dependencyType.FullName}, which it injects, was itself skipped.");
				skipped.Add(plan.ServiceType);
				return false;
			}

			try
			{
				var values = new object[plan.Injections.Count];
				for (var i = 0; i < values.Length; i++)
				{
					if (!TryResolveDependency(plan.Injections[i].ServiceType, out var dependency))
					{
						Log.Error($"{plan.ConcreteType.FullName} cannot be built: {plan.Injections[i].ServiceType.FullName} is registered but has no live instance. Skipping registration.");
						skipped.Add(plan.ServiceType);
						return false;
					}

					values[i] = dependency;
				}

				if (Activator.CreateInstance(plan.ConcreteType) is not IService serviceInstance)
				{
					Log.Error($"Service {plan.ServiceType.FullName} does not implement IService");
					skipped.Add(plan.ServiceType);
					return false;
				}

				for (var i = 0; i < values.Length; i++)
				{
					plan.Injections[i].Field.SetValue(serviceInstance, values[i]);
				}

				ContainedServices.Add(plan.ServiceType, serviceInstance);
				builtService = new BuiltService(plan, serviceInstance);
				return true;
			}
			catch (Exception e)
			{
				//Skip the offending service rather than rethrow: this runs under
				//RuntimeInitializeOnLoadMethod, so propagating takes down the whole boot sequence
				//and leaves every remaining service in this container unregistered.
				Log.Error($"Exception building {plan.ConcreteType.FullName}, skipping registration: {e}");
				skipped.Add(plan.ServiceType);
				return false;
			}
		}

		private bool TryResolveDependency(Type serviceType, out IService dependency)
		{
			if (ContainedServices.TryGetValue(serviceType, out dependency))
			{
				return true;
			}

			//Not in this container, so it is the Global one: TryPlanInjection already refused anything else.
			return ServiceLocator.TryGetGlobalServiceForInjection(serviceType, out dependency);
		}

		/// <summary>
		///     Records who waits for whom, so a service that settles can release exactly the services that
		///     were waiting on it.
		/// </summary>
		private void RecordDependencyGraph(List<BuiltService> built)
		{
			foreach (var entry in built)
			{
				_initializationCandidates.Add(entry.Instance);
				_dependents[entry.Instance] = new List<IService>();
			}

			foreach (var entry in built)
			{
				var dependencies = new List<IService>(entry.Plan.InContainerDependencies.Count);
				foreach (var dependencyType in entry.Plan.InContainerDependencies)
				{
					if (ContainedServices.TryGetValue(dependencyType, out var dependency))
					{
						dependencies.Add(dependency);
					}
				}

				_inContainerDependencies[entry.Instance] = dependencies;
				_pendingDependencies[entry.Instance] = dependencies.Count;
			}

			foreach (var entry in built)
			{
				foreach (var dependency in _inContainerDependencies[entry.Instance])
				{
					_dependents[dependency].Add(entry.Instance);
				}
			}
		}

		/// <summary>
		///     Starts every service that waits for nothing. Each one that settles releases the services
		///     waiting on it, so a service begins the moment its own dependencies are done rather than when
		///     some batch it happens to share a depth with is: an unrelated slow service never holds it up.
		///     Invokes ContainerServicesInitialized once every service has settled.
		/// </summary>
		private void InitializeServices()
		{
			foreach (var serviceInstance in _initializationCandidates)
			{
				if (_pendingDependencies[serviceInstance] == 0)
				{
					EnqueueReady(serviceInstance);
				}
			}

			DrainReadyQueue();
		}

		/// <summary>
		///     Sorts an unblocked service into the queue matching how it initializes.
		/// </summary>
		private void EnqueueReady(IService serviceInstance)
		{
			if (serviceInstance.IsAsyncInit)
			{
				_readyToAsyncInitialize.Enqueue(serviceInstance);
				return;
			}

			_readyToSyncInitialize.Enqueue(serviceInstance);
		}

		/// <summary>
		///     Runs everything currently unblocked, draining the synchronous queue completely before starting
		///     any asynchronous service. A synchronous service settles inside the loop and can release more
		///     work, so this drains rather than iterating a snapshot; after each asynchronous start the
		///     synchronous queue is drained again in case that start settled and released anything. An
		///     asynchronous service that settles later drains again from its own continuation.
		/// </summary>
		private void DrainReadyQueue()
		{
			while (_readyToSyncInitialize.Count > 0 || _readyToAsyncInitialize.Count > 0)
			{
				while (_readyToSyncInitialize.Count > 0)
				{
					var serviceInstance = _readyToSyncInitialize.Dequeue();

					if (HasFailedDependency(serviceInstance, _inContainerDependencies[serviceInstance]))
					{
						OnServiceSettled(serviceInstance);
						continue;
					}

					serviceInstance.Initialize();
					OnServiceSettled(serviceInstance);
				}

				if (_readyToAsyncInitialize.Count > 0)
				{
					var serviceInstance = _readyToAsyncInitialize.Dequeue();

					if (HasFailedDependency(serviceInstance, _inContainerDependencies[serviceInstance]))
					{
						OnServiceSettled(serviceInstance);
						continue;
					}

					InitializeAsync(serviceInstance).Forget();
				}
			}

			TryCompleteContainer();
		}

		/// <summary>
		///     Awaits one service's initialization and nothing else. Every task is awaited exactly once,
		///     here: a UniTask carries a single continuation, so a dependent waiting on its dependency's
		///     task would be a second registration on it. Dependents are released by the counter instead.
		/// </summary>
		private async UniTaskVoid InitializeAsync(IService serviceInstance)
		{
			try
			{
				await serviceInstance.InitializeAsyncWrapper();
			}
			catch (Exception e)
			{
				Log.Error(e.ToString());
			}

			//The container can be disposed while this was running: a context purged, or a scene torn
			//down, or a test starting over. There is no graph left to release anything into, and the
			//service this finished initializing is no longer registered anywhere.
			if (_disposed)
			{
				return;
			}

			OnServiceSettled(serviceInstance);
			DrainReadyQueue();
		}

		/// <summary>
		///     One service has finished, succeeded or failed. Anything left waiting only on it is now ready;
		///     a service released after a failure is marked Failed by <see cref="HasFailedDependency" />
		///     rather than run, and settles in turn so the failure reaches its own dependents.
		/// </summary>
		private void OnServiceSettled(IService serviceInstance)
		{
			_settledCount++;

			foreach (var dependent in _dependents[serviceInstance])
			{
				if (--_pendingDependencies[dependent] == 0)
				{
					EnqueueReady(dependent);
				}
			}
		}

		private void TryCompleteContainer()
		{
			if (_disposed || ContainerInitialized || _settledCount < _initializationCandidates.Count)
			{
				return;
			}

			ContainerInitialized = true;
			ContainerServicesInitialized?.Invoke();
		}

		/// <summary>
		///     Whether something this service injected has already failed, in which case it is Failed too
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

		public override void DisposeContainer()
		{
			_disposed = true;
			base.DisposeContainer();
			_inContainerDependencies.Clear();
			_dependents.Clear();
			_pendingDependencies.Clear();
			_initializationCandidates.Clear();
			_readyToSyncInitialize.Clear();
			_readyToAsyncInitialize.Clear();
			_settledCount = 0;
			ContainerServicesInitialized = null;
		}

		/// <summary>
		///     One service's route from its attribute to a live instance: the fields to fill, and which of
		///     them name services this container must build first rather than outer ones that already exist.
		/// </summary>
		private class ServicePlan
		{
			public readonly Type ConcreteType;
			public readonly Type ServiceType;
			public readonly List<InjectionSite> Injections = new();
			public readonly List<Type> InContainerDependencies = new();

			public ServicePlan(Type concreteType, Type serviceType)
			{
				ConcreteType = concreteType;
				ServiceType = serviceType;
			}
		}

		private readonly struct InjectionSite
		{
			public readonly FieldInfo Field;
			public readonly Type ServiceType;

			public InjectionSite(FieldInfo field, Type serviceType)
			{
				Field = field;
				ServiceType = serviceType;
			}
		}

		private readonly struct BuiltService
		{
			public readonly ServicePlan Plan;
			public readonly IService Instance;

			public BuiltService(ServicePlan plan, IService instance)
			{
				Plan = plan;
				Instance = instance;
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
