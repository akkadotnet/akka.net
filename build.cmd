@echo off

src\.nuget\nuget.exe update -self

src\.nuget\nuget.exe install FAKE -OutputDirectory src\packages -ExcludeVersion -Version 2.10.24

if not exist src\packages\SourceLink.Fake\tools\SourceLink.fsx ( 
  src\.nuget\nuget.exe install SourceLink.Fake -OutputDirectory src\packages -ExcludeVersion
)
cls

set encoding=utf-8
src\packages\FAKE\tools\FAKE.exe boot conf
src\packages\FAKE\tools\FAKE.exe build.fsx %*


