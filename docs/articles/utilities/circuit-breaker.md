---
uid: circuit-breaker
title: Circuit Breaker
---
# Circuit Breaker

## Why are they used?
A circuit breaker is used to provide stability and prevent cascading failures in distributed systems. These should be used in conjunction with judicious timeouts at the interfaces between remote systems to prevent the failure of a single component from bringing down all components.

As an example, we have a web application interacting with a remote third party web service. Let's say the third party has oversold their capacity and their database melts down under load. Assume that the database fails in such a way that it takes a very long time to hand back an error to the third party web service. This in turn makes calls fail after a long period of time. Back to our web application, the users have noticed that their form submissions take much longer seeming to hang. Well the users do what they know to do which is use the refresh button, adding more requests to their already running requests. This eventually causes the failure of the web application due to resource exhaustion. This will affect all users, even those who are not using functionality dependent on this third party web service.

Introducing circuit breakers on the web service call would cause the requests to begin to fail-fast, letting the user know that something is wrong and that they need not refresh their request. This also confines the failure behavior to only those users that are using functionality dependent on the third party, other users are no longer affected as there is no resource exhaustion. Circuit breakers can also allow savvy developers to mark portions of the site that use the functionality unavailable, or perhaps show some cached content as appropriate while the breaker is open.

The Akka.NET library provides an implementation of a circuit breaker called `Akka.Pattern.CircuitBreaker` which has the behavior described below.

## What do they do?

* During normal operation, a circuit breaker is in the `Closed` state:
	* Exceptions or calls exceeding the configured `СallTimeout` increment a
	  failure counter
	* Successes reset the failure count to zero
	* When the failure counter reaches a `MaxFailures` count, the breaker is
	  tripped into `Open` state
* While in `Open` state:
	* All calls fail-fast with a `CircuitBreakerOpenException`
	* After the configured `ResetTimeout`, the circuit breaker enters a
	  `Half-Open` state
* In `Half-Open` state:
	* The first call attempted is allowed through without failing fast
	* All other calls fail-fast with an exception just as in `Open` state
	* If the first call succeeds, the breaker is reset back to `Closed` state
	* If the first call fails, the breaker is tripped again into the `Open` state
	  for another full `ResetTimeout`
* State transition listeners:
	* Callbacks can be provided for every state entry via `OnOpen`, `OnClose`,
	  and `OnHalfOpen`
	* These are executed in the `ExecutionContext` provided.

![Circuit breaker states](/images/circuit-breaker-states.png)

## Examples

### Initialization

Here's how a `CircuitBreaker` would be configured for:
  * 5 maximum failures
  * a call timeout of 10 seconds
  * a reset timeout of 1 minute

[!code-csharp[Main](../../examples/DocsExamples/Utilities/CircuitBreakerDocSpec.cs?name=circuit-breaker-usage)]

### Call Protection

Here's how the `CircuitBreaker` would be used to protect an asynchronous
call as well as a synchronous one:

[!code-csharp[Main](../../examples/DocsExamples/Utilities/CircuitBreakerDocSpec.cs?name=call-protection)]

```csharp
dangerousActor.Tell("is my middle name");
// This really isn't that dangerous of a call after all

dangerousActor.Tell("block for me");
dangerousActor.Tell("block for me");
dangerousActor.Tell("block for me");
dangerousActor.Tell("block for me");
dangerousActor.Tell("block for me");

// My CircuitBreaker is now open, and will not close for one minute

// My CircuitBreaker is now half-open

dangerousActor.Tell("is my middle name");
// My CircuitBreaker is now closed
// This really isn't that dangerous of a call after all
```
