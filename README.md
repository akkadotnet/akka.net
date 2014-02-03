# Pigeon

*Alpha stage, do not use in production.*

###High performance Actor Model framework.
Pigeon is an unofficial port of the Scala/Java Akka actor model framework.

Why not Akka# or dotAkka? 
I simply assume that TypeSafe that makes Akka don’t want to be associated with spare time projects like this, so I try not to stain their brand name.
Pigeon tries to stay as close to the Akka implementation as possible while still beeing .NET idiomatic.

Read more on:

* [Getting started](../../wiki/Getting started)
* [Remoting](../../wiki/Remoting)
* [Hotswap](../../wiki/Hotswap)
* [Supervision](../../wiki/Supervision)
* [Performance](../../wiki/Performance)
* [The F# API](../../wiki/F#-API)

## Getting started
Write your first actor:
```csharp
public class Greet
{
    public string Who { get; set; }
}

public class GreetingActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => Console.WriteLine("Hello {0}", m.Who));
    }
}
```
Usage:
```csharp
var system = ActorSystem.Create("MySystem");
var greeter = system.ActorOf<GreetingActor>("greeter");
greeter.Tell(new Greet { Who = "Roger" });

```
##Remoting
Server:
```csharp
var config = ConfigurationFactory.ParseString(@"
# we use real Akka Hocon notation configs
akka { 
    remote {
        #this is the host and port the ActorSystem will listen to for connections
        server {
            host : ""127.0.0.1""
            port : 8080
        }
    }
}
");

using (var system = ActorSystem.Create("MyServer",config,new RemoteExtension())) 
{
    Console.ReadLine();
}
```
Client:
```csharp
var system = new ActorSystem();
var greeter = system.ActorSelection("http://localhost:8080/user/greeter");    
//pass a message to the remote actor
greeter.Tell(new Greet { Who = "Roger" });
```
    
##Code Hotswap
```csharp
public class GreetingActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => {
                Console.WriteLine("Hello {0}", m.Who);
                //this could also be a lambda
                Become(OtherReceive);
            });
    }
    
    void OtherReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => {
                Console.WriteLine("You already said hello!");
                //Unbecome() to revert to old behavior
            });
    }
}
```

##Supervision
```csharp
public class MyActor : UntypedActor
{
    private ActorRef logger = Context.ActorOf<LogActor>();

    // if any child, e.g. the logger above. throws an exception
    // apply the rules below
    // e.g. Restart the child if 10 exceptions occur in 30 seconds or less
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNumberOfRetries: 10, 
            duration: TimeSpan.FromSeconds(30), 
            decider: x =>
            {
                if (x is ArithmeticException)
                    return Directive.Resume;
                if (x is NotSupportedException)
                    return Directive.Stop;

                return Directive.Restart;
            });
    }
    
    ...
}
```

##Special F# API
```fsharp
type SomeActorMessages =
    | Greet of string
    | Hi

type SomeActor() =
    inherit Actor()

    override x.OnReceive message =
        match message with
        | :? SomeActorMessages as m ->  
            match m with
            | Greet(name) -> Console.WriteLine("Hello {0}",name)
            | Hi -> Console.WriteLine("Hello from F#!")
        | _ -> failwith "unknown message"

let system = ActorSystem.Create("FSharpActors")
let actor = system.ActorOf<SomeActor>("MyActor")

actor <! Greet "Roger"
actor <! Hi
```

#####Contribute
If you are interested in helping porting the actor part of Akka to .NET please let me know.

#####Contact
Twitter: http://twitter.com/rogeralsing  (@rogeralsing)

Mail: rogeralsing@gmail.com
