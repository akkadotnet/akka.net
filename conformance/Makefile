# Convenience targets for verifying the ACT (Akka Conformance Tester) system by hand.
# Works on macOS (incl. Apple Silicon) and Linux. Requires: dotnet (10), go, node.

ROOT        := $(abspath $(dir $(lastword $(MAKEFILE_LIST)))/..)
ACTHOST_DLL := $(ROOT)/conformance/act-host/bin/Release/net10.0/act-host.dll
SEED_PORT    ?= 5110
SEED_SECONDS ?= 45

.PHONY: help verify verify-cs build seed clean

help:
	@echo "ACT conformance — make targets:"
	@echo "  make verify      Build everything and run C#, Go, and JS workers through the 10-step ladder"
	@echo "  make verify-cs   Run only the C# in-process conformance suite (positive + negative)"
	@echo "  make build       Build the reference seed (act-host) and the Go worker"
	@echo "  make seed        Start a reference seed in the foreground (prints SEED_URI; run a worker against it)"
	@echo "  make clean       Remove build artifacts"

verify:
	./verify.sh

verify-cs:
	dotnet test $(ROOT)/src/contrib/cluster/Akka.Cluster.Conformance.Tests/Akka.Cluster.Conformance.Tests.csproj \
		-c Release --framework net10.0

build:
	dotnet build $(ROOT)/conformance/act-host/ActHost.csproj -c Release
	cd $(ROOT)/conformance/go-worker && go build -o go-worker .

# Foreground reference seed for hands-on runs. Prints SEED_URI, then prints the ACT verdict + trace
# when the worker leaves (or after SEED_SECONDS). Run a worker against the printed URI in another shell.
seed: build
	dotnet $(ACTHOST_DLL) --port=$(SEED_PORT) --seconds=$(SEED_SECONDS)

clean:
	rm -f $(ROOT)/conformance/go-worker/go-worker
	-dotnet clean $(ROOT)/conformance/act-host/ActHost.csproj -c Release >/dev/null 2>&1
