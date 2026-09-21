using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.Tests
{
    /// <summary>
    ///     Records what the container did, in the order it did it, so ordering can be asserted from the
    ///     test rather than from inside a service: an assertion inside an async init outlives its own
    ///     container, and the next GameStart clears the state it would be reading.
    /// </summary>
    public static class InjectionLog
    {
        public static readonly List<string> Constructed = new();
        public static readonly List<string> Initialized = new();

        public static void Clear()
        {
            Constructed.Clear();
            Initialized.Clear();
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

    //The one Global service here: the others take it, which is what proves a context service can reach
    //the outer container.
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
        public InjectedMiddle(IInjectedLeaf leaf)
        {
            Leaf = leaf;
            InjectionLog.Constructed.Add(nameof(InjectedMiddle));
        }

        public IInjectedLeaf Leaf { get; }
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized.Add(nameof(InjectedMiddle));
            return true;
        }
    }

    [Service(InjectionContexts.Clean, typeof(IInjectedRoot))]
    public class InjectedRoot : IInjectedRoot
    {
        public InjectedRoot(IInjectedMiddle middle, IInjectedLeaf leaf)
        {
            Middle = middle;
            Leaf = leaf;
            InjectionLog.Constructed.Add(nameof(InjectedRoot));
        }

        public IInjectedLeaf Leaf { get; }
        public IInjectedMiddle Middle { get; }
        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized.Add(nameof(InjectedRoot));
            return true;
        }
    }

    public interface IMarkedConstructor : IService
    {
        bool TookTheMarkedOne { get; }
    }

    [Service(InjectionContexts.Clean, typeof(IMarkedConstructor))]
    public class MarkedConstructorService : IMarkedConstructor
    {
        public MarkedConstructorService()
        {
        }

        [ServiceConstructor]
        public MarkedConstructorService(IInjectedLeaf leaf)
        {
            TookTheMarkedOne = true;
        }

        public bool TookTheMarkedOne { get; }
        public bool IsAsyncInit => false;
    }

    public interface IAsyncDependency : IService
    {
    }

    public interface IAsyncDependent : IService
    {
    }

    [Service(InjectionContexts.Clean, typeof(IAsyncDependency))]
    public class AsyncDependency : IAsyncDependency
    {
        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            InjectionLog.Initialized.Add(nameof(AsyncDependency));
            return true;
        }
    }

    [Service(InjectionContexts.Clean, typeof(IAsyncDependent))]
    public class AsyncDependent : IAsyncDependent
    {
        private readonly IAsyncDependency _dependency;

        public AsyncDependent(IAsyncDependency dependency)
        {
            _dependency = dependency;
        }

        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            //Recorded, not asserted: this runs inside the container's own initialization, where a throw
            //would be swallowed into a Failed state rather than surfacing as a test failure.
            DependencyStateWhenInitialized = _dependency.GetConfigState();
            await UniTask.Yield();
            InjectionLog.Initialized.Add(nameof(AsyncDependent));
            return true;
        }

        public static ConfigurationState DependencyStateWhenInitialized { get; private set; }
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
        public CycleA(ICycleB b)
        {
        }

        public bool IsAsyncInit => false;
    }

    [Service(InjectionContexts.Rejected, typeof(ICycleB))]
    public class CycleB : ICycleB
    {
        public CycleB(ICycleA a)
        {
        }

        public bool IsAsyncInit => false;
    }

    public interface INonServiceParameter : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(INonServiceParameter))]
    public class NonServiceParameterService : INonServiceParameter
    {
        public NonServiceParameterService(string notAService)
        {
        }

        public bool IsAsyncInit => false;
    }

    public interface ITwoConstructors : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(ITwoConstructors))]
    public class TwoConstructorsService : ITwoConstructors
    {
        public TwoConstructorsService()
        {
        }

        public TwoConstructorsService(IInjectedLeaf leaf)
        {
        }

        public bool IsAsyncInit => false;
    }

    public interface IRejectedAsyncDependency : IService
    {
    }

    public interface ISyncOnAsync : IService
    {
    }

    [Service(InjectionContexts.Rejected, typeof(IRejectedAsyncDependency))]
    public class RejectedAsyncDependency : IRejectedAsyncDependency
    {
        public bool IsAsyncInit => true;

        async UniTask<bool> IService.InitializeServiceAsync()
        {
            await UniTask.Yield();
            return true;
        }
    }

    [Service(InjectionContexts.Rejected, typeof(ISyncOnAsync))]
    public class SyncOnAsyncService : ISyncOnAsync
    {
        public SyncOnAsyncService(IRejectedAsyncDependency dependency)
        {
        }

        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized.Add(nameof(SyncOnAsyncService));
            return true;
        }
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
        public DependentOnFailure(IFailingDependency dependency)
        {
        }

        public bool IsAsyncInit => false;

        bool IService.InitializeService()
        {
            InjectionLog.Initialized.Add(nameof(DependentOnFailure));
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
        public AcrossContextsService(INeighbourContextService neighbour)
        {
        }

        public bool IsAsyncInit => false;
    }

    public class ConstructorInjectionTests
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

        [Test]
        public void Construction_PassesTheDependenciesAndOrdersLeafFirst()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedRoot>(out var root));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedMiddle>(out var middle));
            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IInjectedLeaf>(out var leaf));

            Assert.AreSame(leaf, middle.Leaf, "the middle service was handed a different leaf instance");
            Assert.AreSame(middle, root.Middle);
            Assert.AreSame(leaf, root.Leaf, "a diamond has to resolve to one instance, not two");

            var constructed = InjectionLog.Constructed;
            Assert.Less(constructed.IndexOf(nameof(InjectedMiddle)), constructed.IndexOf(nameof(InjectedRoot)));
        }

        [Test]
        public void Initialization_FollowsTheSameOrderAsConstruction()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            var initialized = InjectionLog.Initialized;
            Assert.Less(initialized.IndexOf(nameof(InjectedMiddle)), initialized.IndexOf(nameof(InjectedRoot)));
        }

        [Test]
        public void ContextService_IsHandedTheGlobalInstance()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetGlobalService<IInjectedLeaf>(out var leaf));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IInjectedMiddle>(out var middle));
            Assert.AreSame(leaf, middle.Leaf, "the context service should share the Global instance, not a copy");
        }

        [Test]
        public void TwoPublicConstructors_WithTheAttribute_UsesTheMarkedOne()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);

            Assert.IsTrue(ServiceLocator.TryGetContextService<IMarkedConstructor>(out var service));
            Assert.IsTrue(service.TookTheMarkedOne);
        }

        [Test]
        public async Task AsyncService_InitializesOnlyAfterItsAsyncDependencySucceeded()
        {
            ServiceLocator.DiscoverServicesOfLifetime(Lifetime.ScopedContext, InjectionContexts.Clean);
            await UniTask.WaitUntil(() => ServiceLocator.IsContextContainerInitialized(InjectionContexts.Clean))
                .Timeout(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConfigurationState.Success, AsyncDependent.DependencyStateWhenInitialized,
                "the dependent started before its dependency had finished");

            var initialized = InjectionLog.Initialized;
            Assert.Less(initialized.IndexOf(nameof(AsyncDependency)), initialized.IndexOf(nameof(AsyncDependent)));
        }

        [Test]
        public void Cycle_SkipsBothSidesAndLeavesTheRestOfTheContainerStanding()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<ICycleA>(out _));
            Assert.IsFalse(ServiceLocator.TryGetContextService<ICycleB>(out _));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IFailingDependency>(out _), "the cycle took unrelated services with it");
        }

        [Test]
        public void ParameterThatIsNotAService_SkipsOnlyThatService()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<INonServiceParameter>(out _));
            Assert.IsTrue(ServiceLocator.TryGetContextService<IFailingDependency>(out _));
        }

        [Test]
        public void TwoPublicConstructors_WithoutTheAttribute_IsSkipped()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<ITwoConstructors>(out _));
        }

        [Test]
        public void SyncServiceDependingOnAnAsyncOne_IsFailedRatherThanRunEarly()
        {
            DiscoverRejectedContext();

            Assert.IsTrue(ServiceLocator.TryGetContextService<ISyncOnAsync>(out var service));
            Assert.AreEqual(ConfigurationState.Failed, service.GetConfigState());
            CollectionAssert.DoesNotContain(InjectionLog.Initialized, nameof(SyncOnAsyncService));
        }

        [Test]
        public void DependencyThatFailsInit_FailsItsDependentWithoutRunningIt()
        {
            DiscoverRejectedContext();

            Assert.IsTrue(ServiceLocator.TryGetContextService<IDependentOnFailure>(out var dependent));
            Assert.AreEqual(ConfigurationState.Failed, dependent.GetConfigState());
            CollectionAssert.DoesNotContain(InjectionLog.Initialized, nameof(DependentOnFailure));
        }

        [Test]
        public void ContextService_CannotTakeAServiceFromAnotherContext()
        {
            DiscoverRejectedContext();

            Assert.IsFalse(ServiceLocator.TryGetContextService<IAcrossContexts>(out _));
        }
    }
}
