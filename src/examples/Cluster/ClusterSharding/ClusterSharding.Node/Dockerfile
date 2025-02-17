FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY ["./bin/Release/net8.0/publish", "."]
ENTRYPOINT ["dotnet", "ClusterSharding.Node.dll"]
