```SimplePubSub.cs```
======
a simple pub-sub for intra-application (inside the app) communication.  


Features
-------------
1. Thread safe: supports any number of publishers and subscribers
2. No allocations:  sending/receiving messages doesn't allocate (no GC pressure). 
3. Pull, not push:  Incoming Messages are enqueued for reading during the subscriber's update logic. (improved job workflow)
4. No marshaling:  stays in managed code land.


Q&A
------
1. **Why force subscribing with queues?  why not allow callbacks or just use ```events```?**

	When I started developing this, I actually started off with subscribers registering callbacks, because this was meant to be an optimized version of events. However I ran into a bunch of "gotchas" when considering asynchronous publishers/subscribers.

	The biggest reason I didn't subscribe with custom delegates is it slows down the publisher, especially if one of the subscribers has a slow callback.  
	
	An example use might be that a lof of these published messages are going to be from the rendering or physics threads, so I want to keep those fast and make the "gameplay" threads do most the work by forcing the subscriber to loop the pending work.

	Regarding events, they are not threadsafe. Subscribed callbacks also run into this risk if you arn't careful.


	
Example use
-------------
Best to look at the code posted in the "check it out", but:

```csharp
//Publisher:
var channel = pubSub.GetChannel<int>("myKey"); //create/get a channel for sending/receiving messages
channel.Publish(1);

//Subscriber:
var channel = pubSub.GetChannel<int>("myKey");  //create/get a channel for sending/receiving messages
var queue = new ConcurrentQueue<int>(); //create a thread safe queue that messages will be put in.
channel.Subscribe(queue); //now whenever a publish occurs, the queue will get the message

while(queue.TryDequeue(out var message)){ Console.Writeline(message); }  //do work with the message
```

My own "self critique"
-----
The subscriber needs to discover the PubSub Channel's ```key``` and ```message``` type.  
I don't see an easy solution for this, so in my demo the subscriber reads static values from the publisher's class.   
This works but is verbose.   Probably the workaround is to have a central registry (class) with all standard Channel keys+message types, but it's still pretty not ideal.  
Ideally, you could subscribe to a channel and consume it's messages without searching for either the key or message type.


