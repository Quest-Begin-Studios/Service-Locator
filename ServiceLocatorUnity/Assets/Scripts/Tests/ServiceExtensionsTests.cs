using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.Tests
{
    // These exercise ServiceExtensions' public state-tracking surface (GetConfigState,
    // GetAsyncInitTask, AwaitInitialization). Initialize/InitializeAsyncWrapper themselves are
    // internal to the QBS.ServiceLocator assembly, so a ServiceContainer is used here purely as
    // the mechanism to drive a service through real initialization.
    public class ServiceExtensionsTests
    {
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private static ServiceContainer BuildAndInitialize<TConcrete>() where TConcrete : class, IService, new()
        {
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(TConcrete), new ServiceAttribute(Lifetime.Global, typeof(TConcrete)) },
            };

            var container = new ServiceContainer(Lifetime.Global, allServices);
            container.OnEnteredContainerLifetime();
            return container;
        }

        [Test]
        public void GetConfigState_FreshService_IsUninitialized()
        {
            var service = new SucceedingSyncService();
            Assert.AreEqual(ConfigurationState.Uninitialized, service.GetConfigState());
        }

        [Test]
        public void GetAsyncInitTask_FreshService_IsNull()
        {
            var service = new SucceedingAsyncService();
            Assert.IsNull(service.GetAsyncInitTask());
        }

        [Test]
        public async Task AwaitInitialization_NeverInitialized_ReturnsFalseImmediately()
        {
            var service = new SucceedingAsyncService();
            Assert.IsFalse(await service.AwaitInitialization(maxWait: 0.1f));
        }

        [Test]
        public async Task AwaitInitialization_AfterSuccessfulAsyncInit_ReturnsTrue()
        {
            var container = BuildAndInitialize<SucceedingAsyncService>();
            container.TryGetService<SucceedingAsyncService>(out var service);

            var result = await service.AwaitInitialization(maxWait: 2f);

            Assert.IsTrue(result);
            Assert.AreEqual(ConfigurationState.Success, service.GetConfigState());
        }

        [Test]
        public async Task AwaitInitialization_AfterThrowingAsyncInit_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = BuildAndInitialize<ThrowingAsyncService>();
            container.TryGetService<ThrowingAsyncService>(out var service);

            var result = await service.AwaitInitialization(maxWait: 2f);

            Assert.IsFalse(result);
            Assert.AreEqual(ConfigurationState.Failed, service.GetConfigState());
        }

        [Test]
        public async Task AwaitInitialization_TimesOutWhenInitNeverCompletes()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = BuildAndInitialize<NeverCompletingAsyncService>();
            container.TryGetService<NeverCompletingAsyncService>(out var service);

            var result = await service.AwaitInitialization(maxWait: 0.1f);

            Assert.IsFalse(result);
        }
    }
}
