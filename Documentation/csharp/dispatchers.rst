.. _dispatchers-java:

Dispatchers
===================

An Akka ``MessageDispatcher`` is what makes Akka Actors "tick", it is the engine of the machine so to speak.
All ``MessageDispatcher`` implementations are also an ``ExecutionContext``, which means that they can be used
to execute arbitrary code, for instance :ref:`futures-java`.

Default dispatcher
------------------

Every ``ActorSystem`` will have a default dispatcher that will be used in case nothing else is configured for an ``Actor``.
The default dispatcher can be configured, and is by default a ``Dispatcher`` with the specified ``default-executor``.
If an ActorSystem is created with an ExecutionContext passed in, this ExecutionContext will be used as the default executor for all
dispatchers in this ActorSystem. If no ExecutionContext is given, it will fallback to the executor specified in
``akka.actor.default-dispatcher.default-executor.fallback``. By default this is a "fork-join-executor", which
gives excellent performance in most cases.

.. _dispatcher-lookup-java:

Looking up a Dispatcher
-----------------------

Dispatchers implement the :class:`ExecutionContext` interface and can thus be used to run :class:`Future` invocations etc.

.. includecode:: code/docs/dispatcher/DispatcherDocTest.java#lookup

Setting the dispatcher for an Actor
-----------------------------------

So in case you want to give your ``Actor`` a different dispatcher than the default, you need to do two things, of which the first is
is to configure the dispatcher:

.. includecode:: ../scala/code/docs/dispatcher/DispatcherDocSpec.scala#my-dispatcher-config

And here's another example that uses the "thread-pool-executor":

.. includecode:: ../scala/code/docs/dispatcher/DispatcherDocSpec.scala#my-thread-pool-dispatcher-config

For more options, see the default-dispatcher section of the :ref:`configuration`.

Then you create the actor as usual and define the dispatcher in the deployment configuration.

.. includecode:: ../java/code/docs/dispatcher/DispatcherDocTest.java#defining-dispatcher-in-config

.. includecode:: ../scala/code/docs/dispatcher/DispatcherDocSpec.scala#dispatcher-deployment-config

An alternative to the deployment configuration is to define the dispatcher in code.
If you define the ``dispatcher`` in the deployment configuration then this value will be used instead
of programmatically provided parameter.

.. includecode:: ../java/code/docs/dispatcher/DispatcherDocTest.java#defining-dispatcher-in-code

.. note::
    The dispatcher you specify in ``withDispatcher`` and the ``dispatcher`` property in the deployment 
    configuration is in fact a path into your configuration.
    So in this example it's a top-level section, but you could for instance put it as a sub-section,
    where you'd use periods to denote sub-sections, like this: ``"foo.bar.my-dispatcher"``

Types of dispatchers
--------------------

There are 4 different types of message dispatchers:

* Dispatcher

  - This is an event-based dispatcher that binds a set of Actors to a thread pool. It is the default dispatcher used if one is not specified.

  - Sharability: Unlimited

  - Mailboxes: Any, creates one per Actor

  - Use cases: Default dispatcher, Bulkheading

* PinnedDispatcher

  - This dispatcher dedicates a unique thread for each actor using it; i.e. each actor will have its own thread pool with only one thread in the pool.

  - Sharability: None

  - Mailboxes: Any, creates one per Actor

  - Use cases: Bulkheading

* SynchronizedDispatcher

  - This dispatcher runs invocations on the current SynchronizationContext only. 

  - Sharability: Unlimited

  - Mailboxes: Any, creates one per Actor

  - Use cases: UI based Actors

More dispatcher configuration examples
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

Configuring a ``PinnedDispatcher``:

.. includecode:: ../csharp/code/docs/dispatcher/DispatcherDocSpec.cs#my-pinned-dispatcher-config

And then using it:

.. includecode:: ../csharp/code/docs/dispatcher/DispatcherDocSpec.cs#defining-pinned-dispatcher

