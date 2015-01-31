---
layout: wiki
title: Synchronizing workflows
---
# Synchronizing workflows

```csharp

//add whatever contextual information necessary for
//jobs in these classes
public class SubTaskStarted {}
public class SubTaskEnded {}
public class TasksDone {} 
public class TasksFailed {} 

public class JobSynchronizer : ReceiveActor
{
     private readonly ActorRef _notifyStatus ;

     //TODO: don't use a counter incase of lost messages.
     //use a dict of actorref , bool for completion status per actor instead
     private int _jobCount; 
    
     public JobSynchronizer(ActorRef notifyStatus)
     {
          SetReceiveTimeout(TimeSpan.FromSeconds(5));
          _notifyStatus = notifyStatus ;
          Receive<SubTaskStarted>(_ => 
          {
             _jobCount ++;
             CheckAllDone();
          });
          Receive<SubTaskEnded>(_ =>
          {
             _jobCount --;
             CheckAllDone();
          });
          Receive<ReceiveTimeout>(_ =>
          {
              //kill timeout
              SetReceiveTimeout(null);
              //
              _notifyStatus.Tell(new TasksFailed());
          });
     }

     void CheckAllDone()
     {
         if(_jobCount == 0)
         {
             _notifyStatus.Tell(new TasksDone());
         }
     }
}

public class Worker : ReceiveActor
{
     private ActorRef _synchronizer;
     public Worker(ActorRef synchronizer)
     {
           _synchronizer = synchronizer;
           Receive<DoSomeWork>(work => 
           {
                //notify the synchronizer that we are starting to do some work
                _synchronizer.Tell(new SubTaskStarted());
                //do work
                var directories = GetDirectories(work.Directory);
                foreach(var dir in directories)
                {
                    //tell self do do more work
                    //(or fork out to Children to do more work)
                    self.Tell(new Work(dir));                     
                }

                //do more work, e.g. process files

                //once we are done with this task, notify the synchronizer
                _synchronizer.Tell(new SubTaskEnded());
           })
     }
}
```