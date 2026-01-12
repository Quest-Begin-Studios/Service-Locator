namespace QBS.ServiceLocator
{
	public enum Lifetime
	{
		None,
		Scene,
		ScopedContext,
		Global,
	}

	// Populate as required.
	public enum Context
	{
		// Reserved, do not use.
		None,

		// ReSharper disable once InconsistentNaming
		_Count,
	}
}