using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.Tests
{
    public class ServiceContainerTests
    {
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private static ServiceContainer BuildContainer(params Type[] concreteTypes)
        {
            var allServices = new Dictionary<Type, ServiceAttribute>();
            foreach (var type in concreteTypes)
            {
                allServices.Add(type, new ServiceAttribute(Lifetime.Global, type));
            }

            return new ServiceContainer(Lifetime.Global, allServices);
        }

        [Test]
        public void OnEnteredContainerLifetime_SyncOnly_InitializesImmediately()
        {
            var container = BuildContainer(typeof(SucceedingSyncService));

            container.OnEnteredContainerLifetime();

            Assert.IsTrue(container.ContainerInitialized);
            Assert.IsTrue(container.TryGetService<SucceedingSyncService>(out var service));
            Assert.AreEqual(ConfigurationState.Success, service.GetConfigState());
            Assert.AreEqual(1, service.InitializeCallCount);
        }

        [Test]
        public void OnEnteredContainerLifetime_InitializesSyncServicesBeforeStartingAsyncOnes()
        {
            var container = BuildContainer(typeof(SucceedingSyncService), typeof(SucceedingAsyncService));

            container.OnEnteredContainerLifetime();

            Assert.IsTrue(container.TryGetService<SucceedingSyncService>(out var sync));
            Assert.AreEqual(ConfigurationState.Success, sync.GetConfigState());

            Assert.IsTrue(container.TryGetService<SucceedingAsyncService>(out var async));
            Assert.AreEqual(1, async.InitializeCallCount);

            // The async service yields at least once before completing, so the container
            // cannot have finished on this same synchronous call.
            Assert.IsFalse(container.ContainerInitialized);
        }

        [Test]
        public async Task OnEnteredContainerLifetime_AsyncServices_FireContainerServicesInitializedOnceAllSettle()
        {
            var container = BuildContainer(typeof(SucceedingAsyncService));
            var eventFireCount = 0;
            container.ContainerServicesInitialized += () => eventFireCount++;

            container.OnEnteredContainerLifetime();
            await UniTask.WaitUntil(() => container.ContainerInitialized).Timeout(TimeSpan.FromSeconds(2));

            Assert.AreEqual(1, eventFireCount);
            Assert.IsTrue(container.TryGetService<SucceedingAsyncService>(out var service));
            Assert.AreEqual(ConfigurationState.Success, service.GetConfigState());
        }

        [Test]
        public void OnEnteredContainerLifetime_ThrowingSyncService_DoesNotBlockOtherServices()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = BuildContainer(typeof(SucceedingSyncService), typeof(ThrowingSyncService));

            Assert.DoesNotThrow(() => container.OnEnteredContainerLifetime());

            Assert.IsTrue(container.ContainerInitialized);
            Assert.IsTrue(container.TryGetService<SucceedingSyncService>(out var good));
            Assert.AreEqual(ConfigurationState.Success, good.GetConfigState());
            Assert.IsTrue(container.TryGetService<ThrowingSyncService>(out var bad));
            Assert.AreEqual(ConfigurationState.Failed, bad.GetConfigState());
        }

        [Test]
        public async Task OnEnteredContainerLifetime_ThrowingAsyncService_DoesNotBlockOtherServices()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = BuildContainer(typeof(SucceedingAsyncService), typeof(ThrowingAsyncService));

            container.OnEnteredContainerLifetime();
            await UniTask.WaitUntil(() => container.ContainerInitialized).Timeout(TimeSpan.FromSeconds(2));

            Assert.IsTrue(container.TryGetService<SucceedingAsyncService>(out var good));
            Assert.AreEqual(ConfigurationState.Success, good.GetConfigState());
            Assert.IsTrue(container.TryGetService<ThrowingAsyncService>(out var bad));
            Assert.AreEqual(ConfigurationState.Failed, bad.GetConfigState());
        }

        [Test]
        public void GetService_ServiceNotRegistered_ThrowsKeyNotFoundException()
        {
            // GetService is the intentionally-throwing counterpart to TryGetService (standard
            // .NET indexer-style contract) — not a gap, just documenting the design.
            var container = BuildContainer();
            container.OnEnteredContainerLifetime();

            Assert.Throws<KeyNotFoundException>(() => container.GetService<SucceedingSyncService>());
        }

        [Test]
        public void DisposeContainer_CallsDisposeServiceAndResetsState()
        {
            var container = BuildContainer(typeof(SucceedingSyncService));
            container.OnEnteredContainerLifetime();
            container.TryGetService<SucceedingSyncService>(out var service);

            container.DisposeContainer();

            Assert.AreEqual(1, service.DisposeCallCount);
            Assert.IsFalse(container.ContainerInitialized);
            Assert.AreEqual(ConfigurationState.Uninitialized, service.GetConfigState());
        }
    }
}
