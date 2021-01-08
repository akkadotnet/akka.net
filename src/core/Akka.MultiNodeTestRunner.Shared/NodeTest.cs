//-----------------------------------------------------------------------
// <copyright file="NodeTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.Shared
{
    public class NodeTest
    {
        public int Node { get; set; }
        public string Role { get; set; }
        public string TestName { get; set; }
        public string TypeName { get; set; }
        public string MethodName { get; set; }
        public string SkipReason { get; set; }
    }
}

