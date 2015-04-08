using System;

namespace Akka.TestKit
{
// ReSharper disable once InconsistentNaming
    public interface ITestKitAssertions
    {
        void Fail(string format = "", params object[] args);
        void AssertTrue(bool condition, string format = "", params object[] args);
        void AssertFalse(bool condition, string format = "", params object[] args);
        void AssertEqual<T>(T expected, T actual, string format = "", params object[] args);
        void AssertEqual<T>(T expected, T actual, Func<T,T,bool> comparer, string format = "", params object[] args);
    }
}