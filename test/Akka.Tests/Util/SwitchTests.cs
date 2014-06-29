using System;
using Akka.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Util
{
    [TestClass]
    public class SwitchTests : AkkaSpec
    {
        [TestMethod]
        public void OnAndOff()
        {
            var s = new Switch(false);
            Assert.IsTrue(s.IsOff, "Initially should be off");
            Assert.IsFalse(s.IsOn, "Initially should not be on");

            Assert.IsTrue(s.SwitchOn(), "Switch on from off should succeed");
            Assert.IsTrue(s.IsOn, "Switched on should be on");
            Assert.IsFalse(s.IsOff, "Switched on should not be off");

            Assert.IsFalse(s.SwitchOn(), "Switch on when already on should not succeed");
            Assert.IsTrue(s.IsOn, "Already switched on should be on");
            Assert.IsFalse(s.IsOff, "Already switched on should not be off");

            Assert.IsTrue(s.SwitchOff(), "Switch off from on should succeed");
            Assert.IsTrue(s.IsOff, "Switched off should be off");
            Assert.IsFalse(s.IsOn, "Switched off should not be on");

            Assert.IsFalse(s.SwitchOff(), "Switch off when already off should not succeed");
            Assert.IsTrue(s.IsOff, "Already switched off should be off");
            Assert.IsFalse(s.IsOn, "Already switched off should not be on");
        }

        [TestMethod]
        public void InitiallyOnShouldBeOn()
        {
            var s = new Switch(true);
            Assert.IsTrue(s.IsOn, "Switched on should be on");
            Assert.IsFalse(s.IsOff, "Switched on should not be off");
        }

        [TestMethod]
        public void Given_OffSwitch_When_SwitchOn_throws_exception_Then_Should_revert()
        {
            var s = new Switch(false);
            intercept<InvalidOperationException>(() => s.SwitchOn(() => { throw new InvalidOperationException(); }));
            Assert.IsTrue(s.IsOff);
            Assert.IsFalse(s.IsOn);
        }


        [TestMethod]
        public void Given_OnSwitch_When_SwitchOff_throws_exception_Then_Should_revert()
        {
            var s = new Switch(true);
            intercept<InvalidOperationException>(() => s.SwitchOff(() => { throw new InvalidOperationException(); }));
            Assert.IsTrue(s.IsOn);
            Assert.IsFalse(s.IsOff);
        }

        [TestMethod]
        public void RunActionWithoutLocking()
        {
            var s = new Switch(false);
            var actionRun = false;
            Assert.IsTrue(s.IfOff(() => { actionRun = true; }));
            Assert.IsTrue(actionRun);
            actionRun = false;
            Assert.IsFalse(s.IfOn(() => { actionRun = true; }));
            Assert.IsFalse(actionRun);

            s.SwitchOn();
            actionRun = false;
            Assert.IsTrue(s.IfOn(() => { actionRun = true; }));
            Assert.IsTrue(actionRun);

            actionRun = false;
            Assert.IsFalse(s.IfOff(() => { actionRun = true; }));
            Assert.IsFalse(actionRun);
        }


        [TestMethod]
        public void RunActionWithLocking()
        {
            var s = new Switch(false);
            var actionRun = false;
            Assert.IsTrue(s.WhileOff(() => { actionRun = true; }));
            Assert.IsTrue(actionRun);
            actionRun = false;
            Assert.IsFalse(s.WhileOn(() => { actionRun = true; }));
            Assert.IsFalse(actionRun);

            s.SwitchOn();
            actionRun = false;
            Assert.IsTrue(s.WhileOn(() => { actionRun = true; }));
            Assert.IsTrue(actionRun);

            actionRun = false;
            Assert.IsFalse(s.WhileOff(() => { actionRun = true; }));
            Assert.IsFalse(actionRun);
        }

    }


}