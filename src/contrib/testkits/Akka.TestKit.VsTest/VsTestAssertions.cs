using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.TestKit.VsTest
{
    /// <summary>
    /// Assertions for Visual Studio Unit Testing Framework
    /// </summary>
    public class VsTestAssertions : ITestKitAssertions
    {
        public void Fail(string format = "", params object[] args)
        {
            Assert.Fail(format, args);
        }

        public void AssertTrue(bool condition, string format = "", params object[] args)
        {
            Assert.IsTrue(condition,format, args);
        }

        public void AssertFalse(bool condition, string format = "", params object[] args)
        {

            Assert.IsFalse(condition,format, args);
        }

        public void AssertEqual<T>(T expected, T actual, string format = "", params object[] args)
        {
            Assert.AreEqual(expected,actual,format,args);
        }

        public void AssertEqual<T>(T expected, T actual, Func<T, T, bool> comparer, string format = "", params object[] args)
        {            
            if(!comparer(expected, actual))
                throw new AssertFailedException(string.Format("Assert.AreEqual failed. Expected [{0}]. Actual [{1}]. {2}", FormatValue(expected), FormatValue(actual), string.Format(format,args)));
        }

        private static string FormatValue<T>(T expected)
        {
            return ReferenceEquals(expected, null) ? "null" : expected.ToString();
        }

    }
}