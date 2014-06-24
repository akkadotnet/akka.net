#I @"packages\fake\tools\"
#r "FakeLib.dll"
#r "System.Xml.Linq"

open System
open System.IO
open Fake
open Fake.FileUtils

cd __SOURCE_DIRECTORY__

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

Target "Clean" <| fun _ ->
    DeleteDir binDir

//--------------------------------------------------------------------------------
// Generate AssemblyInfo files with the version for release notes 


open AssemblyInfoFile
Target "AssemblyInfo" <| fun _ ->
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

Target "Build" <| fun _ ->

    !!"Akka.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore


//--------------------------------------------------------------------------------
// Copy the build output to bin directory
//--------------------------------------------------------------------------------

Target "CopyOutput" <| fun _ ->
    
    let copyOutput project =
        let src = "src" @@ project @@ @"bin\release\"
        let dst = binDir @@ project
        CopyDir dst src allFiles
    [ "Akka"
      "Akka.Remote"
      "Akka.FSharp"
      "Akka.slf4net"
      "Akka.NLog" ]
    |> List.iter copyOutput

Target "BuildRelease" DoNothing



//--------------------------------------------------------------------------------
// Tests targets
//--------------------------------------------------------------------------------

//--------------------------------------------------------------------------------
// Clean test output

Target "CleanTests" <| fun _ ->
    DeleteDir testOutput
//--------------------------------------------------------------------------------
// Run tests

open MSTest
Target "RunTests" <| fun _ ->
    let testAssemblies = !! "test/**/bin/release/*.Tests.dll"

    mkdir testOutput

    MSTest
        (fun p -> { p with ResultsDir = testOutput })
        testAssemblies



//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

module Nuget = 
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
        | "Akka.NLog" -> "NLog logging adapter for Akka."
        | _ -> description

open Nuget

//--------------------------------------------------------------------------------
// Clean nuget directory

Target "CleanNuget" <| fun _ ->
    CleanDir nugetDir

//--------------------------------------------------------------------------------
// Pack nuget for all projects
// Publish to nuget.org if nugetkey is specified

Target "Nuget" <| fun _ ->

    for nuspec in !! "src/**/*.nuspec" do
        CleanDir workingDir

        let project = Path.GetFileNameWithoutExtension nuspec 
        let projectDir = Path.GetDirectoryName nuspec
        let releaseDir = projectDir @@ @"bin\Release"
        let packages = projectDir @@ "packages.config"

        let pack outputDir =
            NuGetHelper.NuGet
                (fun p ->
                    { p with
                        Description = description project
                        Authors = authors
                        Copyright = copyright
                        Project =  project
                        Properties = ["Configuration", "Release"]
                        ReleaseNotes = release.Notes |> String.concat "\n"
                        Version = release.NugetVersion
                        Tags = tags |> String.concat " "
                        OutputPath = outputDir
                        WorkingDir = workingDir
                        AccessKey = getBuildParamOrDefault "nugetkey" ""
                        Publish = hasBuildParam "nugetkey"
                        
                        Dependencies = getDependencies packages @ getAkkaDependency project })
                nuspec
        // pack nuget (with only dll and xml files)

        ensureDirectory libDir
        !! (releaseDir @@ project + ".dll")
        ++ (releaseDir @@ project + ".xml")
        |> CopyFiles libDir

        pack nugetDir

        // pack symbol packages (adds .pdb and sources)

        !! (releaseDir @@ project + ".pdb")
        |> CopyFiles libDir

        let nugetSrcDir = workingDir @@ @"src\"
        CreateDir nugetSrcDir

        let isCs = hasExt ".cs"
        let isFs = hasExt ".fs"
        let isAssemblyInfo f = (filename f).Contains("AssemblyInfo")
        let isSrc f = (isCs f || isFs f) && not (isAssemblyInfo f) 

        CopyDir nugetSrcDir projectDir isSrc
        DeleteDir (nugetSrcDir @@ "obj")
        DeleteDir (nugetSrcDir @@ "bin")

        // pack in working dir
        pack workingDir
        
        // copy to nuget directory with .symbols.nupkg extension
        let pkg = (!! (workingDir @@ "*.nupkg")) |> Seq.head

        let destFile = pkg |> filename |> changeExt ".symbols.nupkg" 
        let dest = nugetDir @@ destFile
        
        CopyFile dest pkg

    DeleteDir workingDir


//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

Target "Help" <| fun _ ->
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
      " * Help - Display this help" ]

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "All" DoNothing

// build dependencies
"Clean" ==> "AssemblyInfo" ==> "Build" ==> "CopyOutput" ==> "BuildRelease"

// tests dependencies
"CleanTests" ==> "RunTests"

// nuget dependencies
"CleanNuget" ==> "BuildRelease" ==> "Nuget"


"BuildRelease" ==> "All"
"RunTests" ==> "All"
"Nuget" ==> "All"

RunTargetOrDefault "Help"