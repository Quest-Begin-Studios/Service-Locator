using System;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Attribute used to mark service implementations for automatic discovery and registration.
	///     Specifies the service's lifetime scope (Global, Scene, PersistentScene, or ScopedContext), the interface
	///     type it implements, and optionally the context for scoped services. Used by the ServiceLocator for
	///     reflection-based service discovery.
	/// </summary>
	/// <remarks>
	///     When two concrete types claim one <see cref="ServiceType"/>, the higher <see cref="Priority"/> wins
	///     and assembly load order decides nothing. Equal priorities stay an error, so two packages cannot
	///     silently fight over an interface. Studio convention: 0 for a package default, 100 for a game's
	///     override, 1000 for a test fake.
	/// </remarks>
	public class ServiceAttribute : Attribute
	{
		public Lifetime Lifetime { get; }
		public Type ServiceType { get; }
		public Context Context { get; }
		public int Priority { get; }

		//For Global, Scene and PersistentScene Services
		public ServiceAttribute(Lifetime lifetime, Type serviceType, int priority = 0)
		{
			Lifetime = lifetime;
			Context = default;
			ServiceType = serviceType;
			Priority = priority;
		}

		//For Scoped Context Services — contextValue must be a const int defined in consumer assembly
		public ServiceAttribute(int contextValue, Type serviceType, int priority = 0)
		{
			// implicit conversion to `Context`
			Context = contextValue;
			ServiceType = serviceType;
			Lifetime = Lifetime.ScopedContext;
			Priority = priority;
		}
	}

	/// <summary>
	///     Marks which constructor the locator calls on a service that has more than one public
	///     constructor. A service with a single public constructor needs no attribute; one with several
	///     and no attribute is skipped, because guessing which to call is how a service ends up
	///     half-built.
	/// </summary>
	[AttributeUsage(AttributeTargets.Constructor)]
	public class ServiceConstructorAttribute : Attribute
	{
	}
}