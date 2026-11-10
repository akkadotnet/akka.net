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
| Method                       | ReminderCount | MaxBatchSize | Mean        | Error     | StdDev    | Reminders/sec |
|----------------------------- |-------------- |------------- |------------:|----------:|----------:|--------------:|
| **FetchAndCompleteAllReminders** | **1000**          | **1000**         |    **62.07 ms** | **245.95 ms** | **13.481 ms** |        **16,111** |
| **FetchAndCompleteAllReminders** | **1000**          | **5000**         |    **62.85 ms** | **148.13 ms** |  **8.120 ms** |        **15,911** |
| **FetchAndCompleteAllReminders** | **5000**          | **1000**         |   **334.16 ms** | **240.57 ms** | **13.186 ms** |        **14,963** |
| **FetchAndCompleteAllReminders** | **5000**          | **5000**         |   **325.84 ms** | **307.91 ms** | **16.877 ms** |        **15,345** |
| **FetchAndCompleteAllReminders** | **25000**         | **1000**         | **1,501.81 ms** | **630.07 ms** | **34.536 ms** |        **16,647** |
| **FetchAndCompleteAllReminders** | **25000**         | **5000**         | **1,362.50 ms** | **650.92 ms** | **35.679 ms** |        **18,349** |
