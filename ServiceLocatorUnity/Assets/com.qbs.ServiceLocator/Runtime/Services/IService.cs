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
    ///     Do not hide or re-implement <see cref="IDisposable.Dispose" /> in implementing classes.
    ///     Doing so bypasses state table cleanup and causes a memory leak.
    ///     Override <see cref="DisposeService" /> for custom disposal logic instead.
    /// </remarks>
    public interface IService : IDisposable
    {
        private static readonly ConditionalWeakTable<IService, ServiceState> _configStateTable = new();

        /// <summary>
        ///     Whether this service requires asynchronous initialization.
        /// </summary>
        public bool IsAsyncInit { get; }

        /// <summary>
        ///     Current initialization state of this service.
        /// </summary>
        public ConfigurationState ConfigState
        {
            get => _configStateTable.GetOrCreateValue(this).ConfigState;
            private set => _configStateTable.GetOrCreateValue(this).ConfigState = value;
        }

        /// <summary>
        ///     The running task for async initialization, or <c>null</c> if not started.
        /// </summary>
        public Task AsyncInitTask
        {
            get => _configStateTable.GetOrCreateValue(this).AsyncInitTask;
            private set => _configStateTable.GetOrCreateValue(this).AsyncInitTask = value;
        }

        /// <summary>
        ///     Runs synchronous initialization and updates <see cref="ConfigState" />.
        ///     Logs an error if called on an async-init service.
        /// </summary>
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

        /// <summary>
        ///     Starts async initialization, stores the task in <see cref="AsyncInitTask" />, and returns it.
        ///     Logs an error if called on a sync-init service.
        /// </summary>
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

        /// <summary>
        ///     Override to provide synchronous initialization logic. Return <c>false</c> to signal failure.
        /// </summary>
        protected virtual bool InitializeService()
        {
            return true;
        }

        /// <summary>
        ///     Override to provide asynchronous initialization logic. Return <c>false</c> to signal failure.
        /// </summary>
        protected virtual Task<bool> InitializeServiceAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Waits for async initialization to complete, up to <paramref name="maxWait" /> seconds.
        ///     Returns <c>true</c> if initialization succeeded within the timeout.
        /// </summary>
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

        /// <summary>
        ///     Override to provide custom disposal logic. Called automatically by <see cref="IDisposable.Dispose" />.
        /// </summary>
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