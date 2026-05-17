namespace QBS.ServiceLocator
{
	/// <summary>
	/// Defines context identifiers for <see cref="Context"/> scoped services.
	/// <para>
	/// Extend this class in your own assembly using a <c>partial</c> declaration to add
	/// application-specific contexts. Each value must be a unique <c>const int</c> greater than 0.
	/// </para>
	/// <example>
	/// <code>
	/// // In your project (any assembly that references this package):
	/// namespace QBS.ServiceLocator
	/// {
	///     public static partial class GameContexts
	///     {
	///         public const int MainMenu = 1;
	///         public const int Gameplay = 2;
	///         public const int Settings  = 3;
	///     }
	/// }
	/// </code>
	/// Use the constants in your service attribute and at runtime:
	/// <code>
	/// [ServiceAttribute(GameContexts.Gameplay, typeof(IMyService))]
	/// public class MyService : IMyService { ... }
	///
	/// ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, GameContexts.Gameplay);
	/// </code>
	/// </example>
	/// </summary>
	public static partial class GameContexts
	{
		public static readonly Context None = default;
		public static readonly Context MainMenu = new Context(1);
	}
}