using System;
using Cysharp.Threading.Tasks;

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
    ///     Doing so bypasses state table cleanup and causes a memory leak. Service discovery rejects
    ///     any type that does this.
    ///     Implement <see cref="DisposeService" /> for custom disposal logic instead.
    /// </remarks>
    public interface IService : IDisposable
    {
        /// <summary>
        ///     Whether this service requires asynchronous initialization.
        /// </summary>
        public bool IsAsyncInit { get; }

        /// <summary>
        ///     Implement to provide synchronous initialization logic. Return <c>false</c> to signal failure.
        /// </summary>
        protected internal bool InitializeService()
        {
            return true;
        }

        /// <summary>
        ///     Implement to provide asynchronous initialization logic. Return <c>false</c> to signal failure.
        /// </summary>
        protected internal UniTask<bool> InitializeServiceAsync()
        {
            return UniTask.FromResult(true);
        }

        /// <summary>
        ///     Implement to provide custom disposal logic. Called automatically by <see cref="IDisposable.Dispose" />.
        /// </summary>
        protected internal void DisposeService()
        {
            // no-op until required
        }

        void IDisposable.Dispose()
        {
            DisposeService();
            ServiceExtensions.ClearState(this);
        }
    }
}