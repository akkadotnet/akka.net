//-----------------------------------------------------------------------
// <copyright file="AssemblyInfo.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: InternalsVisibleTo("Akka.Persistence.Query.Sql")]
[assembly: InternalsVisibleTo("Akka.Persistence.TCK")]
[assembly: InternalsVisibleTo("Akka.Streams.Tests")]
[assembly: InternalsVisibleTo("Akka.Streams.TestKit")]
[assembly: InternalsVisibleTo("Akka.Benchmarks")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("123b83e9-21f8-49a8-888a-3b1212ff21dc")]
