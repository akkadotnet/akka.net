FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_file="core/Akka.Streams.Tests/Akka.Streams.Tests.csproj"
ENV test_filter=""
ENV run_count=2
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/src
CMD ["/bin/bash", "-c", "x=0; while [ $x -le ${run_count} ]; do dotnet test ${test_file} ${test_filter} --framework net6.0 --logger trx;  x=$(( $x + 1 )); sleep 5s; done"]