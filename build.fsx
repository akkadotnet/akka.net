#I @"packages\fake\tools\"
#r "FakeLib.dll"
#r "System.Xml.Linq"

open System
open System.IO
open Fake
open Fake.FileUtils

cd __SOURCE_DIRECTORY__
let (!!) includes = (!! includes).SetBaseDirectory __SOURCE_DIRECTORY__

//--------------------------------------------------------------------------------
// Information about the project for Nuget and Assembly info files
//--------------------------------------------------------------------------------


let product = "Akka.net"
let authors = [ "Roger Alsing"; "Aaron Stannard"; "Jérémie Chassaing"; "Stefan Alfbo" ]
let copyright = "Copyright © Roger Asling 2013-2014"
let company = "Akka.net"
let description = "Akka .NET is a port of the popular Java/Scala framework Akka to .NET."
let tags = ["akka";"actors";"actor";"model";"Akka";"concurrency"]
let configuration = "Release"

// Read release notes and version

let release =
    File.ReadLines "RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

//--------------------------------------------------------------------------------
// Directories

let binDir = "bin"
let testOutput = "TestResults"

let nugetDir = binDir @@ "nuget"
let workingDir = binDir @@ "build"
let libDir = workingDir @@ @"lib\net45\"

//--------------------------------------------------------------------------------
// Clean build results

Target "Clean" <| fun () ->
    DeleteDir binDir

//--------------------------------------------------------------------------------
// Generate AssemblyInfo files with the version for release notes 


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

//--------------------------------------------------------------------------------
// Build the solution

Target "Build" <| fun () ->

    !!"Akka.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore


//--------------------------------------------------------------------------------
// Copy the build output to bin directory
//--------------------------------------------------------------------------------

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

"Clean" ==> "AssemblyInfo" ==> "Build" ==> "CopyOutput" ==> "BuildRelease"


//--------------------------------------------------------------------------------
// Tests targets
//--------------------------------------------------------------------------------

//--------------------------------------------------------------------------------
// Clean test output

Target "CleanTests" <| fun () ->
    DeleteDir testOutput
//--------------------------------------------------------------------------------
// Run tests

open MSTest
Target "RunTests" <| fun () ->
    let testAssemblies = !! "test/**/bin/release/*.Tests.dll"

    mkdir testOutput

    MSTest
    <| fun p -> { p with ResultsDir = testOutput }
    <| testAssemblies

"CleanTests" ==> "RunTests"


//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

// Xml utilities to read dependencies from packages.config
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

    // extract dependencies from packages.config
    let getDependencies packages =
        if fileExists packages then
            load packages
            |> descendants "package"
            |> Seq.map (fun d -> d?id, d?version)
            |> Seq.toList
        else []

    // add Akka dependency for other projects
    let getAkkaDependency project =
        match project with
        | "Akka" -> []
        | _ -> ["Akka", release.NugetVersion]

    // selected nuget description
    let description project =
        match project with
        | "Akka.FSharp" -> "FSharp API support for Akka."
        | "Akka.Remote" -> "Remote actor support for Akka."
        | "Akka.slf4net" -> "slf4net logging adapter for Akka."
        | _ -> description

open Nuget

//--------------------------------------------------------------------------------
// Clean nuget directory

Target "CleanNuget" <| fun () ->
    CleanDir nugetDir

//--------------------------------------------------------------------------------
// Pack nuget for all projects
// Publish to nuget.org if nugetkey is specified

Target "Nuget" <| fun () ->

    for nuspec in !! "src/**/*.nuspec" do
        CleanDir workingDir

        let project = Path.GetFileNameWithoutExtension nuspec 
        let projectDir = Path.GetDirectoryName nuspec
        let releaseDir = projectDir @@ @"bin\Release"
        let packages = projectDir @@ "packages.config"

        ensureDirectory libDir
        !! (releaseDir @@ project + ".dll")
        ++ (releaseDir @@ project + ".xml")
        |> CopyFiles libDir

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

"CleanNuget" ==> "BuildRelease" ==> "Nuget"

//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

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

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "All" DoNothing

"BuildRelease" ==> "All"
"RunTests" ==> "All"
"Nuget" ==> "All"

RunTargetOrDefault "Help"