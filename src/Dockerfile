FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_file="core/Akka.Streams.Tests/Akka.Streams.Tests.csproj"
ENV test_filter=""
ENV run_count=2
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka
CMD ["/bin/bash", "-c", "x=1; c=0; while [ $x -le 1 ] && [ $c -le ${run_count} ]; do dotnet test ${test_file} ${test_filter} --framework net6.0 --logger trx; c=$(( $c + 1 )); if [ $? -eq 0 ]; then x=1; else x=0; fi;  done"]