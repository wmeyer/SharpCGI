module Connection

open Record
open FastCGI

/// A connection as seen by Requests and Responses.
type Connection =
   // To receive Stdin records in Request.
   abstract ReceiveRecord : int -> Record option
   abstract AsyncReceiveRecord : int -> Async<Record option>

   // Send response content (as Stdout records)
   abstract SendBuffer : int -> byte[] -> unit
   abstract AsyncSendBuffer : int -> byte[] -> Async<unit>

   // To tell the web server that a request was ended.
   abstract EndRequest : int -> unit
   abstract AsyncEndRequest : int -> Async<unit>

   abstract Options : Options
   abstract Log : string -> unit
   abstract LogError : string -> unit
