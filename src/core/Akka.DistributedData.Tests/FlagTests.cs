using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Akka.DistributedData.Tests
{
    public class FlagTests
    {
        [Fact]
        public void AFlagMustBeAbleToSwitchOnOnce()
        {
            var f1 = new Flag();
            var f2 = f1.SwitchOn();
            var f3 = f2.SwitchOn();

            Assert.Equal(false, f2.Enabled);
            Assert.Equal(true, f2.Enabled);
            Assert.Equal(true, f3.Enabled);
        }

        [Fact]
        public void AFlagMustMergeByPickingTrue()
        {
            var f1 = new Flag(false);
            var f2 = f1.SwitchOn();

            var m1 = f1.Merge(f2);
            Assert.Equal(true, m1.Enabled);

            var m2 = f2.Merge(f1);
            Assert.Equal(true, m2.Enabled);
        }
    }
}
