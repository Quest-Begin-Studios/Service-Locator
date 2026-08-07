using System;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Attribute used to mark service implementations for automatic discovery and registration.
	///     Specifies the service's lifetime scope (Global, Scene, PersistentScene, or ScopedContext), the interface
	///     type it implements, and optionally the context for scoped services. Used by the ServiceLocator for
	///     reflection-based service discovery.
	/// </summary>
	public class ServiceAttribute : Attribute
	{
		public Lifetime Lifetime { get; }
		public Type ServiceType { get; }
		public Context Context { get; }

		//For Global, Scene and PersistentScene Services
		public ServiceAttribute(Lifetime lifetime, Type serviceType)
		{
			Lifetime = lifetime;
			Context = default;
			ServiceType = serviceType;
		}

		//For Scoped Context Services — contextValue must be a const int defined in consumer assembly
		public ServiceAttribute(int contextValue, Type serviceType)
		{
			// implicit conversion to `Context`
			Context = contextValue;
			ServiceType = serviceType;
			Lifetime = Lifetime.ScopedContext;
		}
	}
}