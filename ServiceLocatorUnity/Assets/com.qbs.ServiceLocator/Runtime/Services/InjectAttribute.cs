using System;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Marks a field the container fills with another service, and declares that this service is not
	///     initialized until that one has finished initializing.
	/// </summary>
	/// <remarks>
	///     The field is written after construction and before any service in the container is initialized,
	///     so it is never null by the time <see cref="IService.InitializeService" /> runs, and it cannot be
	///     readonly.
	///
	///     Injecting is the stronger of the two ways to reach a service, and the one to reach for only when
	///     the other must be <em>ready</em> before this one starts. A service that merely needs the
	///     reference should fetch it from <see cref="ServiceLocator" /> where it uses it: every service is
	///     constructed and registered before any is initialized, so the instance is always there, and two
	///     services can hold each other that way. A pair that injects each other is a cycle — each would
	///     wait for the other forever — and both are skipped.
	///
	///     Derives from PreserveAttribute for the same reason <see cref="ServiceAttribute" /> does: the
	///     field is written by reflection, and Unity's linker matches Preserve by inheritance, so an
	///     annotated field survives managed stripping.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Field)]
	public class InjectAttribute : UnityEngine.Scripting.PreserveAttribute
	{
	}
}
