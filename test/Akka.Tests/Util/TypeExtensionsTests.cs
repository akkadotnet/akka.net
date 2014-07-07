using System.Collections;
using System.Collections.Generic;
using Akka.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Util
{
    [TestClass]
    public class TypeExtensionsTests
    {
        [TestMethod]
        public void TestGenericImplements()
        {
            typeof(object[]).Implements<IEnumerable>().ShouldBe(true);
            typeof(object[]).Implements<string>().ShouldBe(false);
            typeof(List<string>).Implements<IEnumerable<string>>().ShouldBe(true);
            typeof(List<string>).Implements<IEnumerable<int>>().ShouldBe(false);
        }

        [TestMethod]
        public void TestNongenericImplements()
        {
            typeof(object[]).Implements(typeof(IEnumerable)).ShouldBe(true);
            typeof(object[]).Implements(typeof(string)).ShouldBe(false);
            typeof(List<string>).Implements(typeof(IEnumerable<string>)).ShouldBe(true);
            typeof(List<string>).Implements(typeof(IEnumerable<int>)).ShouldBe(false);
        }
    }
}