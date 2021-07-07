FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY "PubSub.Client/PubSub.Client.csproj" "PubSub.Client/"
COPY "PubSub.Messages/PubSub.Messages.csproj" "PubSub.Messages/"
COPY "FSharp.PubSub.Messages/FSharp.PubSub.Messages.fsproj" "FSharp.PubSub.Messages/"
RUN dotnet restore "PubSub.Client/PubSub.Client.csproj"
COPY . .
COPY ./PubSub.Client/ PubSub.Client/
COPY ./PubSub.Messages/ PubSub.Messages/
COPY ./FSharp.PubSub.Messages/ FSharp.PubSub.Messages/
WORKDIR "/src/PubSub.Client"
RUN dotnet build "PubSub.Client.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PubSub.Client.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PubSub.Client.dll", "John_Docker"]
