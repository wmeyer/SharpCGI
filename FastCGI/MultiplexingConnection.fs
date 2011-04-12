
module MultiplexingConnection

open System
open System.Collections.Generic

open Connection
open Record
open FastCGI
open BasicConnection
open ProtocolConstants
open AsyncSocket

/// Handles the records of one request.
type RequestAgent(requestID, handler, conn:MultiplexingConnection, keepConnection) as this =
   let options : Options = conn.Options
   let log = options.TraceLogger
   let logE = options.ErrorLogger
   let id = string( System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode this)

   /// This buffer is built by and by when additional ParamRecord's arrive
   /// After the last ParamsRecord has been processed, it is null.
   let mutable paramBuffer = Array.empty<byte>

   let mutable finishing = false // about to get completed, does not expect new input

   // signalled exactly once: when the agent leaves its loop
   // (do not Dispose this automatically, it should be able to outlive the agent)
   let finished = new Threading.ManualResetEvent(false)

   let agent = MailboxProcessor<Record>.Start(fun inbox ->
      let rec loop() = async {
         let! record = inbox.Receive()
         match record.Type with
         | RecordType.ParamsRecord -> 
            if record.Length > 0 then
               log "non empty params record"
               assert( paramBuffer <> null )
               paramBuffer <- Array.append paramBuffer record.Content
               return! loop() // expecting more records for this request
            else
               // no more params -> we start executing the request, although we still expect StdinRecord's
               // receiving these additional records from 'inbox' is handled by the Request object
               log "empty params record"
               let response = new Response(conn, requestID)
               let request = new Request(requestID, paramBuffer, conn, response)
               paramBuffer <- null // discard collected raw data
               try
                  log "calling handler"
                  do! handler request response
                  log "call to handler finished"
               with
               | ex when options.CatchHandlerExceptions ->
                  conn.LogError <| sprintf "RequestAgent, Uncaught exception in handler %s" (ex.ToString())

               do! response.AsyncSendHeaders()
               this.RequestEnding <- true
               if not response.Closed then
                  do! response.AsyncCloseOutput()
               if not keepConnection then conn.Close()
               finished.Set() |> ignore
               // no further recursion: this agent is finished
                                    
         | x -> conn.LogError <| sprintf "Ignoring record of unexpected type %A" x
      }
      loop()
   )

   /// Send a record to the agent to be processed
   member x.PostRecord = agent.Post

   member x.Mailbox = agent

   member x.RequestEnding
      with get() = lock x (fun () -> finishing)
      and set(v) = lock x (fun () -> finishing <- v)

   member x.Finished = finished :> Threading.WaitHandle


/// Implements request multiplexing (untested; did not find a web server
/// that really used it)
/// Uses a new mailbox processor as a worker for every request.
and MultiplexingConnection(socket:AsyncSocket, options:Options) as this =

   let conn = new BasicConnection(socket, options)

   let log = conn.Log
   let logError = conn.LogError

   let mutable keepConnection = false

   // maps request ids to RequestAgents
   // (usually there is only one active agent at a particular time,
   // but the design and the FCGI spec allow an arbitrary number)
   let requestAgents = new Dictionary<int, RequestAgent>()

   let removeDeadAgents() =
      let deadAgents = [for KeyValue(id, reqAgent) in requestAgents do
                           if reqAgent.RequestEnding then yield id]
      for id in deadAgents do
         requestAgents.Remove id |> ignore

   
   interface Connection with
      member x.ReceiveRecord requestID = 
         (x :> Connection).AsyncReceiveRecord requestID |> Async.RunSynchronously

      member x.AsyncReceiveRecord requestID =
         async {
            match requestAgents.TryGetValue requestID with
            | (false, _) -> return None
            | (true, agent) ->
               let! r = agent.Mailbox.Receive() // TryReceive seems to wait til timeout unnecesarily sometimes
               return Some r
         }

      member x.EndRequest reqID = 
         endRequestRecord reqID
         |> conn.SendRecord

      member x.AsyncEndRequest reqID = 
         endRequestRecord reqID
         |> conn.AsyncSendRecord

      member x.SendBuffer requestID buffer =
         conn.SendBuffer requestID buffer 0 buffer.Length

      member x.AsyncSendBuffer requestID buffer =
         conn.AsyncSendBuffer requestID buffer 0 buffer.Length

      member x.Log str = conn.Log str
      member x.LogError str = conn.LogError str
      member x.Options = conn.Options


   interface IDisposable with
      member x.Dispose() =
         (conn :> IDisposable).Dispose()

   // mini interface for RequestAgent:
   member x.LogError = logError
   member x.Options = conn.Options
   member x.Close() = conn.Close()

   static member Loop socket options handler =
         async {
            use conn = new MultiplexingConnection(socket, options)
            do! conn.RequestLoop handler
         }

   member private x.Finishing =
      not keepConnection
      && requestAgents.Values |> Seq.forall (fun agent -> agent.RequestEnding)

   /// Reads records from the socket, manages request agents and delegates to them
   member private x.RequestLoop handler =
      let rec loop() =
         async {
            try
               removeDeadAgents()

               let! received = conn.RecvRecord()
               match received with

               | Record record -> 
                  match record.Type with

                  | RecordType.BeginRequestRecord ->
                      let flags = record.Content.[2]
                      keepConnection <- flags &&& FCGI_KEEP_CONN = FCGI_KEEP_CONN
                      log <| sprintf "Connection, Loop: FCGI_KEEP_CONN: %b" keepConnection
                      requestAgents.[ record.RequestID ] <-
                         new RequestAgent(record.RequestID, handler, this, keepConnection)
                      return! loop()

                  | RecordType.OtherRecord ->
                      do! unknownTypeRecord record.TypeInt  
                          |> conn.AsyncSendRecord
                      return! loop()

                  | RecordType.GetValuesRecord ->
                      do! conn.CreateGetValuesResult record
                          |> conn.AsyncSendRecord
                      return! loop()

                  | _ -> // propagate all others to request agent
                      match requestAgents.TryGetValue record.RequestID with
                      | (true, agent) -> log "Posting record"; agent.PostRecord record
                      | (false, _) -> logError <| sprintf "Ignoring record for unknown request ID %d" record.RequestID
                      return! loop()

               | NoData ->
                  if conn.Closed || x.Finishing then
                     log "Loop of closed connection ending."
                     // wait for all agents to finished
                     // (before the async workflow stops and the connection and the socket get disposed)
                     let waitHandles =
                        requestAgents.Values
                        |> Seq.map (fun agent -> agent.Finished)
                        |> Array.ofSeq
                     if waitHandles.Length > 0 then
                        Threading.WaitHandle.WaitAll waitHandles |> ignore
                  else
                     logError "MultiplexingConnection, Loop: unexpectedly received no data. Cancelling connection."
                     // don't wait for agents, they might not finish as the connection seem corrupt
                     conn.Close()

               | UnknownVersion ->
                  logError "MultiplexingConnection, Loop: unknown FCGI version"
                  conn.Close()

             with
               | ex -> logError <| sprintf "MultiplexingConnection cancelled with exception: %s" (ex.ToString())
                       conn.Close()
      }
      loop()


