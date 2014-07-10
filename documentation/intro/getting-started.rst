Getting Started
===============

Prerequisites
-------------


Getting Started Guides and Template Projects
--------------------------------------------


Download
--------

There are several ways to download Akka.NET. 


Modules
-------

Akka.NET is very modular and consists of several assemblies containing different features.

- ``akka.actor`` – Classic Actors, Typed Actors etc.

  server

- ``akka.remote`` – Remote Actors

- ``akka.slf4net`` – SLF4NET Logger (event bus listener)

- ``akka.testkit`` – Toolkit for testing Actor systems

In addition to these stable modules there are several which are on their way
into the stable core but are still marked “experimental” at this point. This
does not mean that they do not function as intended, it primarily means that
their API has not yet solidified enough in order to be considered frozen. You
can help accelerating this process by giving feedback on these modules on our
mailing list.


How to see the JARs dependencies of each Akka module is described in the
:ref:`dependencies` section.

Using a release distribution
----------------------------


Using a snapshot version
------------------------


Microkernel
-----------

The Akka distribution includes the microkernel. To run the microkernel put your
application jar in the ``deploy`` directory and use the scripts in the ``bin``
directory.

.. _build-tool:


Build from sources
------------------

Akka.NET uses Git and is hosted at `Github <http://github.com>`_.

* Akka: clone the Akka repository from `<http://github.com/akkadotnet/akka.net>`_

Continue reading the page on :ref:`building-akka`

Need help?
----------

If you have questions you can get help on the `Akka Mailing List <http://groups.google.com/group/akkadotnet-user>`_.

Thanks for being a part of the Akka.NET community.

