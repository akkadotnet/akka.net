//-----------------------------------------------------------------------
// <copyright file="AssemblyInfo.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Akka.MultiNode.TestAdapter;
using Xunit;

[assembly: TestFramework(typeof(MultiNodeTestFramework))]
