namespace System
open System
open System.Reflection
open System.Runtime.InteropServices

[<assembly: AssemblyTitleAttribute("Akka.FSharp")>]
[<assembly: AssemblyProductAttribute("Akka.net")>]
[<assembly: AssemblyDescriptionAttribute("Akka .NET is a port of the popular Java/Scala framework Akka to .NET.")>]
[<assembly: AssemblyCopyrightAttribute("Copyright © Roger Alsing 2013-2014")>]
[<assembly: AssemblyCompanyAttribute("Akka.net")>]
[<assembly: ComVisibleAttribute(false)>]
[<assembly: CLSCompliantAttribute(true)>]
[<assembly: AssemblyVersionAttribute("0.6.2.0")>]
[<assembly: AssemblyFileVersionAttribute("0.6.2.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.6.2.0"
