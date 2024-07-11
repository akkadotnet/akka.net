# Akka.Cluster.Cpu.Benchmark

This project is a standalone console CPU benchmark to measure Akka.Cluster actors CPU usage. It will: 

* Spin up a single bare minimum cluster node as a seed node, 
* Spins up a cluster with a predetermined size that uses that node as seed, and then 
* Collect CPU usage at regular interval and save the result in a comma separated value (.csv) file that can be imported into a spreadsheet application.

## Usage

To run this project directly using .NET CLI, use one of the following commands:

```powershell
dotnet run -c Release
dotnet run -c Release -- [OPTIONS]
```

To run this as a standalone compiled executable, use one of the following commands:

```powershell
./Akka.Cluster.Cpu.Benchmark.exe [OPTIONS]
dotnet run Akka.Cluster.Cpu.Benchmark.dll
dotnet run Akka.Cluster.Cpu.Benchmark.dll -- [OPTIONS]
```

## Options

| Option                      | Description                                                                                                        |
|-----------------------------|--------------------------------------------------------------------------------------------------------------------|
| -d, --sample-duration=VALUE | Sets the sample point duration in seconds.<br/>Default: 5 seconds                                                  |
| --delay=VALUE               | Sets the initial delay to wait for cluster to stabilize before benchmark starts in seconds.<br/>Default: 5 seconds |
| -s, --samples=VALUE         | Sets how many samples are taken during the benchmark.<br/>Default: 60 samples                                      |
| -c, --cluster-size=VALUE    | Sets how many nodes to be added to the cluster in addition to the tested node.<br/>Default: 9 nodes                |
| -w, --warm-up-count=VALUE   | Sets how many blank samples to be performed as benchmark warm-up before benchmark starts.<br/>Default: 5 samples   |
| -h, -?, --help              | Shows help                                                                                                         |

