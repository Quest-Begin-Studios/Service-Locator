using NUnit.Framework;

namespace QBS.ServiceLocator.Tests
{
    public class ServiceAttributeTests
    {
        private interface IMarker : IService
        {
        }

        [TearDown]
        public void TearDown()
        {
            Context.ClearRegisteredContexts();
        }

        [Test]
        public void GlobalConstructor_SetsLifetimeAndServiceTypeAndDefaultContext()
        {
            var attribute = new ServiceAttribute(Lifetime.Global, typeof(IMarker));

            Assert.AreEqual(Lifetime.Global, attribute.Lifetime);
            Assert.AreEqual(typeof(IMarker), attribute.ServiceType);
            Assert.AreEqual(default(Context), attribute.Context);
        }

        [Test]
        public void SceneConstructor_SetsLifetimeAndServiceTypeAndDefaultContext()
        {
            var attribute = new ServiceAttribute(Lifetime.Scene, typeof(IMarker));

            Assert.AreEqual(Lifetime.Scene, attribute.Lifetime);
            Assert.AreEqual(typeof(IMarker), attribute.ServiceType);
            Assert.AreEqual(default(Context), attribute.Context);
        }

        [Test]
        public void ScopedContextConstructor_AlwaysSetsScopedContextLifetime()
        {
            Context context = 12345;
            var attribute = new ServiceAttribute(12345, typeof(IMarker));

            Assert.AreEqual(Lifetime.ScopedContext, attribute.Lifetime);
            Assert.AreEqual(typeof(IMarker), attribute.ServiceType);
            Assert.AreEqual(context, attribute.Context);
        }
    }
}
