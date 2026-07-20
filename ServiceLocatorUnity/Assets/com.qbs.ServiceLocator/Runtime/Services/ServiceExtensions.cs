using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using QBS.Core;

namespace QBS.ServiceLocator
{
    /// <summary>
    ///     Orchestrates <see cref="IService" /> initialization and disposal state.
    ///     Kept outside the interface so implementing types cannot silently replace this logic —
    ///     extension methods are resolved statically and are never part of interface dispatch.
    /// </summary>
    public static class ServiceExtensions
    {
        private static readonly ConditionalWeakTable<IService, ServiceState> _stateTable = new();

        /// <summary>
        ///     Current initialization state of this service.
        /// </summary>
        public static ConfigurationState GetConfigState(this IService service)
        {
            return _stateTable.GetOrCreateValue(service).ConfigState;
        }

        /// <summary>
        ///     The running task for async initialization, or <c>null</c> if not started.
        /// </summary>
        public static UniTask? GetAsyncInitTask(this IService service)
        {
            return _stateTable.GetOrCreateValue(service).AsyncInitTask;
        }

        /// <summary>
        ///     Runs synchronous initialization and updates <see cref="GetConfigState" />.
        ///     Logs an error if called on an async-init service.
        /// </summary>
        internal static void Initialize(this IService service)
        {
            if (service.IsAsyncInit)
            {
                Log.Error($"Use Async Initialization for this {service.GetType().FullName} service");
                return;
            }

            var state = _stateTable.GetOrCreateValue(service);
            bool initServiceSuccess;
            try
            {
                initServiceSuccess = service.InitializeService();
            }
            catch (Exception e)
            {
                state.ConfigState = ConfigurationState.Failed;
                Log.Error($"Exception initializing {service.GetType().FullName} service: {e}");
                return;
            }

            if (initServiceSuccess)
            {
                state.ConfigState = ConfigurationState.Success;
            }
            else
            {
                state.ConfigState = ConfigurationState.Failed;
                Log.Error($"Failed to initialize {service.GetType().FullName} service");
            }
        }

        /// <summary>
        ///     Starts async initialization, stores the task, and returns it.
        ///     Logs an error if called on a sync-init service.
        /// </summary>
        internal static UniTask InitializeAsyncWrapper(this IService service)
        {
            if (!service.IsAsyncInit)
            {
                Log.Error($"Use synchronous initialization for this {service.GetType().FullName} service");
                return UniTask.CompletedTask;
            }

            var state = _stateTable.GetOrCreateValue(service);
            var task = InitializeAsync(service, state).Preserve();
            state.AsyncInitTask = task;
            return task;
        }

        private static async UniTask InitializeAsync(IService service, ServiceState state)
        {
            state.ConfigState = ConfigurationState.InProgress;
            bool initServiceSuccess;
            try
            {
                initServiceSuccess = await service.InitializeServiceAsync();
            }
            catch (Exception e)
            {
                state.ConfigState = ConfigurationState.Failed;
                Log.Error($"Exception initializing {service.GetType().FullName} service: {e}");
                return;
            }

            if (initServiceSuccess)
            {
                state.ConfigState = ConfigurationState.Success;
            }
            else
            {
                state.ConfigState = ConfigurationState.Failed;
                Log.Error($"Failed to initialize {service.GetType().FullName} service");
            }
        }

        /// <summary>
        ///     Waits for async initialization to complete, up to <paramref name="maxWait" /> seconds.
        ///     Returns <c>true</c> if initialization succeeded within the timeout.
        /// </summary>
        public static async UniTask<bool> AwaitInitialization(this IService service, float maxWait = 5f)
        {
            var state = _stateTable.GetOrCreateValue(service);
            if (state.AsyncInitTask == null)
            {
                return state.ConfigState == ConfigurationState.Success;
            }

            // Polls ConfigState instead of re-awaiting state.AsyncInitTask directly: that task is
            // already being awaited by the container's own HandleAsyncInitializations via UniTask.WhenAll
            var stopwatch = Stopwatch.StartNew();
            var maxWaitMilliseconds = (long) (maxWait * 1000);
            while (state.ConfigState == ConfigurationState.InProgress && stopwatch.ElapsedMilliseconds < maxWaitMilliseconds)
            {
                await UniTask.Yield();
            }

            if (state.ConfigState != ConfigurationState.Success)
            {
                if (state.ConfigState == ConfigurationState.Failed)
                {
                    Log.Error($"Initialization of {service.GetType().FullName} failed");
                }
                else
                {
                    Log.Error($"Initialization of {service.GetType().FullName} timed out after {maxWait}s");
                }
            }

            return state.ConfigState == ConfigurationState.Success;
        }

        /// <summary>
        ///     Clears the static configuration state table.
        /// </summary>
        internal static void CleanupConfigStateTable()
        {
            _stateTable.Clear();
        }

        /// <summary>
        ///     Removes the state entry for a disposed service. Called by <see cref="IService.Dispose" /> only.
        /// </summary>
        internal static void ClearState(IService service)
        {
            _stateTable.Remove(service);
        }

        private class ServiceState
        {
            public ConfigurationState ConfigState;
            public UniTask? AsyncInitTask;
        }
    }
}
