FROM mcr.microsoft.com/dotnet/sdk:6.0 
ENV test_name="Akka.Streams.Tests"
ENV test_name_file_extension="csproj"
ENV test_dir=src/core
RUN mkdir akka
COPY . ./akka
RUN ls
WORKDIR /akka/${test_dir}
CMD ["/bin/bash", "-c", "dotnet restore ${test_name}/${test_name}.${test_name_file_extension} && dotnet test ${test_name}/${test_name}.${test_name_file_extension} --logger trx; exit 0"]