.. _developer_guidelines:

Developer Guidelines
====================

.. note::

 

Code Style
----------


Process
-------

* Make sure you have signed the Akka.NET CLA, if not, `sign it online <http://www.typesafe.com/contribute/cla>`_.
* Pick a ticket, if there is no ticket for your work then create one first.
* Start working in a feature branch. Name it something like ``wip-<ticket number>-<descriptive name>-<your username>``.
* When you are done, create a GitHub Pull-Request towards the targeted branch and email the Akka Mailing List that you want it reviewed
* When there's consensus on the review, someone from the Akka Core Team will merge it.

Commit messages
---------------

Please follow these guidelines when creating public commits and writing commit messages.

1. If your work spans multiple local commits (for example; if you do safe point commits while working in a topic branch or work in a branch for long time doing merges/rebases etc.) then please do **not** commit it all but rewrite the history by squashing the commits into a single big commit which you write a good commit message for (like discussed below). Here is a great article for how to do that: `http://sandofsky.com/blog/git-workflow.html <http://sandofsky.com/blog/git-workflow.html>`_. Every commit should be able to be used in isolation, cherry picked etc.

2. First line should be a descriptive sentence what the commit is doing. It should be possible to fully understand what the commit does by just reading this single line. It is **not** ok to only list the ticket number, type "minor fix" or similar. Include reference to ticket number, prefixed with #, at the end of the first line. If the commit is a **small** fix, then you are done. If not, go to 3.

3. Following the single line description should be a blank line followed by an enumerated list with the details of the commit.

Example::

    Completed replication over BookKeeper based transaction log. Fixes #XXX

      * Details 1
      * Details 2
      * Details 3

Testing
-------


Actor TestKit
^^^^^^^^^^^^^

There is a useful test kit for testing actors: `akka.util.TestKit <@github@/akka-testkit/src/main/scala/akka/testkit/TestKit.scala>`_. It enables assertions concerning replies received and their timing, there is more documentation in the :ref:`akka-testkit` module.


