# Multi-Node Testing Distributed Akka.NET Applications
 
One of the most powerful testing features of Akka.NET is its ability to create and simulate real-world network conditions such as latency, network partitions, process crashes, and more. Given that any of these can happen in a production environment it's important to be able to write tests t which validate your application's ability to correctly recover.

This is precisely what the Multi-Node TestKit and TestRunner (MNTR) does in Akka.NET.

### MNTR Components
The Akka.NET Multi-Node TestKit consists of the following publicly available NuGet packages