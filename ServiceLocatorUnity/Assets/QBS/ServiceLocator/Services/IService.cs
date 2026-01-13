using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace QBS.ServiceLocator
{
	/// <summary>
	///     Represents the initialization state of a service throughout its lifecycle.
	/// </summary>
	public enum ConfigurationState
	{
		Uninitialized,
		InProgress,
		Failed,
		Success,
	}

	/// <summary>
	///     Core interface for all services in the Service Locator pattern.
	///     Provides lifecycle management with support for both synchronous and asynchronous initialization,
	///     configuration state tracking, and initialization awaiting capabilities.
	/// </summary>
	public interface IService
	{
		private static readonly ConditionalWeakTable<IService, ServiceState> ConfigStateTable = new();
		public bool IsAsyncInit { get; }
		public ConfigurationState ConfigState
		{
			get => ConfigStateTable.GetOrCreateValue(this).ConfigState;
			private set => ConfigStateTable.GetOrCreateValue(this).ConfigState = value;
		}

		public void Initialize()
		{
			if (IsAsyncInit)
			{
				Debug.LogError($"Use Async Initialization for this {GetType().FullName} service");
				return;
			}

			var initServiceSuccess = InitializeService();
			if (initServiceSuccess)
			{
				ConfigState = ConfigurationState.Success;
			}
			else
			{
				ConfigState = ConfigurationState.Failed;
				Debug.LogError($"Failed to initialize {GetType().FullName} service");
			}
		}

		public async Task InitializeAsync()
		{
			if (!IsAsyncInit)
			{
				Debug.LogError($"Use synchronous initialization for this {GetType().FullName} service");
				return;
			}

			ConfigState = ConfigurationState.InProgress;
			var initServiceSuccess = await InitializeServiceAsync();
			if (initServiceSuccess)
			{
				ConfigState = ConfigurationState.Success;
			}
			else
			{
				ConfigState = ConfigurationState.Failed;
				Debug.LogError($"Failed to initialize {GetType().FullName} service");
			}
		}

		protected virtual bool InitializeService()
		{
			return true;
		}

		protected virtual Task<bool> InitializeServiceAsync()
		{
			return Task.FromResult(true);
		}

		public async Task<bool> AwaitInitialization(float maxWait = 5f)
		{
			var elapsedTime = 0f;
			while (ConfigState is ConfigurationState.InProgress or ConfigurationState.Uninitialized)
			{
				await Task.Yield();
				elapsedTime += Time.deltaTime;
				if (elapsedTime > maxWait)
				{
					Debug.LogError($"Exiting await as initialization of {this} took longer than {maxWait} seconds");
					return false;
				}
			}

			return ConfigState == ConfigurationState.Success;
		}

		/// <summary>
		///     Clears the static configuration state table.
		/// </summary>
		public static void CleanupConfigStateTable()
		{
			ConfigStateTable.Clear();
		}

		/// <summary>
		///     Data holder class for the static ConfigStateTable
		/// </summary>
		private class ServiceState
		{
			public ConfigurationState ConfigState;
		}
	}
}