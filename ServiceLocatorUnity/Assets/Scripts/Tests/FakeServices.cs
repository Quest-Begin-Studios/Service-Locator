using System;
using Cysharp.Threading.Tasks;

namespace QBS.ServiceLocator.Tests
{
    public class SucceedingSyncService : IService
    {
        public bool IsAsyncInit => false;
        public int InitializeCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        bool IService.InitializeService()
        {
            InitializeCallCount++;
            return true;
        }

        void IService.DisposeService()
        {
            DisposeCallCount++;
        }
    }

    public class FailingSyncService : IService
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            return false;
        }
    }

    public class ThrowingSyncService : IService
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            throw new InvalidOperationException("sync init failure");
        }
    }

    public class SucceedingAsyncService : IService
    {
        public bool IsAsyncInit => true;
        public int InitializeCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            InitializeCallCount++;
            await UniTask.Yield();
            return true;
        }

        void IService.DisposeService()
        {
            DisposeCallCount++;
        }
    }

    public class ThrowingAsyncService : IService
    {
        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            throw new InvalidOperationException("async init failure");
        }
    }

    public class NeverCompletingAsyncService : IService
    {
        private readonly UniTaskCompletionSource<bool> _completionSource = new();

        public bool IsAsyncInit => true;

        UniTask<bool> IService.InitializeServiceAsync()
        {
            return _completionSource.Task;
        }
    }
}
