FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_file="Akka.Streams.Tests.csproj"
ENV test_dir="src/core"
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/${test_dir}
CMD ["/bin/bash", "-c", "dotnet restore ${test_dir}/${test_file} && dotnet test ${test_dir}/${test_file} --logger trx; exit 0"]