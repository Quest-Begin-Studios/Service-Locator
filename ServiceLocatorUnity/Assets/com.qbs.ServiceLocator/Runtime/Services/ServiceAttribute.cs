using System;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Attribute used to mark service implementations for automatic discovery and registration.
	///     Specifies the service's lifetime scope (Global, Scene, PersistentScene, or ScopedContext), the interface
	///     type it implements, and optionally the context for scoped services. Used by the ServiceLocator for
	///     reflection-based service discovery.
	/// </summary>
	/// <summary>
	///     Who a service implementation belongs to, and so which one wins when two claim the same interface.
	///     Ordered by declaration: a later member outranks an earlier one, and assembly load order decides
	///     nothing. Two implementations at the same priority stay an error, so neither a pair of packages
	///     nor a pair of game services can silently fight over an interface.
	/// </summary>
	public enum ServicePriority
	{
		/// <summary>A package's own implementation: the one a consumer is free to replace.</summary>
		Default,

		/// <summary>A game's replacement for a package default. Wins over <see cref="Default"/>.</summary>
		Override,

		/// <summary>A test fake. Wins over everything, and ships in no player.</summary>
		Tests,
	}

	/// <remarks>
	///     When two concrete types claim one <see cref="ServiceType"/>, the higher <see cref="Priority"/>
	///     wins: a game's <see cref="ServicePriority.Override"/> beats a package's
	///     <see cref="ServicePriority.Default"/> whichever order discovery met them in.
	/// </remarks>
	public class ServiceAttribute : Attribute
	{
		public Lifetime Lifetime { get; }
		public Type ServiceType { get; }
		public Context Context { get; }
		public ServicePriority Priority { get; }

		//For Global, Scene and PersistentScene Services
		public ServiceAttribute(Lifetime lifetime, Type serviceType, ServicePriority priority = ServicePriority.Default)
		{
			Lifetime = lifetime;
			Context = default;
			ServiceType = serviceType;
			Priority = priority;
		}

		//For Scoped Context Services — contextValue must be a const int defined in consumer assembly
		public ServiceAttribute(int contextValue, Type serviceType, ServicePriority priority = ServicePriority.Default)
		{
			// implicit conversion to `Context`
			Context = contextValue;
			ServiceType = serviceType;
			Lifetime = Lifetime.ScopedContext;
			Priority = priority;
		}
	}
}