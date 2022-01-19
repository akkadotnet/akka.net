FROM mcr.microsoft.com/dotnet/sdk:6.0 
RUN echo $PWD
RUN ls
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/src/core/Akka.Streams.Tests
CMD ["/bin/bash", "-c", "dotnet restore Akka.Streams.Tests.csproj && dotnet test Akka.Streams.Tests.csproj --logger trx; exit 0"]