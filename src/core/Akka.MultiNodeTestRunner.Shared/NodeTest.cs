//-----------------------------------------------------------------------
// <copyright file="NodeTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.MultiNodeTestRunner.Shared
{
    public class NodeTest
    {
        public int Node { get; set; }
        public string TestName { get; set; }
        public string TypeName { get; set; }
        public string MethodName { get; set; }
    }
}
