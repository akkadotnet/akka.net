//-----------------------------------------------------------------------
// <copyright file="HoconBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Hocon
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class HoconBenchmarks
    {
        public const string hoconString = @"
          akka {  
            actor { 
              provider = remote
              deployment {
                /remoteecho {
                    remote = ""akka.tcp://DeployTarget@localhost:8090""
                }
              }              
            }
            remote {
              dot-netty.tcp {
                port = 8090
                hostname = localhost
              }
            }
            persistence {
                journal {
                    plugin = akka.persistence.journal.in-memory
                }
            }
          }
        ";

        public Config fallback1;
        public Config fallback2;
        public Config fallback3;

        [GlobalSetup]
        public void Setup()
        {
            fallback1 = ConfigurationFactory.ParseString(@"
            akka.actor.ask-timeout = 60s
            akka.actor.branch-factor = 0.25
            akka.persistence {
                journal.plugin = akka.persistence.journal.sql-server
                journal.sql-server {
                    auto-initialize = on
                }
            }");
            fallback2 = ConfigurationFactory.ParseString(@"
            akka.actor.provider = cluster
            akka.remote.dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
            }
            akka.cluster {
                seed-nodes = [ 
                    ""akka.tcp://system@localhost:8000/"",
                    ""akka.tcp://system@localhost:8001/""
                ]
            }");
            fallback3 = Persistence.Persistence.DefaultConfig();
        }

        [Benchmark]
        public string Hocon_parse_resolve_string_value()
        {
            return fallback2.GetString("akka.actor.provider", null);
        }

        [Benchmark]
        public int Hocon_parse_resolve_int_value()
        {
            return fallback2.GetInt("akka.remote.dot-netty.tcp.port", 0);
        }

        [Benchmark]
        public double Hocon_parse_resolve_double_value()
        {
            return fallback1.GetDouble("akka.actor.branch-factor", 0);
        }

        [Benchmark]
        public bool Hocon_parse_resolve_boolean_value()
        {
            return fallback1.GetBoolean("akka.persistence.journal.sql-server.auto-initialize", false);
        }

        [Benchmark]
        public TimeSpan Hocon_parse_resolve_TimeSpan_value()
        {
            return fallback1.GetTimeSpan("akka.actor.ask-timeout", null);
        }

        [Benchmark]
        public IEnumerable<string> Hocon_parse_resolve_string_list_value()
        {
            return fallback1.GetStringList("akka.cluster.seed-nodes", new string[] { });
        }

        [Benchmark]
        public Config Hocon_parse_raw()
        {
            return ConfigurationFactory.ParseString(hoconString);
        }

        [Benchmark]
        public Config Hocon_combine_fallbacks()
        {
            return fallback1.WithFallback(fallback2).WithFallback(fallback3);
        }
    }
}
