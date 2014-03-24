#I @"packages\fake\tools\"
#r "FakeLib.dll"

open System
open System.IO
open Fake

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let (!!) includes = (!! includes).SetBaseDirectory __SOURCE_DIRECTORY__

let binDir = "bin"

let release =
    File.ReadLines "RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

Target "Clean" <| fun () ->
    DeleteDir binDir

Target "AssemblyInfo" <| fun() ->
    for file in !! "src/**/AssemblyInfo.?s" do
        let title =
            file
            |> Path.GetDirectoryName
            |> Path.GetDirectoryName
            |> Path.GetFileName
        printfn "%s" title


Target "Build" <| fun () ->

    !!"Akka.sln"
    |> MSBuildRelease "" "Rebuild"
    |> ignore

    
Target "All" DoNothing

Target "Help" <| fun () ->
    List.iter printfn [
      "usage:"
      "build [target]"
      ""
      "Targets:"
      "* All  - Build all"
      "* Help - Display this help"
    ]

"Clean" ==> "AssemblyInfo" ==> "Build"
"Build" ==> "All"

RunTargetOrDefault "Help"


