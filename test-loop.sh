#!/bin/bash
for i in {1..20}
do
    echo "=== Run $i ==="
    dotnet test src/core/Akka.Streams.Tests/Akka.Streams.Tests.csproj -c Release \
        --filter "FullyQualifiedName~MergeHub_must_keep_working_even_if_one_of_the_producers_fail" \
        --no-build --verbosity quiet

    if [ $? -ne 0 ]; then
        echo "FAILED on run $i"
        exit 1
    fi
done

echo "All 20 runs passed successfully!"