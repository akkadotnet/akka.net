using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class HoconTests
    {
        [TestMethod]
        public void FallbackAftetPropertyPath()
        {
            var hocon = @"
a {
   b.c.d {
        e = 1
        f = 2
    }
    g {
      h = 3
    }
}
";
            var res = Parser.Parse(hocon);

        }
    }
}
