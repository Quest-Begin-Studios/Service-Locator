using System;
using NUnit.Framework;

namespace QBS.ServiceLocator.Tests
{
    public class ContextTests
    {
        [TearDown]
        public void TearDown()
        {
            Context.ClearRegisteredContexts();
        }

        [Test]
        public void ImplicitConversion_RegistersNewValue()
        {
            Context context = 101;
            Assert.AreEqual(101, context.Value);
        }

        [Test]
        public void ImplicitConversion_SameValueTwice_ReturnsSameRegisteredContext()
        {
            Context first = 202;
            Context second = 202;
            Assert.AreEqual(first, second);
        }

        [Test]
        public void Constructor_ZeroValue_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Context(0));
        }

        [Test]
        public void Constructor_DuplicateValue_Throws()
        {
            _ = new Context(303);
            Assert.Throws<ArgumentException>(() => new Context(303));
        }

        [Test]
        public void EqualityOperators_MatchByValue()
        {
            Context a = 404;
            Context b = 404;
            Context c = 405;

            Assert.IsTrue(a == b);
            Assert.IsFalse(a == c);
            Assert.IsTrue(a != c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void ToString_ReturnsValueAsString()
        {
            Context context = 606;
            Assert.AreEqual("606", context.ToString());
        }

        [Test]
        public void ClearRegisteredContexts_AllowsValueReuse()
        {
            _ = new Context(707);
            Context.ClearRegisteredContexts();
            Assert.DoesNotThrow(() => new Context(707));
        }
    }
}
