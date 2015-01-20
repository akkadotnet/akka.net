Pigeon is extremely performant when it comes to sending and receiving messages.
This is the output of the Ping-Pong benchmark example (Ported from Akka) when running on an 8 core machine:
#####Message Throughput

    Worker threads: 1023
    OSVersion: Microsoft Windows NT 6.2.9200.0
    ProcessorCount: 8
    ClockSpeed: 3392 MHZ
    Actor count, Messages/sec
    2, 7073000 messages/s
    4, 11760000 messages/s
    6, 14534000 messages/s
    8, 18039000 messages/s
    10, 20161000 messages/s
    12, 18785000 messages/s
    14, 17523000 messages/s
    16, 17482000 messages/s
    18, 17931000 messages/s
    20, 18575000 messages/s
    22, 18975000 messages/s
    24, 20920000 messages/s
    ....
