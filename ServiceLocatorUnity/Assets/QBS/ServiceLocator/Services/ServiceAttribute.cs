using System;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Attribute used to mark service implementations for automatic discovery and registration.
	///     Specifies the service's lifetime scope (Global, Scene, or ScopedContext), the interface type it implements,
	///     and optionally the context for scoped services. Used by the ServiceLocator for reflection-based service discovery.
	/// </summary>
	public class ServiceAttribute : Attribute
	{
		public Lifetime Lifetime { get; }
		public Type ServiceType { get; }
		public Context Context { get; }

		//For Global and Scene Services
		public ServiceAttribute(Lifetime lifetime, Type serviceType)
		{
			Lifetime = lifetime;
			Context = Context.None;
			ServiceType = serviceType;
		}

		//For Scoped Context Services
		public ServiceAttribute(Context context, Type serviceType)
		{
			Context = context;
			ServiceType = serviceType;
			Lifetime = Lifetime.ScopedContext;
		}
	}
}