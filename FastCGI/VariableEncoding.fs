
// Decode and encode name-value pairs as they occur in FastCGI records.

module VariableEncoding

open System


let (|ElemLength|_|) (arr:byte array, index) =
   if arr.Length >= index+1 && arr.[index] &&&  0x80uy = 0x00uy then Some (int arr.[index], index+1)
   elif arr.Length >= index+4 then
      let x1,x2,x3,x4 = arr.[index], arr.[index+1], arr.[index+2], arr.[index+3]
      let decodedLen= (int(x1 &&& 0x7fuy) <<< 24) +
                      (int(x2) <<< 16) +
                      (int(x3) <<< 8) +
                      int(x4)
      Some (decodedLen, index+4)
   else None

// optimized for performance (used a lot)
let decodeVariables (encoding:Text.Encoding) (arr:byte[]) =
   let rec loop i acc = 
      match arr, i with
      | ElemLength (nameLen, i2) ->
         match arr, i2 with
         | ElemLength (valueLen, i3) ->
            let name = String(encoding.GetChars(arr, i3, nameLen))
            let value = String(encoding.GetChars(arr, i3+nameLen, valueLen))
            loop (i3+nameLen+valueLen) ((name,value)::acc)
         | _ -> acc // order of the vars does not matter
      | _ -> acc
   loop 0 []


// rarely or never used; optimization not necessary
let encodeVariables (encoding:Text.Encoding) (parameters: seq<string*string>) =
   [|
      for name, value in parameters do
         yield byte name.Length
         yield! encoding.GetBytes name
         yield byte value.Length
         yield! encoding.GetBytes value
   |]
