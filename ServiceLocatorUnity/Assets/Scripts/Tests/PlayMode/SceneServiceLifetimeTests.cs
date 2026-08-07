using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QBS.ServiceLocator.PlayModeTests
{
    public interface IMultiSceneTestService : IService
    {
    }

    [Service(Lifetime.Scene, typeof(IMultiSceneTestService))]
    public class MultiSceneTestService : MonoBehaviour, IMultiSceneTestService
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

    public interface ISelfRegisteringSceneTestService : IService
    {
    }

    // Registers itself the way a real scene service does: from Awake, with no explicit scene.
    [Service(Lifetime.Scene, typeof(ISelfRegisteringSceneTestService))]
    public class SelfRegisteringSceneTestService : MonoBehaviour, ISelfRegisteringSceneTestService
    {
        public bool IsAsyncInit => false;

        private void Awake()
        {
            ServiceLocator.RegisterSceneService<ISelfRegisteringSceneTestService>(this);
        }

        bool IService.InitializeService()
        {
            return true;
        }
    }

    // Deliberately unattributed and not a Component — exists only to prove the persistent registration
    // path rejects a service whose scene cannot be read.
    public class NonComponentSceneService : IMultiSceneTestService
    {
        public bool IsAsyncInit => false;
    }

    public interface IPersistentTestService : IService
    {
    }

    // The loader-overlay / audio-rig shape: authored as a GameObject, but app-lived.
    [Service(Lifetime.PersistentScene, typeof(IPersistentTestService))]
    public class PersistentTestService : MonoBehaviour, IPersistentTestService
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

    public class SceneServiceLifetimeTests
    {
        private readonly List<Scene> _createdScenes = new List<Scene>();
        private readonly List<GameObject> _persistentObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Only covers logs raised during SetUp itself — the test runner resets this before the body runs,
            // so a test that provokes an error log must set it again. See IgnoreExpectedErrorLogs.
            LogAssert.ignoreFailingMessages = true;

            // Resets the locator's cached DontDestroyOnLoad scene along with every container, so tests that
            // depend on being the *first* persistent registrant don't inherit a capture from an earlier test.
            ServiceLocator.GameStart();
        }

        /// <summary>
        ///     Call at the top of any test whose body deliberately drives the locator into a logged error.
        ///     Without it the runner fails the test on the unhandled message, regardless of its assertions.
        /// </summary>
        private static void IgnoreExpectedErrorLogs()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var scene in _createdScenes)
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }

            _createdScenes.Clear();

            foreach (var persistentObject in _persistentObjects)
            {
                if (persistentObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(persistentObject);
                }
            }

            _persistentObjects.Clear();
            ServiceLocator.PurgeContainer(Lifetime.Scene);
            LogAssert.ignoreFailingMessages = false;
        }

        private Scene CreateScene(string label)
        {
            var scene = SceneManager.CreateScene($"{label}_{Guid.NewGuid():N}");
            _createdScenes.Add(scene);
            return scene;
        }

        /// <summary>
        ///     A plain root GameObject in <paramref name="scene"/>, deliberately <i>not</i> moved to
        ///     DontDestroyOnLoad — registration is what does that. Tracked so teardown can destroy it once it
        ///     is living outside any scene the fixture unloads.
        /// </summary>
        private T CreateRootServiceObject<T>(Scene scene) where T : Component
        {
            var gameObject = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            _persistentObjects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        /// <summary>
        ///     Creates the GameObject, moves it into <paramref name="scene"/>, and only then adds the component,
        ///     so that Awake observes the final <c>gameObject.scene</c> — exactly like an object authored in a scene.
        /// </summary>
        private static T CreateServiceObject<T>(Scene scene) where T : Component
        {
            var gameObject = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject.AddComponent<T>();
        }

        [UnityTest]
        public IEnumerator TwoScenes_RegisteringTheSameInterface_ResolveToDistinctInstances()
        {
            var sceneA = CreateScene("A");
            var sceneB = CreateScene("B");
            yield return null;

            var serviceA = CreateServiceObject<MultiSceneTestService>(sceneA);
            var serviceB = CreateServiceObject<MultiSceneTestService>(sceneB);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceA);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceB);

            Assert.AreSame(serviceA, ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneA));
            Assert.AreSame(serviceB, ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneB));
            Assert.AreNotSame(
                ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneA),
                ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneB));
        }

        [UnityTest]
        public IEnumerator DisposeSceneContainer_BeforeUnload_DisposesOnlyThatScenesServices()
        {
            var sceneA = CreateScene("A");
            var sceneB = CreateScene("B");
            yield return null;

            var serviceA = CreateServiceObject<MultiSceneTestService>(sceneA);
            var serviceB = CreateServiceObject<MultiSceneTestService>(sceneB);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceA);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceB);

            // Disposal is the caller's to sequence, and running it ahead of the unload is the point:
            // serviceA's GameObject is still alive here, so DisposeService can still touch Unity state.
            ServiceLocator.DisposeSceneContainer(sceneA);

            Assert.AreEqual(1, serviceA.DisposeCallCount);
            Assert.IsTrue(serviceA != null, "the service should still be a live object when it is disposed");
            Assert.AreEqual(0, serviceB.DisposeCallCount);
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(sceneA));
            Assert.IsNull(ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneA));
            Assert.AreSame(serviceB, ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneB));

            yield return SceneManager.UnloadSceneAsync(sceneA);

            Assert.AreEqual(1, serviceA.DisposeCallCount, "unloading must not dispose a second time");
            Assert.AreSame(serviceB, ServiceLocator.FetchSceneService<IMultiSceneTestService>(sceneB));
        }

        [UnityTest]
        public IEnumerator UnloadingAScene_WithoutDisposing_LeavesTheContainerToTheCaller()
        {
            // The locator does not subscribe to sceneUnloaded: scene lifetime is driven by whoever owns
            // the load, so an undisposed container outlives its scene. Documents the contract that makes
            // "dispose before you unload" mandatory rather than a nicety.
            var scene = CreateScene("A");
            yield return null;

            var service = CreateServiceObject<MultiSceneTestService>(scene);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(service);

            yield return SceneManager.UnloadSceneAsync(scene);

            Assert.AreEqual(0, service.DisposeCallCount);
            Assert.IsTrue(ServiceLocator.IsSceneContainerInitialized(scene));

            ServiceLocator.DisposeSceneContainer(scene);

            Assert.AreEqual(1, service.DisposeCallCount);
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(scene));
        }

        [UnityTest]
        public IEnumerator FetchSceneService_ByComponent_ResolvesTheCallersOwnScene()
        {
            var sceneA = CreateScene("A");
            var sceneB = CreateScene("B");
            yield return null;

            var serviceA = CreateServiceObject<MultiSceneTestService>(sceneA);
            var serviceB = CreateServiceObject<MultiSceneTestService>(sceneB);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceA);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceB);

            var callerInA = CreateServiceObject<MultiSceneTestService>(sceneA);
            var callerInB = CreateServiceObject<MultiSceneTestService>(sceneB);

            Assert.AreSame(serviceA, ServiceLocator.FetchSceneService<IMultiSceneTestService>(callerInA));
            Assert.AreSame(serviceB, ServiceLocator.FetchSceneService<IMultiSceneTestService>(callerInB));
            Assert.IsTrue(ServiceLocator.TryGetSceneService<IMultiSceneTestService>(callerInB, out var viaTryGet));
            Assert.AreSame(serviceB, viaTryGet);
        }

        [UnityTest]
        public IEnumerator FetchSceneService_ByComponent_SceneWithoutContainer_ReturnsNull()
        {
            var scene = CreateScene("A");
            yield return null;

            var caller = CreateServiceObject<MultiSceneTestService>(scene);

            Assert.IsNull(ServiceLocator.FetchSceneService<IMultiSceneTestService>(caller));
        }

        [UnityTest]
        public IEnumerator ComponentOverloads_DestroyedOrNullCaller_LogAndFailSafe()
        {
            var scene = CreateScene("A");
            yield return null;

            var service = CreateServiceObject<MultiSceneTestService>(scene);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(service);

            var destroyed = CreateServiceObject<MultiSceneTestService>(scene);
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);

            // `caller is Component` still passes for a destroyed component, so the guards must use
            // Unity's null operator rather than let gameObject.scene throw MissingReferenceException.
            IgnoreExpectedErrorLogs();
            Assert.IsNull(ServiceLocator.FetchSceneService<IMultiSceneTestService>(destroyed));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<IMultiSceneTestService>(destroyed, out _));
            Assert.DoesNotThrow(() => ServiceLocator.RegisterSceneService<IMultiSceneTestService>(destroyed));

            Assert.IsNull(ServiceLocator.FetchSceneService<IMultiSceneTestService>((Component) null));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<IMultiSceneTestService>(null, out _));

            // The live registration is untouched by any of the above.
            Assert.AreSame(service, ServiceLocator.FetchSceneService<IMultiSceneTestService>(scene));
        }

        [UnityTest]
        public IEnumerator SelfRegisteringServiceInAwake_LandsInItsOwnScenesContainer()
        {
            var sceneA = CreateScene("A");
            var sceneB = CreateScene("B");
            yield return null;

            var serviceA = CreateServiceObject<SelfRegisteringSceneTestService>(sceneA);
            var serviceB = CreateServiceObject<SelfRegisteringSceneTestService>(sceneB);

            Assert.AreSame(serviceA, ServiceLocator.FetchSceneService<ISelfRegisteringSceneTestService>(sceneA));
            Assert.AreSame(serviceB, ServiceLocator.FetchSceneService<ISelfRegisteringSceneTestService>(sceneB));
        }

        [UnityTest]
        public IEnumerator RegisteringTheSameInterfaceTwiceInOneScene_KeepsTheFirst()
        {
            var scene = CreateScene("A");
            yield return null;

            var first = CreateServiceObject<MultiSceneTestService>(scene);
            var second = CreateServiceObject<MultiSceneTestService>(scene);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(first);

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(second);

            Assert.AreSame(first, ServiceLocator.FetchSceneService<IMultiSceneTestService>(scene));
        }

        [UnityTest]
        public IEnumerator SceneContainerEvents_CarryTheOwningScene()
        {
            var created = new List<Scene>();
            var disposed = new List<Scene>();
            var registrations = new List<(Scene Scene, Type Type)>();

            void OnCreated(Scene s) => created.Add(s);
            void OnDisposed(Scene s) => disposed.Add(s);
            void OnRegistered(Scene s, Type t, IService _) => registrations.Add((s, t));

            ServiceLocator.SceneContainerCreated += OnCreated;
            ServiceLocator.SceneContainerDisposed += OnDisposed;
            ServiceLocator.SceneServiceRegistered += OnRegistered;

            try
            {
                var sceneA = CreateScene("A");
                var sceneB = CreateScene("B");
                yield return null;

                ServiceLocator.RegisterSceneService<IMultiSceneTestService>(CreateServiceObject<MultiSceneTestService>(sceneA));
                ServiceLocator.RegisterSceneService<IMultiSceneTestService>(CreateServiceObject<MultiSceneTestService>(sceneB));

                CollectionAssert.AreEqual(new[] { sceneA, sceneB }, created);
                CollectionAssert.AreEqual(
                    new[] { (sceneA, typeof(IMultiSceneTestService)), (sceneB, typeof(IMultiSceneTestService)) },
                    registrations);

                ServiceLocator.DisposeSceneContainer(sceneA);

                CollectionAssert.AreEqual(new[] { sceneA }, disposed);
            }
            finally
            {
                ServiceLocator.SceneContainerCreated -= OnCreated;
                ServiceLocator.SceneContainerDisposed -= OnDisposed;
                ServiceLocator.SceneServiceRegistered -= OnRegistered;
            }
        }

        [UnityTest]
        public IEnumerator RegisterPersistentSceneService_MovesTheCallerToDontDestroyOnLoadItself()
        {
            // The caller never says DontDestroyOnLoad: registration does it, so there is no ordering to get
            // wrong and no way to end up with a "persistent" service that dies with the scene it was authored in.
            var authoringScene = CreateScene("Bootstrap");
            yield return null;

            var service = CreateRootServiceObject<PersistentTestService>(authoringScene);
            Assert.AreEqual(authoringScene, service.gameObject.scene);

            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(service);

            Assert.AreNotEqual(authoringScene, service.gameObject.scene);
            Assert.AreSame(service, ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());

            yield return SceneManager.UnloadSceneAsync(authoringScene);

            Assert.IsTrue(service != null, "the service outlives the scene it was authored in");
            Assert.AreEqual(0, service.DisposeCallCount);
            Assert.AreSame(service, ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
        }

        [UnityTest]
        public IEnumerator PersistentService_IsReachableFromScenesLoadedLater()
        {
            var bootstrap = CreateScene("Bootstrap");
            yield return null;

            var service = CreateRootServiceObject<PersistentTestService>(bootstrap);
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(service);

            // A scene that did not exist when the service registered, and holds no copy of it.
            CreateScene("Later");
            yield return null;

            Assert.AreSame(service, ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
            Assert.IsTrue(ServiceLocator.TryGetPersistentSceneService<IPersistentTestService>(out var viaTryGet));
            Assert.AreSame(service, viaTryGet);
        }

        [UnityTest]
        public IEnumerator PersistentService_IsNotVisibleToOrdinarySceneResolution()
        {
            // Strict-local must survive: the persistent container is a separate, explicit lookup, never a
            // fallback that ordinary scene resolution quietly reaches into.
            var scene = CreateScene("A");
            yield return null;

            var service = CreateRootServiceObject<PersistentTestService>(scene);
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(service);

            var callerInScene = CreateServiceObject<MultiSceneTestService>(scene);

            Assert.IsNull(ServiceLocator.FetchSceneService<IPersistentTestService>(scene));
            Assert.IsFalse(ServiceLocator.TryGetSceneService<IPersistentTestService>(callerInScene, out _));
        }

        [UnityTest]
        public IEnumerator FetchPersistentSceneService_BeforeAnyRegistration_ReturnsNull()
        {
            yield return null;

            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
            Assert.IsFalse(ServiceLocator.TryGetPersistentSceneService<IPersistentTestService>(out _));
        }

        [UnityTest]
        public IEnumerator RegisterPersistentSceneService_NestedComponent_DoesNotRegister()
        {
            // Unity only honours DontDestroyOnLoad for root GameObjects, so a nested service would silently
            // stay in its scene and die there. It must be rejected instead.
            var scene = CreateScene("A");
            yield return null;

            var root = CreateRootServiceObject<MultiSceneTestService>(scene);
            var nested = new GameObject("Nested");
            nested.transform.SetParent(root.transform);
            var service = nested.AddComponent<PersistentTestService>();

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(service);

            Assert.AreEqual(scene, service.gameObject.scene);
            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
            Assert.IsNull(ServiceLocator.FetchSceneService<IPersistentTestService>(scene));
        }

        [UnityTest]
        public IEnumerator RegisterPersistentSceneService_NonComponent_DoesNotRegister()
        {
            yield return null;

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterPersistentSceneService<IMultiSceneTestService>(new NonComponentSceneService());

            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IMultiSceneTestService>());
        }

        [UnityTest]
        public IEnumerator RegisterPersistentSceneService_SceneLifetimeService_IsRejectedWithoutMovingIt()
        {
            // The DontDestroyOnLoad move cannot be undone, so a refusal has to land before it. Moving first
            // and validating second would strand the rejected object outside every scene for the session.
            var scene = CreateScene("A");
            yield return null;

            var service = CreateRootServiceObject<MultiSceneTestService>(scene);

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterPersistentSceneService<IMultiSceneTestService>(service);

            Assert.AreEqual(scene, service.gameObject.scene, "a rejected service must stay in the scene it was authored in");
            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IMultiSceneTestService>());
        }

        [UnityTest]
        public IEnumerator RegisterSceneService_PersistentLifetimeService_IsRejected()
        {
            // The other direction of the same rule: the attribute decides the lifetime, not the call site,
            // so a PersistentScene service cannot be quietly demoted into a per-scene container.
            var scene = CreateScene("A");
            yield return null;

            var service = CreateServiceObject<PersistentTestService>(scene);

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterSceneService<IPersistentTestService>(service);

            Assert.IsNull(ServiceLocator.FetchSceneService<IPersistentTestService>(scene));
            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
        }

        [UnityTest]
        public IEnumerator RegisterPersistentSceneService_Twice_KeepsTheFirstAndLeavesTheSecondInItsScene()
        {
            var scene = CreateScene("A");
            yield return null;

            var first = CreateRootServiceObject<PersistentTestService>(scene);
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(first);

            var second = CreateRootServiceObject<PersistentTestService>(scene);

            IgnoreExpectedErrorLogs();
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(second);

            Assert.AreSame(first, ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
            Assert.AreEqual(scene, second.gameObject.scene, "the rejected duplicate must not have been moved out of its scene");
        }

        [UnityTest]
        public IEnumerator PurgeContainer_PersistentScene_DisposesOnlyThePersistentContainer()
        {
            var scene = CreateScene("A");
            yield return null;

            var sceneService = CreateServiceObject<MultiSceneTestService>(scene);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(sceneService);

            var persistent = CreateRootServiceObject<PersistentTestService>(scene);
            ServiceLocator.RegisterPersistentSceneService<IPersistentTestService>(persistent);

            ServiceLocator.PurgeContainer(Lifetime.PersistentScene);

            Assert.AreEqual(1, persistent.DisposeCallCount);
            Assert.IsNull(ServiceLocator.FetchPersistentSceneService<IPersistentTestService>());
            Assert.AreEqual(0, sceneService.DisposeCallCount, "an ordinary scene container must be untouched");
            Assert.AreSame(sceneService, ServiceLocator.FetchSceneService<IMultiSceneTestService>(scene));
        }

        [UnityTest]
        public IEnumerator PurgeContainer_Scene_DisposesEveryScenesContainer()
        {
            var sceneA = CreateScene("A");
            var sceneB = CreateScene("B");
            yield return null;

            var serviceA = CreateServiceObject<MultiSceneTestService>(sceneA);
            var serviceB = CreateServiceObject<MultiSceneTestService>(sceneB);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceA);
            ServiceLocator.RegisterSceneService<IMultiSceneTestService>(serviceB);

            ServiceLocator.PurgeContainer(Lifetime.Scene);

            Assert.AreEqual(1, serviceA.DisposeCallCount);
            Assert.AreEqual(1, serviceB.DisposeCallCount);
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(sceneA));
            Assert.IsFalse(ServiceLocator.IsSceneContainerInitialized(sceneB));
        }
    }
}
