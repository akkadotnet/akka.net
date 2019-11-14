---
uid: network-security
title: Network Security
---

# Akka.Remote Security

There are 2 ways you may like to achieve network security when using Akka.Remote:

- Transport Layer Security (introduced with Akka.Remote Version 1.2)
- Virtual Private Networks

### Akka.Remote with TLS (Transport Layer Security)
The release of Akka.NET version 1.2.0 introduces the default [DotNetty](https://github.com/Azure/DotNetty) transport and the ability to configure [TLS](http://en.wikipedia.org/wiki/Transport_Layer_Security) security across Akka.Remote Actor Systems.  In order to use TLS, you must first install a valid SSL certificate on all Akka.Remote hosts that you intend to use TLS.

Once you've installed valid SSL certificates, TLS is enabled via your HOCON configuration by setting `enable-ssl = true` and configuring the `ssl` HOCON configuration section like below:

```
akka {
  loglevel = DEBUG
  actor {
    provider = remote
  }
  remote {
    dot-netty.tcp {
      port = 0
      hostname = 127.0.0.1
      enable-ssl = true
      log-transport = true
      ssl {
        suppress-validation = true
        certificate {
          # valid ssl certificate must be installed on both hosts
          path = "<valid certificate path>" 
          password = "<certificate password>"
          # flags is optional: defaults to "default-flag-set" key storage flag
          # other available storage flags:
          #   exportable | machine-key-set | persist-key-set | user-key-set | user-protected
          flags = [ "default-flag-set" ] 
        }
      }
    }
  }
}
```

### Akka.Remote with Virtual Private Networks
The absolute best practice for securing remote Akka.NET applications today is to make the network around the applications secure - don't use public, open networks! Instead, use a private network to restrict machines that can contact Akka.Remote processes to ones who have your VPN credentials.

Some options for doing this:

* [OpenVPN](https://openvpn.net/) - for "do it yourself" environments;
* [Azure Virtual Networks](http://azure.microsoft.com/en-us/services/virtual-network/) - for Windows Azure customers; and
* [Amazon Virtual Private Cloud (VPC)](http://aws.amazon.com/vpc/) - for Amazon Web Services customers.
