
```csharp
var scope = new RemoteScope(new Address("akka.tcp", "remote-system", "localhost", 8080));
var deploy = new Deploy(scope);
var remote = system.ActorOf(Props.Create(() => new SomeActor("some arg", 123))
     .WithDeploy(deploy) , "remoteactor");
```