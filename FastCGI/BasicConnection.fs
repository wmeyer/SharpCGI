module BasicConnection

open System
open System.Net.Sockets

open Record
open FastCGI
open AsyncSocket
open VariableEncoding
open ProtocolConstants


type RecvRecordResult =
 | NoData // socket closed or no data after timeout
 | UnknownVersion // received a record of unknown version
 | Record of Record


// Common implementation parts of different connection types.
// Owns the socket and  manages receiving and sending records.
type BasicConnection(socket:AsyncSocket, options:Options) as this =
   let id = string( System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode this)
   
   // did we close our socket, e.g. the connection is terminated?
   let mutable closed = false

   let recvAll = socket.AsyncRecvAll

   member x.AsyncSendBuffer reqID arr offset len = 
      async {
         if len > 0 then
            let lenThisRecord = min len 0xffff
            do! new VirtualRecord(RecordType.StdoutRecord, reqID, arr, offset, lenThisRecord)
                  |> this.AsyncSendRecord
            if len > lenThisRecord then
               do! x.AsyncSendBuffer reqID arr (offset+lenThisRecord) (len-lenThisRecord)
      }

   member x.AsyncSendRecord (r:Record) =
      async {
         do! socket.AsyncSendAll (r.GetHeader())
         let arr, offset, size = r.GetContent()
         do! socket.AsyncSendAll(arr, offset, size)
      }

   member x.SendBuffer reqID arr offset len =
      if len > 0 then
         let lenThisRecord = min len 0xffff
         new VirtualRecord(RecordType.StdoutRecord, reqID, arr, offset, lenThisRecord)
         |> this.SendRecord
         if len > lenThisRecord then
            x.SendBuffer reqID arr (offset+lenThisRecord) (len-lenThisRecord)

   // TODO: test with a web server that actually queries this
   member x.CreateGetValuesResult (r:Record) =
      let resultVars = seq {
         for (name, value) in decodeVariables options.VariableEncoding r.Content do
            match name with
            | FCGI_MAX_CONNS -> yield name, options.FCGI_MAX_CONNS
            | FCGI_MAX_REQS -> yield name, options.FCGI_MAX_REQS
            | FCGI_MPXS_CONNS -> yield name,options.FCGI_MPXS_CONNS
            | _ -> ()
      }
      ContainedRecord(RecordType.GetValuesResultRecord, 0, encodeVariables options.VariableEncoding resultVars)

   // dispose the socket if the connection is disposed
   interface IDisposable with
      member x.Dispose() =
         try
            socket.Dispose()
         with | _ -> ()


   // PUBLIC

   member x.LogError str = options.ErrorLogger (str + " : " + id)

   member x.Log str = options.TraceLogger (str + " : " + id)

   // not used (seems to get ignored by most web servers or directly propagated to the user agent)
   member x.LogErrorToServer (s:string) requestID =
      let utf8 = Text.UTF8Encoding()
      if s.Length > 0 then
         ContainedRecord(RecordType.StderrRecord, requestID, utf8.GetBytes s)
         |> x.SendRecord

   /// access to global config options
   member x.Options = options

   /// Has this connection been closed actively? (not by the peer)
   member x.Closed = lock x (fun () -> closed)

   /// Close this connection.
   /// Used if web server sets FCGI_KEEP_CONN to false,
   ///   i.e. connections should be closed after every request,
   /// or if a fatal error occured.
   member x.Close() =
     lock x (fun () ->
        if not closed then
         closed <- true
         try
            x.Log "Connection close";
            socket.Shutdown(SocketShutdown.Send);
            socket.Close();
            x.Log "Connection CLOSED"
            socket.Dispose()
         with
            | _ -> ()
   )

   /// Send a record to the web server (blocking)
   member x.SendRecord (r:Record) = 
      x.Log <| sprintf "SendRecord: %A" r.Type
      socket.Send (r.GetHeader()) |> ignore
      let buffer, offset, size = r.GetContent()
      socket.Send(buffer, offset, size, SocketFlags.None) |> ignore
      x.Log "SendRecord done"

   /// asynchronously reads a record from the socket
   member x.RecvRecord() = 
      async {
         let! msg = recvAll FCGI_HEADER_LEN
         if msg.Length <> FCGI_HEADER_LEN then return NoData
         else
            let version = msg.[0]
            if version <> 1uy then
               x.LogError <| sprintf "Record header of unrecognized version: %d" version
               return UnknownVersion
            else
               let! maybeRecord = ContainedRecord.CreateFromHeader msg
                                    (fun contentLength paddingLength ->
                                       async {
                                          let! recordContent = recvAll contentLength
                                          let! padding = recvAll paddingLength
                                          if recordContent.Length = contentLength && padding.Length = paddingLength then
                                             return Some recordContent
                                          else
                                             return None
                             })
               match maybeRecord with
               | None -> return NoData
               | Some r -> return (Record r)
      }

   member x.Test() =
      let wasBlocking = socket.Blocking
      try
         socket.Blocking <- false
         socket.Send(Array.empty<byte>) |> ignore
         socket.Blocking <- wasBlocking
      with | _ -> ()
      socket.Connected
