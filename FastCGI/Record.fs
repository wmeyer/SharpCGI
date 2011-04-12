
// FastCGI records

module Record

open ProtocolConstants

type RecordType =
| BeginRequestRecord = 1
| AbortRequestRecord = 2
| EndRequestRecord = 3
| ParamsRecord = 4
| StdinRecord = 5
| StdoutRecord = 6
| StderrRecord = 7
| DataRecord = 8
| GetValuesRecord = 9
| GetValuesResultRecord = 10
| UnknownTypeRecord = 11
| OtherRecord = 12


/// Represents a package of data received from or sent to the web server.
/// A record usually belongs to a request (except for management records).
[<AbstractClass>]
type Record(recordType, requestID) =
  let intToBytes i = byte(i % 256), byte( (i / 256) % 256 )

  member x.Type = recordType
  member x.RequestID = requestID

  abstract TypeInt : int

   // An array, an offset and a size
  abstract Length : int

  // the preferred way to get a record's content
  // (returns an array, an offset and a size)
  abstract GetContent : unit-> byte[] * int * int
  
  // Might be inefficient for some types of records.
  abstract Content :  byte[]

  member x.GetHeader() =
     let requestIDB0, requestIDB1 = intToBytes x.RequestID
     let contentLenB0, contentLenB1 = intToBytes x.Length
     let headerData = [| FCGI_VERSION
                         byte(x.TypeInt)
                         requestIDB1; requestIDB0
                         contentLenB1; contentLenB0
                         0uy; 0uy |]
     headerData

     
/// A record that owns its own content array and that can be created from a byte stream.
type ContainedRecord(recordType:RecordType, requestID, content:byte array, ?typeInt) =
  inherit Record(recordType, requestID)

  static let intFromBytes b1 b0 = int(b1) * 256 + int(b0)

  override x.GetContent() = content, 0, content.Length
  override x.Content = content
  override x.Length = content.Length
  override x.TypeInt = defaultArg typeInt (int recordType)

  static member CreateFromHeader (msg:byte array) contentGetter =
     async {
        assert( msg.Length = 8 )
        // msg.[0]: version
        let typeCode = int( msg.[1] )
        let requestID = intFromBytes msg.[2] msg.[3]
        let contentLength = intFromBytes msg.[4] msg.[5]
        let paddingLength = int(msg.[6])
        // msg.[7]: reserved
        let! maybeContent = contentGetter contentLength paddingLength
        match maybeContent with
        | None -> return None
        | Some content ->
           let recordType = if typeCode < int(RecordType.OtherRecord) then enum<RecordType>(typeCode) else RecordType.OtherRecord
           return Some (new ContainedRecord(enum<RecordType>(typeCode), requestID, content, typeCode))
     }


/// A record with content that is a part of a larger array, shared by multiple records.
/// (A performance optimization)
type VirtualRecord(recordType:RecordType, requestID, content:byte[], contentOffset, contentSize) =
   inherit Record(recordType, requestID)

   override x.GetContent() = content, contentOffset, contentSize

   override x.Content =
      assert(false) // should not be used
      let arr = Array.create contentSize 0uy
      Array.blit content contentOffset arr 0 contentSize
      arr

   override x.Length = contentSize    
   override x.TypeInt = int recordType


// Some specialized record constructors:

let endRequestRecord id =
   new ContainedRecord(RecordType.EndRequestRecord, id, [|0uy;0uy;0uy;0uy;0uy;0uy;0uy;0uy;|])

let unknownTypeRecord recordType =
   new ContainedRecord(RecordType.UnknownTypeRecord, 0, [|byte(recordType);0uy;0uy;0uy;0uy;0uy;0uy;0uy|])   
