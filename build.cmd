@echo off

src\.nuget\NuGet.exe update -self

src\.nuget\NuGet.exe install FAKE -OutputDirectory src\packages -ExcludeVersion -Version 2.10.24

src\.nuget\NuGet.exe install xunit.runners -OutputDirectory src\packages\FAKE -ExcludeVersion -Version 1.9.2

if not exist src\packages\SourceLink.Fake\tools\SourceLink.fsx ( 
  src\.nuget\nuget.exe install SourceLink.Fake -OutputDirectory src\packages -ExcludeVersion
)
cls

set encoding=utf-8
src\packages\FAKE\tools\FAKE.exe build.fsx %*


