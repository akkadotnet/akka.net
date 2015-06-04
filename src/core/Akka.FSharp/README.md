#   Akka.NET F# API

### Actor system and configuration

Unlike default (C#) actor system, F#-aware systems should be created using `Akka.FSharp.System.create` function. This function differs from it's C# equivalent by providing additional F#-specific features - i.e. serializers allowing to serialize F# quotations for remote deployment process.

Example:

    open Akka.FSharp
    use system = System.create "my-system" (Configuration.load())

F# also gives you it's own actor system Configuration module with support of following functions:

-   `defaultConfig() : Config` - returns default F# Akka configuration.
-   `parse(hoconString : string) : Config` - parses a provided Akka configuration string.
-   `load() : Config` - loads an Akka configuration found inside current project's *.config* file.

### Creating actors with `actor` computation expression

Unlike C# actors, which represent object oriented nature of the language, F# is able to define an actor's logic in more functional way. It is done by using `actor` computation expression. In most of the cases, an expression inside `actor` is expected to be represented as self-invoking recursive function - also invoking an other functions while maintaining recursive cycle is allowed, i.e. to change actor's behavior or even to create more advanced constructs like Finite State Machines. 

It's important to remember, that each actor returning point should point to the next recursive function call - any other value returned will result in stopping current actor (see: [Actor Lifecycle](http://akkadotnet.github.io/wiki/Actor%20lifecycle)).

Example:

    let aref = 
        spawn system "my-actor" 
            (fun mailbox -> 
                let rec loop() = actor {
                    let! message = mailbox.Receive()
                    // handle an incoming message
                    return! loop()
                }
                loop())

Since construct used in an example above is quite popular, you may also use following shorthand functions to define message handler's behavior:

-   `actorOf (fn : 'Message -> unit) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned>` - uses a function, which takes a message as the only parameter. Mailbox parameter is injected by spawning functions.
-   `actorOf2 (fn : Actor<'Message> -> 'Message -> unit) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned>` - uses a function, which takes both the message and an Actor instance as the parameters. Mailbox parameter is injected by spawning functions.

Example:

    let handleMessage (mailbox: Actor<'a>) msg =
        match msg with
        | Some x -> printf "%A" x
        | None -> ()

    let aref = spawn system "my-actor" (actorOf2 handleMessage)
    let blackHole = spawn system "black-hole" (actorOf (fun msg -> ()))

#### Spawning actors

Paragraph above already has shown, how actors may be created with help of the spawning function. There are several spawning function, which may be used to instantiate actors:

-   `spawn (actorFactory : ActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) : ActorRef<'Message>` - spawns an actor using specified actor computation expression. The actor can only be used locally. 
-   `spawnOpt (actorFactory : ActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) (options : SpawnOption list) : ActorRef<'Message>` - spawns an actor using specified actor computation expression, with custom spawn option settings. The actor can only be used locally. 
-   `spawne (actorFactory : ActorRefFactory) (name : string) (expr : Expr<Actor<'Message> -> Cont<'Message, 'Returned>>) (options : SpawnOption list) : ActorRef<'Message>` - spawns an actor using specified actor computation expression, using an Expression AST. The actor code can be deployed remotely.
-   `spawnObj (actorFactory : ActorRefFactory) (name : string) (f : Quotations.Expr<(unit -> #ActorBase)>) : ActorRef` - spawns an actor using specified actor quotation. The actor can only be used locally.
-   `spawnObjOpt (actorFactory : ActorRefFactory) (name : string) (f : Quotations.Expr<(unit -> #ActorBase)>) (options : SpawnOption list) : ActorRef<'Message>` - spawns an actor using specified actor quotation, with custom spawn option settings. The actor can only be used locally.

All of these functions may be used with either actor system or actor itself. In the first case spawned actor will be placed under */user* root guardian of the current actor system hierarchy. In second option spawned actor will become child of the actor used as [actorFactory] parameter of the spawning function.

#### Dealing with disposable resources

When executing application logic inside receive function, be aware of a constant threat of stopping a current actor at any time for various reasons. This is an especially problematic situation when you're using a resource allocation - when actor will be stopped suddenly, you may be left with potentially heavy resources still waiting for being released.

Use `mailbox.Defer (deferredFunc)` in situations when you must ensure operation to be executed at the end of the actor lifecycle.

Example:

    let disposableActor (mailbox:Actor<_>) =
        let resource = new DisposableResource()
        mailbox.Defer ((resource :> IDisposable).Dispose)
        let rec loop () = 
            actor {
                let! msg = mailbox.Receive()
                return! loop ()   
            }
        loop()

### Actor spawning options

To be able to specifiy more precise actor creation behavior, you may use `spawnOpt` and `spawne` methods, both taking a list of `SpawnOption` values. Each specific option should be present only once in the collection. When a conflict occurs (more than one option of specified type has been found), the latest value found inside the list will be chosen.

-   `SpawnOption.Deploy(Akka.Actor.Deploy)` - defines deployment strategy for created actors (see: Deploy). This option may be used along with `spawne` function to enable remote actors deployment.
-   `SpawnOption.Router(Akka.Routing.RouterConfig)` - defines an actor to be a router as well as it's routing specifics (see: [Routing](http://akkadotnet.github.io/wiki/Routing)).
-   `SpawnOption.SupervisiorStrategy(Akka.Actor.SupervisiorStrategy)` - defines a supervisor strategy of the current actor. It will affect it's children (see: [Supervision](http://akkadotnet.github.io/wiki/Supervision)).
-   `SpawnOption.Dispatcher(string)` - defines a type of the dispatcher used for resources management for the created actors. (See: [Dispatchers](http://akkadotnet.github.io/wiki/Dispatchers))
-   `SpawnOption.Mailbox(string)` - defines a type of the mailbox used for the created actors. (See: [Mailboxes](http://akkadotnet.github.io/wiki/Mailbox))

Example (deploy actor remotely):

    open Akka.Actor
    let remoteRef = 
        spawne system "my-actor" <@ actorOf myFunc @> 
            [SpawnOption.Deploy (Deploy(RemoteScope(Address.Parse "akka.tcp://remote-system@127.0.0.1:9000/")))]

### Ask and tell operators

While you may use traditional `ActorRef.Tell` and `ActorRef.Ask` methods, it's more convenient to use dedicated `<!` and `<?` operators to perform related operations. Strongly typed actor refs will ensure at compile time, if passed messages are of unhandled type.

Example:

    aref <! message
    async { let! response = aref <? request }

### Actor selection

Actors may be referenced not only by `ActorRef`s, but also through actor path selection (see: [Addressing](http://akkadotnet.github.io/wiki/Addressing)). With F# API you may select an actor with known path using `select` function:

-   `select (path : string) (selector : ActorRefFactory) : TypedActorSelection<'Message>` - where path is a valid URI string used to recognize actor path, and the selector is either actor system or actor itself.

Example:

    let aref = spawn system "my-actor" (actorOf2 (fun mailbox m -> printfn "%A said %s" (mailbox.Self.Path) m))
    aref <! "one"
    let aref2 = select "akka://my-system/user/my-actor" system
    aref2 <! "two"

### Inboxes

Inboxes are actor-like objects used to be listened by other actors. They are a good choice to watch over other actors lifecycle. Some of the inbox-related functions may block current thread and therefore should not be used inside actors.

-   `inbox (system : ActorSystem) : Inbox` - creates a new inbox in provided actor system scope.
-   `receive (timeout : TimeSpan) (i : Inbox) : 'Message option` - receives a next message sent to the inbox. This is a blocking operation. Returns `None` if timeout occurred or message is incompatible with expected response type.
-   `filterReceive (timeout : TimeSpan) (predicate : 'Message -> bool) (i : Inbox) : 'Message option` - receives a next message sent to the inbox, which satisfies provided predicate. This is a blocking operation. Returns `None` if timeout occurred or message is incompatible with expected response type.
-   `asyncReceive (i : Inbox) : Async<'Message option>` - Awaits in async block for a next message sent to the inbox. Returns `None` if message is incompatible with expected response type.

Inboxes may be configured to accept a limited number of incoming messages (default is 1000):

    akka {
        actor {
            inbox {
                inbox-size = 30
            }
        }
    }

### Monitoring

Actors and Inboxes may be used to monitor lifetime of other actors. This is done by `monitor`/`demonitor` functions:

-   `monitor (subject: ActorRef) (watcher: ICanWatch) : ActorRef` - starts monitoring a referred actor.
-   `demonitor (subject: ActorRef) (watcher: ICanWatch) : ActorRef` - stops monitoring of the previously monitored actor.

Monitored actors will automatically send a `Terminated` message to their watchers when they die.

### Actor supervisor strategies

Actors have a place in their system's hierarchy trees. To manage failures done by the child actors, their parents/supervisors may decide to use specific supervisor strategies (see: [Supervision](http://akkadotnet.github.io/wiki/Supervision)) in order to react to the specific types of errors. In F# this may be configured using functions of the `Strategy` module:

-   `Strategy.OneForOne (decider : exn -> Directive) : SupervisorStrategy` - returns a supervisor strategy applicable only to child actor which faulted during execution.
-   `Strategy.OneForOne (decider : exn -> Directive, ?retries : int, ?timeout : TimeSpan) : SupervisorStrategy` - returns a supervisor strategy applicable only to child actor which faulted during execution. [retries] param defines a number of times, an actor could be restarted. If it's a negative value, there is not limit. [timeout] param defines a time window for number of retries to occur.
-   `OneForOne (decider : Expr<(exn -> Directive)>, ?retries : int, ?timeout : TimeSpan)  : SupervisorStrategy` - returns a supervisor strategy applicable only to child actor which faulted during execution. [retries] param defines a number of times, an actor could be restarted. If it's a negative value, there is not limit. [timeout] param defines a time window for number of retries to occur. **Strategies created this way may be serialized and deserialized on remote nodes** .
-   `Strategy.AllForOne (decider : exn -> Directive) : SupervisorStrategy` - returns a supervisor strategy applicable to each supervised actor when any of them had faulted during execution.
-   `Strategy.AllForOne (decider : exn -> Directive, ?retries : int, ?timeout : TimeSpan) : SupervisorStrategy` -  returns a supervisor strategy applicable to each supervised actor when any of them had faulted during execution. [retries] param defines a number of times, an actor could be restarted. If it's a negative value, there is not limit. [timeout] param defines a time window for number of retries to occur.
-   `AllForOne (decider : Expr<(exn -> Directive)>, ?retries : int, ?timeout : TimeSpan) : SupervisorStrategy` - returns a supervisor strategy applicable to each supervised actor when any of them had faulted during execution. [retries] param defines a number of times, an actor could be restarted. If it's a negative value, there is not limit. [timeout] param defines a time window for number of retries to occur. **Strategies created this way may be serialized and deserialized on remote nodes** .

Example:

    let aref = 
        spawnOpt system "my-actor" (actorOf myFunc) 
            [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error -> 
                match error with
                | :? ArithmeticException -> Directive.Escalate
                | _ -> SupervisorStrategy.DefaultDecider error )) ]
    
    let remoteRef = 
        spawne system "remote-actor" <@ actorOf myFunc @>
            [ SpawnOption.SupervisorStrategy (Strategy.OneForOne <@ fun error -> 
                match error with
                | :? ArithmeticException -> Directive.Escalate
                | _ -> SupervisorStrategy.DefaultDecider error ) @>
              SpawnOption.Deploy (Deploy (RemoteScope remoteNodeAddr)) ]

### Publish/Subscribe support

While you may use built-in set of the event stream methods (see: Event Streams), there is an option of using dedicated F# API functions:

-   `subscribe (ref: ActorRef<'Message>) (eventStream: Akka.Event.EventStream) : bool` - subscribes an actor reference to target channel of the provided event stream. Channels are associated with specific types of a message emitted by the publishers.
-   `unsubscribe (ref: ActorRef<'Message>) (eventStream: Akka.Event.EventStream) : bool` - unsubscribes an actor reference from target channel of the provided event stream.
-   `publish (event: 'Event) (eventStream: Akka.Event.EventStream) : unit` - publishes an event on the provided event stream. Event channel is resolved from event's type.

Example: 

    type Message = 
        | Subscribe 
        | Unsubscribe
        | Msg of ActorRef<Message> * string

    let subscriber = 
        spawn system "subscriber" 
            (actorOf2 (fun mailbox msg -> 
                let eventStream = mailbox.Context.System.EventStream
                match msg with
                | Msg (sender, content) -> printfn "%A says %s" (sender.Path) content
                | Subscribe -> subscribe mailbox.Self eventStream |> ignore
                | Unsubscribe -> unsubscribe mailbox.Self eventStream |> ignore ))
            
    let publisher = 
        spawn system "publisher" 
            (actorOf2 (fun mailbox msg -> 
                publish msg mailbox.Context.System.EventStream))

    subscriber <! Subscribe
    // wait for subscriber to subscribe to event stream
    System.Threading.Thread.Sleep 100
    publisher  <! Msg (publisher, "hello")
    subscriber <! Unsubscribe
    // wait for subscriber to unsubscribe to event stream
    System.Threading.Thread.Sleep 100
    publisher  <! Msg (publisher, "hello again")

### Logging

F# API supports two groups of logging functions - one that operates directly on strings and second (which may be recognized by *f* suffix in function names) which operates using F# string formating features. Major difference is performance - first one is less powerful, but it's also faster than the second one.

Both groups support logging on various levels (DEBUG, &lt;default&gt; INFO, WARNING and ERROR). Actor system's logging level may be managed through configuration, i.e.:

    akka {
        actor {
            # collection of loggers used inside actor system, specified by fully-qualified type name
            loggers = [ "Akka.Event.DefaultLogger, Akka" ]

            # Options: OFF, ERROR, WARNING, INFO, DEBUG
            logLevel = "DEBUG"
        }
    }

F# API provides following logging methods:

-   `log (level : LogLevel) (mailbox : Actor<'Message>) (msg : string) : unit`, `logf (level : LogLevel) (mailbox : Actor<'Message>) (format:StringFormat<'T, 'Result>) : 'T` - both functions takes an `Akka.Event.LogLevel` enum parameter to specify log level explicitly.
-   `logDebug`, `logDebugf` - message will be logged at Debug level.
-   `logInfo`, `logInfof` - message will be logged at Info level.
-   `logWarning`, `logWarningf` - message will be logged at Warning level.
-   `logError`, `logError` - message will be logged at Error level.
-   `logException (mailbox: Actor<'a>) (e : exn) : unit` - this function logs a message from provided `System.Exception` object at the Error level.

### Interop with Task Parallel Library

Since both TPL an Akka frameworks can be used for parallel processing, sometimes they need to work both inside the same application.

To operate directly between `Async` results and actors, use `pipeTo` function (and it's abbreviations in form of `<!|` and `|!>` operators) to inform actor about tasks ending their processing pipelines. Piping functions used on tasks will move async result directly to the mailbox of a target actor.

Example:

    open System.IO
    let handler (mailbox: Actor<obj>) msg = 
        match box msg with
        | :? FileInfo as fi -> 
            let reader = new StreamReader(fi.OpenRead())
            reader.AsyncReadToEnd() |!> mailbox.Self 
        | :? string as content ->
            printfn "File content: %s" content
        | _ -> mailbox.Unhandled()

    let aref = spawn system "my-actor" (actorOf2 handler)
    aref <! new FileInfo "Akka.xml"
