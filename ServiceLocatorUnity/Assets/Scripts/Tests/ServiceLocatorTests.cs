using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.Tests
{
    public interface IGreeterTestService : IService
    {
        string Greet();
    }

    [Service(Lifetime.Global, typeof(IGreeterTestService))]
    public class GreeterTestService : IGreeterTestService
    {
        public bool IsAsyncInit => false;

        public string Greet()
        {
            return "hello";
        }

        bool IService.InitializeService()
        {
            return true;
        }
    }

    public interface IBadDisposeTestService : IService
    {
    }

    [Service(Lifetime.Global, typeof(IBadDisposeTestService))]
    public class BadDisposeTestService : IBadDisposeTestService
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            return true;
        }

        // Re-implements Dispose directly instead of DisposeService — discovery must reject this
        // registration rather than silently letting it bypass state-table cleanup.
        public void Dispose()
        {
        }
    }

    public interface IGlobalAsyncTestService : IService
    {
    }

    [Service(Lifetime.Global, typeof(IGlobalAsyncTestService))]
    public class GlobalAsyncTestService : IGlobalAsyncTestService
    {
        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            return true;
        }
    }

    public interface IDuplicateTestService : IService
    {
    }

    // Two concrete types claiming one ServiceType — the real mock/editor-implementation shape.
    // Discovery must log and skip the loser; a bare dictionary Add would throw out of
    // [RuntimeInitializeOnLoadMethod] and abandon every other service in the scan.
    // These deliberately stay registered, so the collision is exercised on every GameStart.
    [Service(Lifetime.Global, typeof(IDuplicateTestService))]
    public class DuplicateTestServiceA : IDuplicateTestService
    {
        public bool IsAsyncInit => false;
    }

    [Service(Lifetime.Global, typeof(IDuplicateTestService))]
    public class DuplicateTestServiceB : IDuplicateTestService
    {
        public bool IsAsyncInit => false;
    }

    public interface IScopedSyncTestService : IService
    {
    }

    [Service(42001, typeof(IScopedSyncTestService))]
    public class ScopedSyncTestService : IScopedSyncTestService
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            return true;
        }
    }

    public interface IScopedAsyncTestService : IService
    {
    }

    [Service(42002, typeof(IScopedAsyncTestService))]
    public class ScopedAsyncTestService : IScopedAsyncTestService
    {
        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            return true;
        }
    }

    public interface IScopedPurgeTestService : IService
    {
    }

    [Service(42003, typeof(IScopedPurgeTestService))]
    public class ScopedPurgeTestService : IScopedPurgeTestService
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            return true;
        }
    }

    public interface ISceneLocatorTestService : IService
    {
    }

    [Service(Lifetime.Scene, typeof(ISceneLocatorTestService))]
    public class SceneLocatorTestService : ISceneLocatorTestService
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

    public class ServiceLocatorTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ServiceLocator.GameStart();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void GameStart_DiscoversAndInitializesGlobalService()
        {
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var service));
            Assert.AreEqual("hello", service.Greet());
        }

        [Test]
        public void GameStart_RejectsServiceThatReimplementsDisposeDirectly()
        {
            Assert.IsFalse(ServiceLocator.TryGetGlobalService<IBadDisposeTestService>(out _));
        }

        [Test]
        public void GameStart_TwoTypesClaimingOneServiceType_KeepsOneWithoutAbortingDiscovery()
        {
            // [SetUp]'s GameStart() already scanned both DuplicateTestService types. The assertion that
            // matters is that discovery survived it: an unguarded Add would have thrown mid-scan and left
            // every service after the collision — including IGreeterTestService — unregistered.
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IDuplicateTestService>(out var duplicate));
            Assert.IsNotNull(duplicate);
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out _));
        }

        [Test]
        public void TryGetContextService_ForANonScopedService_ReturnsFalseInsteadOfThrowing()
        {
            // IGreeterTestService is Global, so it has no context at all. While every lifetime was written
            // into the context map it landed on Context 0 and sent this lookup into a container that is
            // never created, throwing out of a method whose whole contract is not to.
            Assert.IsFalse(ServiceLocator.TryGetContextService<IGreeterTestService>(out var service));
            Assert.IsNull(service);
            Assert.IsNull(ServiceLocator.FetchContextService<IGreeterTestService>());
        }

        [Test]
        public void FetchGlobalService_ReturnsSameInstanceAsTryGet()
        {
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var tryGetResult));
            var fetchResult = ServiceLocator.FetchGlobalService<IGreeterTestService>();
            Assert.AreSame(tryGetResult, fetchResult);
        }

        [Test]
        public async Task GlobalServicesInitialized_FiresAfterAsyncGlobalServiceCompletes()
        {
            // [SetUp]'s GameStart() already kicked off the async global service, and there's no
            // reliable window to subscribe before it settles. Purge and re-discover here so the
            // subscription is guaranteed to be in place before the (re)started async init fires.
            //
            // Discovery must come before subscribing, not after: GlobalServicesInitialized's
            // add accessor forwards straight to _globalServiceContainer.ContainerServicesInitialized
            // with no null guard, so subscribing while the container is still null (i.e. right
            // after purge, before rediscovery) throws NullReferenceException. Rediscovery
            // recreates the container synchronously — before any async work has had a chance to
            // yield — so subscribing immediately afterward is still ahead of completion.
            ServiceLocator.PurgeContainer(Lifetime.Global);
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.Global);

            var fired = false;
            ServiceLocator.GlobalServicesInitialized += () => fired = true;

            await UniTask.WaitUntil(() => ServiceLocator.IsGlobalContainerInitialized).Timeout(TimeSpan.FromSeconds(2));

            Assert.IsTrue(fired);
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGlobalAsyncTestService>(out _));
        }

        [Test]
        public void DiscoverServicesOfLifetime_Global_CalledAgain_KeepsExistingContainer()
        {
            // LogAssert.ignoreFailingMessages set in [SetUp] only covers logs raised during
            // SetUp itself, not the test body — it must be set again here for the "already
            // discovered" error this test deliberately provokes.
            LogAssert.ignoreFailingMessages = true;
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var before));

            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.Global);

            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var after));
            Assert.AreSame(before, after);
        }

        [Test]
        public void PurgeContainer_Global_ThenRediscover_CreatesFreshServiceInstance()
        {
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var before));

            ServiceLocator.PurgeContainer(Lifetime.Global);
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.Global);

            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IGreeterTestService>(out var after));
            Assert.AreNotSame(before, after);
        }

        [Test]
        public void FetchGlobalService_AfterPurgeWithoutRediscovery_ThrowsNullReferenceException()
        {
            // KNOWN GAP: PurgeContainer(Global) nulls out the global container, but
            // FetchGlobalService/TryGetGlobalService/IsGlobalContainerInitialized have no null
            // guard, unlike their Scene-lifetime counterparts (which use `?.` throughout). This
            // documents current behavior, not desired behavior — if this stops throwing, a null
            // guard was added and this test should be replaced with a positive assertion.
            ServiceLocator.PurgeContainer(Lifetime.Global);

            Assert.Throws<NullReferenceException>(() => ServiceLocator.FetchGlobalService<IGreeterTestService>());
        }

        [Test]
        public void DiscoverServicesOfLifetime_ScopedContext_DiscoversAndInitializesService()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42001);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IScopedSyncTestService>(out _));
            Assert.IsTrue(ServiceLocator.IsContextContainerInitialized(42001));
        }

        [Test]
        public void DiscoverServicesOfLifetime_ScopedContext_CalledTwice_KeepsOriginalContainer()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42001);
            Assert.IsTrue(ServiceLocator.TryGetContextService<IScopedSyncTestService>(out var before));

            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42001);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IScopedSyncTestService>(out var after));
            Assert.AreSame(before, after);
        }

        [Test]
        public void DiscoverServicesOfLifetime_ScopedContext_DefaultContext_DoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext));
        }

        [Test]
        public void IsContextContainerInitialized_UnknownContext_ReturnsFalse()
        {
            Assert.IsFalse(ServiceLocator.IsContextContainerInitialized(999999));
        }

        [Test]
        public async Task SubscribeToContextServiceSetup_FiresWhenContextServicesFinishInitializing()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42002);
            var fired = false;
            ServiceLocator.SubscribeToContextServiceSetup(42002, () => fired = true);

            await UniTask.WaitUntil(() => ServiceLocator.IsContextContainerInitialized(42002))
                .Timeout(TimeSpan.FromSeconds(2));

            Assert.IsTrue(fired);
        }

        [Test]
        public async Task UnsubscribeToContextServiceSetup_StopsReceivingNotifications()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42002);
            var callCount = 0;
            void Handler() => callCount++;

            ServiceLocator.SubscribeToContextServiceSetup(42002, Handler);
            ServiceLocator.UnsubscribeToContextServiceSetup(42002, Handler);

            await UniTask.WaitUntil(() => ServiceLocator.IsContextContainerInitialized(42002))
                .Timeout(TimeSpan.FromSeconds(2));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void FetchContextService_AfterPurgingItsContext_FailsSafeInsteadOfThrowing()
        {
            // _serviceContextMap is built once at discovery, so it still points this service type at 42003
            // after the purge took that container away. Both accessors have to fail safe on the dangling
            // reference rather than index straight into a map that no longer holds the key.
            LogAssert.ignoreFailingMessages = true;
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, 42003);
            Assert.IsTrue(ServiceLocator.TryGetContextService<IScopedPurgeTestService>(out _));

            ServiceLocator.PurgeContainer(Lifetime.ScopedContext, 42003);

            Assert.IsNull(ServiceLocator.FetchContextService<IScopedPurgeTestService>());
            Assert.IsFalse(ServiceLocator.TryGetContextService<IScopedPurgeTestService>(out _));
        }

        [Test]
        public void RegisterSceneService_WithExplicitScene_CreatesThatScenesContainerLazily()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));

            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(new SceneLocatorTestService(), scene);

            Assert.IsTrue(ServiceLocator.IsSceneContainerInitialized(scene));
            Assert.IsTrue(ServiceLocator.TryGetSceneService<ISceneLocatorTestService>(scene, out var registered));
            Assert.AreSame(registered, ServiceLocator.FetchSceneService<ISceneLocatorTestService>(scene));
        }

        [Test]
        public void RegisterSceneService_NullService_IsRejectedRatherThanStored()
        {
            // A stored null goes unnoticed until DisposeContainer walks the map and dereferences it,
            // by which point nothing points back at the registration that put it there.
            LogAssert.ignoreFailingMessages = true;
            var scene = SceneManager.GetActiveScene();

            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(null, scene);

            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<ISceneLocatorTestService>(scene, out _));
            Assert.DoesNotThrow(() => ServiceLocator.PurgeContainer(Lifetime.Scene));
        }

        [Test]
        public void RegisterSceneService_NonComponentWithoutExplicitScene_DoesNotRegister()
        {
            // A non-Component service has no gameObject.scene to infer from, so the single-argument
            // overload must reject it rather than guess a scene.
            LogAssert.ignoreFailingMessages = true;
            var scene = SceneManager.GetActiveScene();

            Assert.DoesNotThrow(() =>
                ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(new SceneLocatorTestService()));

            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));
        }

        [Test]
        public void FetchSceneService_SceneWithoutContainer_ReturnsNull()
        {
            Assert.IsNull(ServiceLocator.FetchSceneService<ISceneLocatorTestService>(SceneManager.GetActiveScene()));
        }

        [Test]
        public void DisposeSceneContainer_DisposesServicesAndDropsTheContainer()
        {
            var scene = SceneManager.GetActiveScene();
            var service = new SceneLocatorTestService();
            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(service, scene);

            ServiceLocator.DisposeSceneContainer(scene);

            Assert.AreEqual(1, service.DisposeCallCount);
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<ISceneLocatorTestService>(scene, out _));
        }

        [Test]
        public void DisposeSceneContainer_SceneWithoutContainer_IsANoOp()
        {
            Assert.DoesNotThrow(() => ServiceLocator.DisposeSceneContainer(SceneManager.GetActiveScene()));
        }

        [Test]
        public void PurgeContainer_Scene_IsSafeAndClearsState()
        {
            var scene = SceneManager.GetActiveScene();
            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(new SceneLocatorTestService(), scene);
            Assert.IsTrue(ServiceLocator.TryGetSceneService<ISceneLocatorTestService>(scene, out _));

            ServiceLocator.PurgeContainer(Lifetime.Scene);

            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<ISceneLocatorTestService>(scene, out _));
        }

        [Test]
        public void SceneContainerEvents_FireWithTheOwningSceneAndService()
        {
            var scene = SceneManager.GetActiveScene();
            var created = new List<Scene>();
            var disposed = new List<Scene>();
            (Scene Scene, Type Type, IService Service) registered = default;

            ServiceLocator.SceneContainerCreated += s => created.Add(s);
            ServiceLocator.SceneContainerDisposed += s => disposed.Add(s);
            ServiceLocator.SceneServiceRegistered += (s, t, i) => registered = (s, t, i);

            var service = new SceneLocatorTestService();
            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(service, scene);
            ServiceLocator.DisposeSceneContainer(scene);

            CollectionAssert.AreEqual(new[] { scene }, created);
            CollectionAssert.AreEqual(new[] { scene }, disposed);
            Assert.AreEqual(scene, registered.Scene);
            Assert.AreEqual(typeof(ISceneLocatorTestService), registered.Type);
            Assert.AreSame(service, registered.Service);
        }

        [Test]
        public void SceneContainerCreated_FiresOncePerScene_NotPerRegistration()
        {
            LogAssert.ignoreFailingMessages = true;
            var scene = SceneManager.GetActiveScene();
            var createdCount = 0;
            ServiceLocator.SceneContainerCreated += _ => createdCount++;

            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(new SceneLocatorTestService(), scene);
            ServiceLocator.RegisterSceneService<ISceneLocatorTestService>(new SceneLocatorTestService(), scene);

            Assert.AreEqual(1, createdCount);
        }

    }
}
