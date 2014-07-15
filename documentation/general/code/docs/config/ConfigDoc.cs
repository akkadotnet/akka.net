/**
 * Copyright (C) 2009-2014 Typesafe Inc. <http://www.typesafe.com>
 */

using Akka.Actor;
using Akka.Configuration;

public static class ConfigDoc {
  public static ActorSystem CreateConfiguredSystem() {
    //#csharp-custom-config
    // make a Config with just your special setting
    Config myConfig = ConfigFactory.ParseString("something=somethingElse");
    // load the normal config stack (system props,
    // then application.conf, then reference.conf)
    Config regularConfig = ConfigFactory.Default();
    // override regular stack with myConfig
    Config combined = myConfig.WithFallback(regularConfig);

    // create ActorSystem
    ActorSystem system = ActorSystem.Create("myname", combined);
    //#csharp-custom-config
    return system;
  }
}