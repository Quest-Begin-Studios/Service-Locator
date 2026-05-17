using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using QBS.Core;

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
	/// <remarks>
	///     Do not hide or re-implement <see cref="IDisposable.Dispose"/> in implementing classes.
	///     Doing so bypasses state table cleanup and causes a memory leak.
	///     Override <see cref="DisposeService"/> for custom disposal logic instead.
	/// </remarks>
	public interface IService : IDisposable
	{
		private static readonly ConditionalWeakTable<IService, ServiceState> _configStateTable = new();
		
		public bool IsAsyncInit { get; }
		public ConfigurationState ConfigState
		{
			get => _configStateTable.GetOrCreateValue(this).ConfigState;
			private set => _configStateTable.GetOrCreateValue(this).ConfigState = value;
		}

		public Task AsyncInitTask
		{
			get => _configStateTable.GetOrCreateValue(this).AsyncInitTask;
			private set => _configStateTable.GetOrCreateValue(this).AsyncInitTask = value;
		}

		public void Initialize()
		{
			if (IsAsyncInit)
			{
				Log.Error($"Use Async Initialization for this {GetType().FullName} service");
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
				Log.Error($"Failed to initialize {GetType().FullName} service");
			}
		}

		public Task InitializeAsyncWrapper()
		{
			if (!IsAsyncInit)
			{
				Log.Error($"Use synchronous initialization for this {GetType().FullName} service");
				return Task.CompletedTask;
			}

			AsyncInitTask = InitializeAsync();
			return AsyncInitTask;
		}

		private async Task InitializeAsync()
		{
			ConfigState = ConfigurationState.InProgress;
			var initServiceSuccess = await InitializeServiceAsync();
			if (initServiceSuccess)
			{
				ConfigState = ConfigurationState.Success;
			}
			else
			{
				ConfigState = ConfigurationState.Failed;
				Log.Error($"Failed to initialize {GetType().FullName} service");
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
			if (AsyncInitTask == null)
			{
				return ConfigState == ConfigurationState.Success;
			}

			using var cts = new CancellationTokenSource();
			await Task.WhenAny(AsyncInitTask, Task.Delay((int) (maxWait * 1000), cts.Token));
			
			if (ConfigState != ConfigurationState.Success)
			{
				if (AsyncInitTask.IsCompleted)
				{
					Log.Error($"Initialization of {GetType().FullName} failed");
				}
				else
				{
					Log.Error($"Initialization of {GetType().FullName} timed out after {maxWait}s");
				}
			}
			cts.Cancel();
			return ConfigState == ConfigurationState.Success;
		}

		/// <summary>
		///     Clears the static configuration state table.
		/// </summary>
		public static void CleanupConfigStateTable()
		{
			_configStateTable.Clear();
		}
		
		void IDisposable.Dispose()
		{
			DisposeService();
			_configStateTable.Remove(this);
		}

		protected virtual void DisposeService()
		{
			// no-op until required
		}

		/// <summary>
		///     Data holder class for the static ConfigStateTable
		/// </summary>
		private class ServiceState
		{
			public ConfigurationState ConfigState;
			public Task AsyncInitTask;
		}
	}
}