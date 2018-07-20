---
uid: mailboxes
title: Mailboxes
---

# Mailboxes

## What Mailboxes Do?

In Akka.NET, Mailboxes hold messages that are destined for an actor. When you send a message to an actor, the message doesn't go directly to the actor, but goes to the actor's mailbox until the actor has time to process it.

A mailbox can be described as a queue of messages. Messages are usually then delivered from the mailbox to the actor one at a time in the order they were received, but there are implementations like the [Priority Mailbox](#unboundedprioritymailbox) that can change the order of delivery.

Normally every actor has its own mailbox, but this is not a requirement. There are implementations of [routers](xref:routers) where all routees share a single mailbox.

## Using a Mailbox

To make an actor use a specific mailbox, you can set it up one of the following locations:

1. In the actor's props

  ```cs
  Props.Create<ActorType>().WithMailbox("my-custom-mailbox");
  ```

2. In the actor's deployment configuration

  ```hocon
  akka.actor.deployment {
      /my-actor-path {
          mailbox = my-custom-mailbox
      }
  }  
  ```

The `my-custom-mailbox` is a key that was setup using the [mailbox configuration](#configuring-custom-mailboxes).

## Configuring Custom Mailboxes

In order to use a custom mailbox, it must be first configured with a key that the actor system can lookup. You can do this using a custom HOCON setting.

The example below will setup a mailbox key called `my-custom-mailbox` pointing to a custom mailbox implementation. Note that the configuration of the mailbox is outside the akka section.

```hocon
akka { ... }
my-custom-mailbox {
    mailbox-type : "MyProject.CustomMailbox, MyProjectAssembly"
}
```

## Built-in Mailboxes

### UnboundedMailbox

**This is the default mailbox** used by Akka.NET. It's a non-blocking unbounded mailbox, and should be good enough for most cases.

### UnboundedPriorityMailbox

The unbounded mailbox priority mailbox is blocking mailbox that allows message prioritization, so that you can choose the order the actor should process messages that are already in the mailbox.

In order to use this mailbox, you need to extend the `UnboundedPriorityMailbox` class and provide an ordering logic. The value returned by the `PriorityGenerator` method will be used to order the message in the mailbox. Lower values will be delivered first. Delivery order for messages of equal priority is undefined.

```cs
public class IssueTrackerMailbox : UnboundedPriorityMailbox
{
  protected override int PriorityGenerator(object message)
  {
	  var issue = message as Issue;

	  if (issue != null)
	  {
		  if (issue.IsSecurityFlaw)
			  return 0;

		  if (issue.IsBug)
			  return 1;
	  }

	  return 2;
  }
}
```

Once the class is implemented, you should set it up using the [instructions above](#using-a-mailbox).

> [!WARNING]
> While we have updated the `UnboundedPriorityMailbox` to support Stashing. We don't recommend using it.
Once you stash messages, they are no longer part of the prioritization process that your PriorityMailbox uses. Once you unstash all messages, they are fed to the Actor, in the order of stashing.
