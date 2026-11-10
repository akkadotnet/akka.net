```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 9.0.13 (9.0.13, 9.0.1326.6317), X64 RyuJIT x86-64-v3
  MediumRun : .NET 9.0.13 (9.0.13, 9.0.1326.6317), X64 RyuJIT x86-64-v3

Job=MediumRun  InvocationCount=1  IterationCount=3
LaunchCount=1  RunStrategy=Monitoring  UnrollFactor=1
WarmupCount=1

```
| Method                       | ReminderCount | MaxBatchSize | Mean        | Error       | StdDev     | Reminders/sec |
|----------------------------- |-------------- |------------- |------------:|------------:|-----------:|--------------:|
| **FetchAndCompleteAllReminders** | **1000**          | **1000**         |    **60.14 ms** |   **249.17 ms** |  **13.658 ms** |        **16,627** |
| **FetchAndCompleteAllReminders** | **1000**          | **5000**         |    **63.86 ms** |   **197.41 ms** |  **10.821 ms** |        **15,660** |
| **FetchAndCompleteAllReminders** | **5000**          | **1000**         |   **318.84 ms** |   **441.94 ms** |  **24.224 ms** |        **15,682** |
| **FetchAndCompleteAllReminders** | **5000**          | **5000**         |   **280.50 ms** |   **124.11 ms** |   **6.803 ms** |        **17,825** |
| **FetchAndCompleteAllReminders** | **25000**         | **1000**         | **1,440.13 ms** | **1,459.32 ms** |  **79.990 ms** |        **17,360** |
| **FetchAndCompleteAllReminders** | **25000**         | **5000**         | **1,349.03 ms** | **1,834.87 ms** | **100.576 ms** |        **18,532** |
