namespace System
open System
open System.Reflection
open System.Runtime.InteropServices

[<assembly: AssemblyTitleAttribute("Akka.FSharp")>]
[<assembly: AssemblyProductAttribute("Akka.NET")>]
[<assembly: AssemblyDescriptionAttribute("Akka.NET is a port of the popular Java/Scala framework Akka to .NET")>]
[<assembly: AssemblyCopyrightAttribute("Copyright © 2013-2015 Akka.NET Team")>]
[<assembly: AssemblyCompanyAttribute("Akka.NET Team")>]
[<assembly: ComVisibleAttribute(false)>]
[<assembly: CLSCompliantAttribute(true)>]
[<assembly: AssemblyVersionAttribute("1.0.6.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0.6.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.6.0"
