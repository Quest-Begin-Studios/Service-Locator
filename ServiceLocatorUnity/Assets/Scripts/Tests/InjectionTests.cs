using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

//An [Inject] field is written by the container, never by the code that declares it, which is exactly
//what CS0649 warns about. The services below are the package's own fakes; consuming code is shown the
//`= default` idiom in the README instead.
#pragma warning disable 0649

namespace QBS.ServiceLocator.Tests
{
    /// <summary>
    ///     Records what the container did, in the order it did it, so ordering can be asserted from the
    ///     test rather than from inside a service: an assertion inside an async init outlives its own
    ///     container, and the next GameStart clears the state it would be reading.
    /// </summary>
    public static class InjectionLog
    {
        public static readonly List<string> Events = new();

        public static void Constructed(string serviceName)
        {
            Events.Add("ctor:" + serviceName);
        }

        public static void Initialized(string serviceName)
        {
            Events.Add("init:" + serviceName);
        }

        public static int IndexOfConstruction(string serviceName)
        {
            return Events.IndexOf("ctor:" + serviceName);
        }

        public static int IndexOfInitialization(string serviceName)
        {
            return Events.IndexOf("init:" + serviceName);
        }

        public static bool WasInitialized(string serviceName)
        {
            return Events.Contains("init:" + serviceName);
        }

        public static void Clear()
        {
            Events.Clear();
        }
    }

    /// <summary>
    ///     Contexts, not Global, for everything below. A service that discovery has to refuse logs an
    ///     error doing so, and a Global one would log it on every GameStart in the whole suite, failing
    ///     unrelated tests that assert on clean logs. A context is only built by the test that asks for it.
    /// </summary>
    public static class InjectionContexts
    {
        public const int Clean = 43001;
        public const int Rejected = 43002;
        public const int Neighbour = 43003;
    }

    public interface IInjectedLeaf : IService
    {
    }

    //The one Global service here: the others inject it, which is what proves a context service can
    //reach the outer container.
    [Service(Lifetime.Global, typeof(IInjectedLeaf))]
    public class InjectedLeaf : IInjectedLeaf
    {
        public bool IsAsyncInit => false;
    }

    public interface IInjectedMiddle : IService
    {
        IInjectedLeaf Leaf { get; }
    }

    public interface IInjectedRoot : IService
    {
        IInjectedLeaf Leaf { get; }
        IInjectedMiddle Middle { get; }
    }

    [Service(InjectionContexts.Clean, typeof(IInjectedMiddle))]
    public class InjectedMiddle : IInjectedMiddle
    {
        [Inject] private IInjectedLeaf _leaf;

        public InjectedMiddle()
        {
            InjectionLog.Constructed(nameof(InjectedMiddle));
        }

        public IInjectedLeaf Leaf => _leaf;
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized(nameof(InjectedMiddle));
            return true;
        }
    }

    [Service(InjectionContexts.Clean, typeof(IInjectedRoot))]
    public class InjectedRoot : IInjectedRoot
    {
        [Inject] private IInjectedLeaf _leaf;
        [Inject] private IInjectedMiddle _middle;

        public InjectedRoot()
        {
            InjectionLog.Constructed(nameof(InjectedRoot));
        }

        public IInjectedLeaf Leaf => _leaf;
        public IInjectedMiddle Middle => _middle;
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized(nameof(InjectedRoot));
            return true;
        }
    }

    public interface IAsyncDependency : IService
    {
    }

    public interface IAsyncDependent : IService
    {
    }

    public interface ISyncAfterAsync : IService
    {
    }

    [Service(InjectionContexts.Clean, typeof(IAsyncDependency))]
    public class AsyncDependency : IAsyncDependency
    {
        public AsyncDependency()
        {
            InjectionLog.Constructed(nameof(AsyncDependency));
        }

        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            InjectionLog.Initialized(nameof(AsyncDependency));
            return true;
        }
    }

    [Service(InjectionContexts.Clean, typeof(IAsyncDependent))]
    public class AsyncDependent : IAsyncDependent
    {
        [Inject] private IAsyncDependency _dependency;

        public AsyncDependent()
        {
            InjectionLog.Constructed(nameof(AsyncDependent));
        }

        public bool IsAsyncInit => true;

        public static ConfigurationState DependencyStateWhenInitialized { get; private set; }

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            //Recorded, not asserted: this runs inside the container's own initialization, where a throw
            //would be swallowed into a Failed state rather than surfacing as a test failure.
            DependencyStateWhenInitialized = _dependency.GetConfigState();
            await UniTask.Yield();
            InjectionLog.Initialized(nameof(AsyncDependent));
            return true;
        }
    }

    //Sync init behind an async dependency: the container withholds this one until the dependency has
    //settled, so it never has to wait for anything itself and never blocks the main thread doing it.
    [Service(InjectionContexts.Clean, typeof(ISyncAfterAsync))]
    public class SyncAfterAsyncService : ISyncAfterAsync
    {
        [Inject] private IAsyncDependency _dependency;

        public SyncAfterAsyncService()
        {
            InjectionLog.Constructed(nameof(SyncAfterAsyncService));
        }

        public bool IsAsyncInit => false;

        public static ConfigurationState DependencyStateWhenInitialized { get; private set; }

        bool IService.InitializeService()
        {
            DependencyStateWhenInitialized = _dependency.GetConfigState();
            InjectionLog.Initialized(nameof(SyncAfterAsyncService));
            return true;
        }
    }

    public interface IMutualFirst : IService
    {
        IMutualSecond Reach();
    }

    public interface IMutualSecond : IService
    {
        IMutualFirst Reach();
    }

    //The pair that [Inject] cannot express. Neither declares the other as a dependency; each fetches
    //at the point of use, which works because every service is registered before any is initialized.
    [Service(InjectionContexts.Clean, typeof(IMutualFirst))]
    public class MutualFirst : IMutualFirst
    {
        public bool IsAsyncInit => false;

        public IMutualSecond Reach()
        {
            ServiceLocator.TryGetContextService<IMutualSecond>(out var second);
            return second;
        }
    }

    [Service(InjectionContexts.Clean, typeof(IMutualSecond))]
    public class MutualSecond : IMutualSecond
    {
        public bool IsAsyncInit => false;

        public IMutualFirst Reach()
        {
            ServiceLocator.TryGetContextService<IMutualFirst>(out var first);
            return first;
        }
    }

    public interface ICycleA : IService
    {
    }

    public interface ICycleB : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(ICycleA))]
    public class CycleA : ICycleA
    {
        [Inject] private ICycleB _b;

        public bool IsAsyncInit => false;
    }

    [Service(InjectionContexts.Rejected, typeof(ICycleB))]
    public class CycleB : ICycleB
    {
        [Inject] private ICycleA _a;

        public bool IsAsyncInit => false;
    }

    public interface INonServiceField : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(INonServiceField))]
    public class NonServiceFieldService : INonServiceField
    {
        [Inject] private string _notAService;

        public bool IsAsyncInit => false;
    }

    public interface IReadonlyInject : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(IReadonlyInject))]
    public class ReadonlyInjectService : IReadonlyInject
    {
        [Inject] private readonly IInjectedLeaf _leaf;

        public bool IsAsyncInit => false;
    }

    public interface IFailingDependency : IService
    {
    }

    public interface IDependentOnFailure : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(IFailingDependency))]
    public class FailingDependency : IFailingDependency
    {
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            return false;
        }
    }

    [Service(InjectionContexts.Rejected, typeof(IDependentOnFailure))]
    public class DependentOnFailure : IDependentOnFailure
    {
        [Inject] private IFailingDependency _dependency;

        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized(nameof(DependentOnFailure));
            return true;
        }
    }

    public interface INeighbourContextService : IService
    {
    }

    public interface IAcrossContexts : IService
    {
    }

    [Service(InjectionContexts.Neighbour, typeof(INeighbourContextService))]
    public class NeighbourContextService : INeighbourContextService
    {
        public bool IsAsyncInit => false;
    }

    //A context may reach Global, never a sibling context: that container may not exist yet, and may be
    //purged while this service is still alive.
    [Service(InjectionContexts.Rejected, typeof(IAcrossContexts))]
    public class AcrossContextsService : IAcrossContexts
    {
        [Inject] private INeighbourContextService _neighbour;

        public bool IsAsyncInit => false;
    }

    public class InjectionTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            InjectionLog.Clear();
            ServiceLocator.GameStart();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        ///     LogAssert.ignoreFailingMessages from [SetUp] only covers SetUp itself, and discovering the
        ///     rejected context deliberately logs errors, so it has to be set again in the test body.
        /// </summary>
        private static void DiscoverRejectedContext()
        {
            LogAssert.ignoreFailingMessages = true;
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Rejected);
        }

        private static async Task DiscoverCleanContextAndWait()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);
            await UniTask.WaitUntil(() => ServiceLocator.IsContextContainerInitialized(InjectionContexts.Clean))
                .Timeout(TimeSpan.FromSeconds(5));
        }

        [Test]
        public void Injection_FillsTheFieldsWithTheContainersOwnInstances()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedRoot>(out var root));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedMiddle>(out var middle));
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IInjectedLeaf>(out var leaf));

            Assert.AreSame(leaf, middle.Leaf, "the middle service was given a different leaf instance");
            Assert.AreSame(middle, root.Middle);
            Assert.AreSame(leaf, root.Leaf, "a diamond has to resolve to one instance, not two");
        }

        [Test]
        public void Initialization_FollowsTheInjectedDependencies()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.Less(InjectionLog.IndexOfInitialization(nameof(InjectedMiddle)),
                InjectionLog.IndexOfInitialization(nameof(InjectedRoot)));
        }

        /// <summary>
        ///     The guarantee the fetch-at-use-time escape rests on: nothing in the container is
        ///     initializing while anything is still being constructed.
        /// </summary>
        [Test]
        public void EveryServiceIsConstructedBeforeAnyOfThemInitializes()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.Less(InjectionLog.IndexOfConstruction(nameof(InjectedRoot)),
                InjectionLog.IndexOfInitialization(nameof(InjectedMiddle)),
                "the last service constructed should still precede the first one initialized");
        }

        [Test]
        public void ContextService_IsGivenTheGlobalInstance()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IInjectedLeaf>(out var leaf));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedMiddle>(out var middle));
            Assert.AreSame(leaf, middle.Leaf, "the context service should share the Global instance, not a copy");
        }

        [Test]
        public async Task AsyncService_InitializesOnlyAfterItsAsyncDependencySucceeded()
        {
            await DiscoverCleanContextAndWait();

            Assert.AreEqual(ConfigurationState.Success, AsyncDependent.DependencyStateWhenInitialized,
                "the dependent started before its dependency had finished");

            Assert.Less(InjectionLog.IndexOfInitialization(nameof(AsyncDependency)),
                InjectionLog.IndexOfInitialization(nameof(AsyncDependent)));
        }

        [Test]
        public async Task SyncService_MayInjectAnAsyncOne_AndRunsAfterIt()
        {
            await DiscoverCleanContextAndWait();

            Assert.IsTrue(ServiceLocator.TryGetContextService<ISyncAfterAsync>(out var service));
            Assert.AreEqual(ConfigurationState.Success, service.GetConfigState());
            Assert.AreEqual(ConfigurationState.Success, SyncAfterAsyncService.DependencyStateWhenInitialized,
                "a sync service behind an async dependency should be held until that dependency settles");

            Assert.Less(InjectionLog.IndexOfInitialization(nameof(AsyncDependency)),
                InjectionLog.IndexOfInitialization(nameof(SyncAfterAsyncService)));
        }

        [Test]
        public void MutualReference_ThroughFetchRatherThanInject_Works()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IMutualFirst>(out var first));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IMutualSecond>(out var second));

            Assert.AreSame(second, first.Reach());
            Assert.AreSame(first, second.Reach());
        }

        [Test]
        public void InjectCycle_SkipsBothSidesAndLeavesTheRestOfTheContainerStanding()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<ICycleA>(out _));
            Assert.IsFalse(ServiceLocator.TryGetContextService<ICycleB>(out _));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IFailingDependency>(out _), "the cycle took unrelated services with it");
        }

        [Test]
        public void InjectFieldThatIsNotAService_SkipsOnlyThatService()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<INonServiceField>(out _));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IFailingDependency>(out _));
        }

        [Test]
        public void ReadonlyInjectField_IsSkipped()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<IReadonlyInject>(out _));
        }

        [Test]
        public void DependencyThatFailsInit_FailsItsDependentWithoutRunningIt()
        {
            DiscoverRejectedContext();

            Assert.IsTrue(ServiceLocator.TryGetContextService<IDependentOnFailure>(out var dependent));
            Assert.AreEqual(ConfigurationState.Failed, dependent.GetConfigState());
            Assert.IsFalse(InjectionLog.WasInitialized(nameof(DependentOnFailure)));
        }

        [Test]
        public void ContextService_CannotInjectAServiceFromAnotherContext()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<IAcrossContexts>(out _));
        }
    }
}
