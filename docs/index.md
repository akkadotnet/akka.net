---
documentType: index
title: Akka.NET Documentation
tagline: A straightforward approach to building distributed, high-scale applications in .NET
---
<style>
.subtitle {
    font-size:20px;
}
.jumbotron{
    text-align: center;
}
img.main-logo{
    width: 192px;
}
h2:before{
    display: none;
}
.featured-box-minimal h4:before {
    height: 0px;
    margin-top: 0px;
}
</style>

<div class="jumbotron">
    <div class="container">
      <img src="images/mainlogo.png" class="main-logo" />
      <h1 class="title">Try <strong>Akka.NET</strong> now!</h1>
      <h1 class="title"><small class="subtitle">Follow our tutorial and build your first Akka.NET application today.</small></h1>
      <div class="options">
        <a class="btn btn-lg btn-primary" href="community/whats-new/akkadotnet-v1.4.md">What's new in Akka.NET v1.4.0?</a>
        <a class="btn btn-lg btn-primary" href="articles/intro/tutorial-1.md">Get Started Now</a> <a class="btn btn-lg btn-primary" href="articles/intro/what-is-akka.md">Read the documentation</a>
      </div>
    </div>
</div>


<section>
    <div class="container">
        <h2 class="lead">Build powerful concurrent &amp; distributed applications <strong>more easily</strong>.</h2>
        <p class="lead">Akka.NET is a toolkit and runtime for building highly concurrent, distributed, and fault tolerant event-driven applications on <strong>.NET</strong> &amp; <strong>Mono</strong>.</p>
        <p class="lead">This community-driven port brings <strong>C#</strong> &amp; <strong>F#</strong> developers the capabilities of the original Akka framework in Java/Scala.</p>
        <p class="lead">Learn about Akka for the JVM <a href="http://akka.io" target="_blank">here</a>.</p>
    </div>
</section>

<!-- WELCOME -->
<section>
    <div class="container">
        <!-- FEATURED BOXES 3 -->
        <div class="row featured-box-minimal">

            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-arrows-alt"></i> Simple Concurrency &amp; Distribution</h4>
                <p>Asynchronous and Distributed by design. High-level abstractions like Actors and FSM.</p>
            </div>

            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-flash"></i> High Performance</h4>
                <p>50 million msg/sec on a single machine. Small memory footprint; ~2.5 million actors per GB of heap.</p>
            </div>

            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-shield"></i> Resilient by Design</h4>
                <p>Write systems that self-heal. Remote and/or local supervisor hierarchies.</p>
            </div>


            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-th-large"></i> Elastic & Decentralized</h4>
                <p>Adaptive load balancing, routing, partitioning and configuration-driven remoting.</p>
            </div>

            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-plus-circle"></i> Extensible</h4>
                <p>Use Akka.NET Extensions to adapt Akka to fit your needs.</p>
            </div>

            <div class="col-md-4 col-sm-6 col-xs-12">
                <h4><i class="fa fa-exclamation"></i> Open Source </h4>
                <p>Akka.NET is released under the Apache 2 license</p>
            </div>

        </div>
        <!-- /FEATURED BOXES 3 -->

    </div>
</section>
<!-- /WELCOME -->
<br>
<br>
<!-- PREMIUM -->
<section class="alternate">
    <div class="container">

        <div class="row">
            <div class="col-md-6">
<h2><strong>Actor</strong> Model</h2>
                <p class="lead">
The Actor Model provides a higher level of abstraction for writing concurrent and distributed systems. It alleviates the developer from having to deal with explicit locking and thread management, making it easier to write correct concurrent and parallel systems. </p>
                <p>Actors were defined in the 1973 paper by <a href="http://en.wikipedia.org/wiki/Carl_Hewitt">Carl Hewitt</a> but have been popularized by the Erlang language, and used for example at Ericsson with great success to build highly concurrent and reliable telecom systems.</p>
                <p><a href="/articles/intro/what-problems-does-actor-model-solve.html">Read more</a></p>
            </div>

            <div class="col-md-6 text-center">

                <img class="img-responsive img-rounded appear-animation" data-animation="fadeIn" style="border:2px solid white;width:100%;border-radius:10px" src="/images/actor.png" alt="" />
            </div>
        </div>
        <div class="row">
            <div class="col-md-6">
<h2><strong>Distributed</strong> by Default</h2>
                <p class="lead">
Everything in Akka.NET is designed to work in a distributed setting: all interactions of actors use purely message passing and everything is asynchronous.
</p>
                <p>This effort has been undertaken to ensure that all functions are available equally when running within a single process or on a cluster of hundreds of machines. The key for enabling this is to go from remote to local by way of optimization instead of trying to go from local to remote by way of generalization. See this classic paper for a detailed discussion on why the second approach is bound to fail.
                </p>
                <p><a href="/articles/Remoting">Read more</a></p>
            </div>

            <div class="col-md-6 text-center">
                <img class="img-responsive img-rounded appear-animation" data-animation="fadeIn" style="border:2px solid white;width:100%;border-radius:10px" src="/images/network.png" alt="" />
            </div>
        </div>
        <div class="row">
            <div class="col-md-6">
<h2><strong>Supervision</strong> &amp; monitoring</h2>
                <p class="lead">
Actors form a tree with actors being parents to the actors they've created.</p>
                <p>
As a parent, the actor is responsible for handling its children’s failures (so-called supervision), forming a chain of responsibility, all the way to the top. When an actor crashes, its parent can either restart or stop it, or escalate the failure up the hierarchy of actors.
This enables a clean set of semantics for managing failures in a concurrent, distributed system and allows for writing highly fault-tolerant systems that self-heal.</p>
<p><a href="/articles/concepts/supervision.html">Read more</a></p>
            </div>

            <div class="col-md-6 text-center">
                <img class="img-responsive img-rounded appear-animation" data-animation="fadeIn" style="border:2px solid white;width:100%;border-radius:10px" src="/images/supervision.png" alt="" />
            </div>
        </div>
    </div>
</section>
