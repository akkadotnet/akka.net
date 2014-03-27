#I @"packages\fake\tools\"
#r "FakeLib.dll"
#r "System.Xml.Linq"

open System
open System.IO
open Fake
open Fake.FileUtils

cd __SOURCE_DIRECTORY__
let (!!) includes = (!! includes).SetBaseDirectory __SOURCE_DIRECTORY__
let binDir = "bin"

let product = "Akka.net"
let authors = [
    "Roger Alsing"
    "Aaron Stannard"
    "Jérémie Chassaing"
    "Stefan Alfbo" ]
let copyright = "Copyright © Roger Asling 2013-2014"
let company = "Akka.net"
let description = "Akka .NET is a port of the popular Java/Scala framework Akka to .NET."
let tags = ["akka";"actors";"actor";"model";"Akka";"concurrency"]
let configuration = "Release"

let testOutput = "TestResults"

let release =
    File.ReadLines "RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

Target "Clean" <| fun () ->
    DeleteDir binDir

open AssemblyInfoFile
Target "AssemblyInfo" <| fun() ->
    for file in !! "src/**/AssemblyInfo.fs" do
        let title =
            file
            |> Path.GetDirectoryName
            |> Path.GetDirectoryName
            |> Path.GetFileName
        
        let version = release.AssemblyVersion + ".0"

        CreateFSharpAssemblyInfo file [ 
            Attribute.Title title
            Attribute.Product product
            Attribute.Description description
            Attribute.Copyright copyright
            Attribute.Company company
            Attribute.ComVisible false
            Attribute.CLSCompliant true
            Attribute.Version version
            Attribute.FileVersion version ]

        CreateCSharpAssemblyInfoWithConfig "src/SharedAssemblyInfo.cs" [
            Attribute.Company "Akka"
            Attribute.Copyright copyright
            Attribute.Trademark ""
            Attribute.Version version
            Attribute.FileVersion version ] { GenerateClass = false; UseNamespace = "System" }

Target "Build" <| fun () ->

    !!"Akka.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore

Target "CopyOutput" <| fun () ->
    
    let copyOutput project =
        let src = "src" @@ project @@ @"bin\release\"
        let dst = binDir @@ project
        CopyDir dst src allFiles
    [ "Akka"
      "Akka.Remote"
      "Akka.FSharp"
      "Akka.slf4net" ]
    |> List.iter copyOutput



Target "BuildRelease" DoNothing

Target "CleanTests" <| fun () ->
    DeleteDir testOutput

open MSTest
Target "RunTests" <| fun () ->
    let testAssemblies = !! "test/**/bin/release/*.Tests.dll"

    mkdir testOutput

    MSTest
    <| fun p -> { p with ResultsDir = testOutput }
    <| testAssemblies



module Xml =
    open System.Xml.Linq

    let load s = XDocument.Load (s:string)
    let xname = XName.op_Implicit
    let descendants n (d: XDocument) = d.Descendants (xname n)
    let (?) (e:XElement) n = 
        match e.Attribute (xname n) with
        | null -> ""
        | a -> a.Value

module Nuget = 
    open Xml

    let getDependencies packages =
        if fileExists packages then
            load packages
            |> descendants "package"
            |> Seq.map (fun d -> d?id, d?version)
            |> Seq.toList
        else []

    let getAkkaDependency project =
        match project with
        | "Akka" -> []
        | _ -> ["Akka", release.NugetVersion]


    let description project =
        match project with
        | "Akka.FSharp" -> "FSharp API support for Akka."
        | "Akka.Remote" -> "Remote actor support for Akka."
        | "Akka.slf4net" -> "slf4net logging adapter for Akka."
        | _ -> description

open Nuget

Target "Nuget" <| fun () ->
    let nugetDir = binDir @@ "nuget"
    let workingDir = binDir @@ "build"
    let libDir = workingDir @@ @"lib\net45\"

    CleanDir nugetDir

    for nuspec in !! "src/**/*.nuspec" do
        CleanDir workingDir

        let project = Path.GetFileNameWithoutExtension nuspec 
        let projectDir = Path.GetDirectoryName nuspec
        let releaseDir = projectDir @@ @"bin\Release"
        let packages = projectDir @@ "packages.config"

        ensureDirectory libDir
        CopyDir releaseDir libDir allFiles

        NuGetHelper.NuGet
        <| fun p ->
            { p with
                Description = description project
                Authors = authors
                Copyright = copyright
                Project =  project
                Properties = ["Configuration", "Release"]
                ReleaseNotes = release.Notes |> String.concat "\n"
                Version = release.NugetVersion
                Tags = tags |> String.concat " "
                OutputPath = nugetDir
                WorkingDir = workingDir
                AccessKey = getBuildParamOrDefault "nugetkey" ""
                Publish = hasBuildParam "nugetkey"

                Dependencies = getDependencies packages @ getAkkaDependency project
                 }    
        <| nuspec

    DeleteDir workingDir

Target "All" DoNothing

Target "Help" <| fun () ->
    List.iter printfn [
      "usage:"
      "build [target]"
      ""
      " Targets for building:"
      " * Build"
      " * Nuget Create nugets packages"
      " * All  (Build all)"
      ""
      " Targets for publishing:"
      " * Nuget nugetkey=<key> publish packages to nuget.org"

      " Other Targets"
      " * Help - Display this help"
    ]

"Clean" ==> "AssemblyInfo" ==> "Build" ==> "CopyOutput" ==> "BuildRelease"

"BuildRelease" ==> "Nuget"

"CleanTests" ==> "RunTests"

"BuildRelease" ==> "All"
"RunTests" ==> "All"
"Nuget" ==> "All"

RunTargetOrDefault "Help"