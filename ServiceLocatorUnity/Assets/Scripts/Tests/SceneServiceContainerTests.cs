using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.Tests
{
    public interface ISceneTestService : IService
    {
    }

    public class SceneTestService : ISceneTestService
    {
        public bool IsAsyncInit => false;
        public int DisposeCallCount { get; private set; }

        bool IService.InitializeService()
        {
            return true;
        }

        void IService.DisposeService()
        {
            DisposeCallCount++;
        }
    }

    // Deliberately does not implement ISceneTestService, so it can never be legitimately
    // registered under that key — used only to prove the RegisterService<T> gap below.
    public interface IOtherSceneTestService : IService
    {
    }

    public class SceneServiceContainerTests
    {
        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private static SceneServiceContainer BuildContainer()
        {
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(ISceneTestService), new ServiceAttribute(Lifetime.Scene, typeof(ISceneTestService)) },
            };
            return new SceneServiceContainer(allServices, SceneManager.GetActiveScene());
        }

        [Test]
        public void ContainerInitialized_IsAlwaysTrue()
        {
            var container = BuildContainer();
            Assert.IsTrue(container.ContainerInitialized);
        }

        [Test]
        public void Scene_ReportsTheSceneItWasConstructedWith()
        {
            var scene = SceneManager.GetActiveScene();
            var container = BuildContainer();

            Assert.AreEqual(scene, container.Scene);
        }

        [Test]
        public void RegisterService_ValidType_SucceedsAndFiresEvent()
        {
            var container = BuildContainer();
            Type raisedType = null;
            IService raisedInstance = null;
            container.SceneServiceRegistered += (type, instance) =>
            {
                raisedType = type;
                raisedInstance = instance;
            };

            var service = new SceneTestService();
            container.RegisterService<ISceneTestService>(service);

            Assert.IsTrue(container.TryGetService<ISceneTestService>(out var retrieved));
            Assert.AreSame(service, retrieved);
            Assert.AreEqual(typeof(ISceneTestService), raisedType);
            Assert.AreSame(service, raisedInstance);
        }

        [Test]
        public void RegisterService_DuplicateType_KeepsOriginalRegistration()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = BuildContainer();
            var first = new SceneTestService();
            var second = new SceneTestService();

            container.RegisterService<ISceneTestService>(first);
            container.RegisterService<ISceneTestService>(second);

            container.TryGetService<ISceneTestService>(out var retrieved);
            Assert.AreSame(first, retrieved);
        }

        [Test]
        public void RegisterService_TypeWithoutServiceAttribute_DoesNotRegister()
        {
            LogAssert.ignoreFailingMessages = true;
            var container = new SceneServiceContainer(new Dictionary<Type, ServiceAttribute>(), SceneManager.GetActiveScene());

            container.RegisterService<ISceneTestService>(new SceneTestService());

            Assert.IsFalse(container.TryGetService<ISceneTestService>(out _));
        }

        [Test]
        public void RegisterService_WrongLifetimeAttribute_DoesNotRegister()
        {
            LogAssert.ignoreFailingMessages = true;
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(ISceneTestService), new ServiceAttribute(Lifetime.Global, typeof(ISceneTestService)) },
            };
            var container = new SceneServiceContainer(allServices, SceneManager.GetActiveScene());

            container.RegisterService<ISceneTestService>(new SceneTestService());

            Assert.IsFalse(container.TryGetService<ISceneTestService>(out _));
        }

        [Test]
        public void RegisterService_PersistentContainer_RejectsAnOrdinarySceneService()
        {
            // The two scene lifetimes are not interchangeable in either direction — that is what stops one
            // interface from ending up live in both a scene container and the persistent one at once.
            LogAssert.ignoreFailingMessages = true;
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(ISceneTestService), new ServiceAttribute(Lifetime.Scene, typeof(ISceneTestService)) },
            };
            var container = new SceneServiceContainer(allServices, SceneManager.GetActiveScene(), Lifetime.PersistentScene);

            container.RegisterService<ISceneTestService>(new SceneTestService());

            Assert.IsFalse(container.TryGetService<ISceneTestService>(out _));
        }

        [Test]
        public void RegisterService_SceneContainer_RejectsAPersistentSceneService()
        {
            LogAssert.ignoreFailingMessages = true;
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(ISceneTestService), new ServiceAttribute(Lifetime.PersistentScene, typeof(ISceneTestService)) },
            };
            var container = new SceneServiceContainer(allServices, SceneManager.GetActiveScene());

            container.RegisterService<ISceneTestService>(new SceneTestService());

            Assert.IsFalse(container.TryGetService<ISceneTestService>(out _));
        }

        [Test]
        public void RegisterService_PersistentContainer_AcceptsAPersistentSceneService()
        {
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(ISceneTestService), new ServiceAttribute(Lifetime.PersistentScene, typeof(ISceneTestService)) },
            };
            var container = new SceneServiceContainer(allServices, SceneManager.GetActiveScene(), Lifetime.PersistentScene);

            var service = new SceneTestService();
            container.RegisterService<ISceneTestService>(service);

            Assert.IsTrue(container.TryGetService<ISceneTestService>(out var retrieved));
            Assert.AreSame(service, retrieved);
        }

        [Test]
        public void RegisterService_InstanceNotAssignableToDeclaredType_ThrowsInvalidCastExceptionOnRetrieval()
        {
            // KNOWN GAP: RegisterService<T> never checks that `service` actually implements T,
            // so a mismatched registration only surfaces later — as an InvalidCastException —
            // when something calls GetService<T>() (TryGetService<T>() shares the same cast
            // and is equally at risk). Documents current behavior, not desired behavior.
            var allServices = new Dictionary<Type, ServiceAttribute>
            {
                { typeof(IOtherSceneTestService), new ServiceAttribute(Lifetime.Scene, typeof(IOtherSceneTestService)) },
            };
            var container = new SceneServiceContainer(allServices, SceneManager.GetActiveScene());

            container.RegisterService<IOtherSceneTestService>(new SceneTestService());

            Assert.Throws<InvalidCastException>(() => container.GetService<IOtherSceneTestService>());
        }

        [Test]
        public void DisposeContainer_DisposesRegisteredServices()
        {
            var container = BuildContainer();
            var service = new SceneTestService();
            container.RegisterService<ISceneTestService>(service);

            container.DisposeContainer();

            Assert.AreEqual(1, service.DisposeCallCount);
            Assert.IsFalse(container.TryGetService<ISceneTestService>(out _));
        }

        [Test]
        public void DisposeContainer_ClearsEventSubscribers()
        {
            var container = BuildContainer();
            var eventFireCount = 0;
            container.SceneServiceRegistered += (_, _) => eventFireCount++;

            container.DisposeContainer();
            container.RegisterService<ISceneTestService>(new SceneTestService());

            Assert.AreEqual(0, eventFireCount);
        }
    }
}
