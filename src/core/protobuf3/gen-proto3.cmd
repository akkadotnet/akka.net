rem get binaries from https://github.com/google/protobuf/releases e.g. protoc-3.2.0-win32.zip for windows

protoc.exe --csharp_out ..\Akka.Remote\Proto\ ContainerFormats.proto
protoc.exe --csharp_out ..\Akka.Remote\Proto\ WireFormats.proto 
