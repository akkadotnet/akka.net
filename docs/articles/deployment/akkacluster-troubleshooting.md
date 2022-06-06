---
uid: cluster-troubleshooting
title: Troubleshooting Akka.Cluster
---

# Troubleshooting Akka.Cluster

[Akka.Cluster](xref:cluster-overview) is designed to support highly available distributed Akka.NET applications and it can operate at large scale. However, prior to deploying Akka.Cluster into a large-scale environment it's useful to know how to troubleshoot various problems that may occur at runtime or exceptions you might see in your Akka.NET logs. This guide explains how to troubleshoot some routine problems that may occur with Akka.Cluster.

## Network Splits and Split Brains

Even during hostile network conditions Akka.Cluster should not break apart into multiple clusters - and when configured correctly Akka.Cluster shouldn't be able to form multiple clusters during launch either. If your cluster is breaking apart into multiple discrete clusters or partitions that are unreachable, this guide will help you troubleshoot potential root causes and fixes for this issue.

### Cluster Can't Reform After Network Partition

When this occurs it's usually the result of a misconfiguration of `akka.cluster.seed-nodes`

## Unreachable Nodes

## Serialization Errors