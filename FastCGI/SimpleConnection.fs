module SimpleConnection

open System

open Connection
open Record
open FastCGI
open BasicConnection
open ProtocolConstants
open AsyncSocket

// Does not support request multi-plexing.
// Only one thread per connection.
// Should be preferred unless you are really working with a multiplexing fastcgi implementation.
type SimpleConnection(socket:AsyncSocket, options:Options) =

   let conn = new BasicConnection(socket, options)

   let log = conn.Log
   let logError = conn.LogError

   let mutable keepConnection = false
   let mutable currentRequestID = 0
   let mutable paramBuffer = Array.empty<byte>


   interface Connection with
      member x.ReceiveRecord requestID = 
         (x :> Connection).AsyncReceiveRecord requestID |> Async.RunSynchronously

      member x.AsyncReceiveRecord requestID =
         async {
            let! r = conn.RecvRecord()
            match r with
            | Record record -> return Some record
            | _ -> return None
         }

      member x.EndRequest reqID =
         endRequestRecord reqID
         |> conn.SendRecord
         assert( reqID = currentRequestID )
         currentRequestID <- 0

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


   static member Loop socket options handler =
      async {
         use conn = new SimpleConnection(socket, options)
         do! conn.RequestLoop handler
      }

   member private x.RequestLoop handleRequest =
      let rec loop() =
         async {
            try
               let! received = conn.RecvRecord()
               match received with

               | Record record -> 
                  match record.Type with

                  | RecordType.BeginRequestRecord ->
                      currentRequestID <- record.RequestID
                      paramBuffer <- Array.empty
                      log <| sprintf "SimpleConnection, Loop: request id: %d" currentRequestID
                      let flags = record.Content.[2]
                      keepConnection <- flags &&& FCGI_KEEP_CONN = FCGI_KEEP_CONN
                      log <| sprintf "SimpleConnection, Loop: FCGI_KEEP_CONN: %b" keepConnection
                      return! loop()

                  | RecordType.ParamsRecord when record.Length > 0 -> 
                     log "non empty params record"
                     assert( paramBuffer <> null )
                     paramBuffer <- Array.append paramBuffer record.Content
                     return! loop() // expecting more records for this request

                  | RecordType.ParamsRecord when record.Length = 0 -> 
                     log "empty params record"
                     let response = new Response(x, currentRequestID)
                     let request = new Request(currentRequestID, paramBuffer, x, response)

                     try
                        log "calling handler"
                        do! handleRequest request response
                        log "call to handler finished"
                        do! response.AsyncSendHeaders()
                        if not response.Closed then
                          do! response.AsyncCloseOutput()
                     with
                     | ex when conn.Options.CatchHandlerExceptions ->
                        logError <| sprintf "Uncaught exception in handler %s" (ex.ToString())
                     if not keepConnection then
                        conn.Close()
                     else
                        return! loop()

                  | RecordType.StdinRecord -> 
                     log "ignored stdin record"
                     return! loop()

                  | RecordType.OtherRecord ->
                      unknownTypeRecord record.TypeInt  
                      |> conn.SendRecord
                      // reply with "unknown type"
                      do! ContainedRecord(RecordType.UnknownTypeRecord, 0, [|byte(record.TypeInt);0uy;0uy;0uy;0uy;0uy;0uy;0uy|])   
                          |> conn.AsyncSendRecord
                      return! loop()

                  | RecordType.GetValuesRecord ->
                      do! conn.CreateGetValuesResult record
                          |> conn.AsyncSendRecord
                      return! loop()

                  | recordType -> 
                     logError <| sprintf "SimpleConnection: unexpected record type %A" recordType
                     return! loop()

               | NoData ->
                  if conn.Closed then
                     log "Loop of closed connection ending."
                  else
                     logError "SimpleConnection, Loop: unexpectedly received no data. Cancelling connection."
                     conn.Close()

               | UnknownVersion ->
                  logError "SimpleConnection, Loop: unknown FCGI version"
                  conn.Close()
             with
               | ex -> logError <| sprintf "SimpleConnection cancelled with exception: %s" (ex.ToString())
                       conn.Close()
         }
      loop()

