#  The Obligatory Hello World
This example shows how to define and consume actors in both C# and F#

## Hello World using the C# API
#### Define a message:
```csharp
//Create an (immutable) message type that your actor will respond to
public class Greet
{
    public Greet(string who)
    {
        Who = who;
    }
    public string Who { get;private set; }
}
```
#### Define your actor using the `ReceiveActor` API
```csharp
//Create the actor class
public class GreetingActor : ReceiveActor
{
    public GreetingActor()
    {
        Receive<Greet>(greet => Console.WriteLine("Hello {0}", greet.Who));
    }
}
```

#### ..or using the `TypedActor` API
```csharp
public class GreetingActor : TypedActor , IHandle<Greet>
{
    public void Handle(Greet greet)
    {
        Console.WriteLine("Hello {0}!", greet.Who);
    }
}
```


#### Usage:
```csharp
//create a new actor system (a container for your actors)
var system = ActorSystem.Create("MySystem");
//create your actor and get a reference to it.
//this will be an "ActorRef", which is not a reference to the actual actor instance
//but rather a client or proxy to it
var greeter = system.ActorOf<GreetingActor>("greeter");
//send a message to the actor
greeter.Tell(new Greet("World"));
```
See also:
- [[Untyped actors]].
- [[Typed actors]].

## Hello World using the F# API

```fsharp
// Create an (immutable) message type that your actor will respond to
type Greet = Greet of string

let system = ActorSystem.Create "MySystem"

// Use F# computation expression with tail-recursive loop 
// to create an actor message handler and return a reference 
let greeter = spawn system "greeter" <| fun mailbox ->
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        match msg with
        | Greet who -> printf "Hello, %s!\n" who
        return! loop() }
    loop()

greeter <! Greet "World"
```