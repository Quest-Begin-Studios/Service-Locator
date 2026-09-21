using System;

namespace QBS.ServiceLocator
{
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

	/// <summary>
	///     Attribute used to mark service implementations for automatic discovery and registration.
	///     Specifies the service's lifetime scope (Global, Scene, PersistentScene, or ScopedContext), the interface
	///     type it implements, and optionally the context for scoped services. Used by the ServiceLocator for
	///     reflection-based service discovery.
	/// </summary>
	/// <remarks>
	///     When two concrete types claim one <see cref="ServiceType"/>, the higher <see cref="Priority"/>
	///     wins: a game's <see cref="ServicePriority.Override"/> beats a package's
	///     <see cref="ServicePriority.Default"/> whichever order discovery met them in.
	///
	///     The base class is load-bearing, not decoration. A service is reached by reflection and referenced
	///     statically by nothing, so managed stripping deletes it from a player; Unity's linker matches
	///     PreserveAttribute by inheritance, so deriving from it keeps every type marked with this attribute,
	///     along with the constructor discovery needs. Measured on 6000.5.1f1, Mono2x, stripping High: a
	///     service marked with an attribute deriving from Attribute is absent from the player assembly, and
	///     one marked with an attribute deriving from PreserveAttribute survives with its members. Changing
	///     the base class back breaks players only, with every behavioural test still green, so
	///     ServiceAttribute_DerivesFromPreserve_SoStrippedPlayersKeepServices asserts the base class itself.
	///
	///     AttributeUsage is spelled out because PreserveAttribute declares Inherited = false, which this
	///     attribute would otherwise adopt: discovery reads it with Attribute.GetCustomAttribute, which
	///     honours inheritance, and that should not change as a side effect of the base class.
	/// </remarks>
	[AttributeUsage(AttributeTargets.All, Inherited = true)]
	public class ServiceAttribute : UnityEngine.Scripting.PreserveAttribute
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