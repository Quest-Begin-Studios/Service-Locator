using System;

namespace QBS.ServiceLocator
{
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