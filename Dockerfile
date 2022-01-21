FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_file="core/Akka.Streams.Tests/Akka.Streams.Tests.csproj"
ENV run_count=2
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/src
CMD ["/bin/bash", "-c", "dotnet restore ${test_file} && x=0; while [ $x -le ${run_count} ]; do dotnet test ${test_file} --logger trx;  x=$(( $x + 1 )); sleep 5s; done"]