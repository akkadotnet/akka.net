//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.NodeTestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var runner = new TestRunner();
            runner.InitializeWithTestArgs(args);
            runner.Run();
            Environment.Exit(runner.NoErrors ? 0 : 1);
        }
    }
}

