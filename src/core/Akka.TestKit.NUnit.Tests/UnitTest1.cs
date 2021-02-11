using System;
using NUnit.Framework;

namespace Akka.TestKit.NUnit.Tests
{
    public class Tests : NUnit3.TestKit
    {
        [SetUp]
        public void Setup()
        {
            Console.SetOut(TestContext.Out);
        }

        [Test]
        public void Test1()
        {
            EventFilter.Info().Expect(3, () =>
            {
                Log.Error("error1");
                Log.Error("error2");
                Log.Info("info");
            });
        }
    }
}