#!/usr/bin/env python3
"""An Akka.NET cluster worker in Python, certified by ACT — with a Flask-like interface for its actors.

The membership protocol (join, gossip/heartbeats, graceful leave) is handled by the akkaflask.Cluster
framework; the only application code is the echo actor, registered exactly like a Flask route:

    @app.actor("/user/echo")
    def echo(msg):
        return msg
"""

import argparse

from akkaflask import Cluster


def main():
    ap = argparse.ArgumentParser(description="Akka.NET cluster worker (Flask-like)")
    ap.add_argument("--seed", required=True,
                    help="seed URI, e.g. akka.tcp://ConformanceCluster@127.0.0.1:5110")
    ap.add_argument("--host", default="127.0.0.1", help="advertised host of this worker")
    ap.add_argument("--port", type=int, default=6300, help="advertised port of this worker")
    ap.add_argument("--run", type=int, default=20, help="seconds to stay Up when not leaving")
    ap.add_argument("--leave", default="true", help="leave the cluster gracefully before exiting")
    args = ap.parse_args()

    app = Cluster(args.seed, host=args.host, port=args.port)

    # Flask-like: the path is the actor's address; the return value is the reply to the sender.
    # A cluster broadcast router fans a message out to /user/echo on every node; we echo it back.
    @app.actor("/user/echo")
    def echo(msg):
        return msg

    app.run(leave=(args.leave == "true"), idle=args.run)


if __name__ == "__main__":
    main()
