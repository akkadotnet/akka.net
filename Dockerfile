FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_file="core/Akka.Streams.Tests/Akka.Streams.Tests.csproj"
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/src
CMD ["/bin/bash", "-c", "dotnet restore ${test_file} && dotnet test ${test_file} --logger trx"]