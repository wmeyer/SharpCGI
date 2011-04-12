
// Cookie parsing and printing
// (This  module could maybe profit from optimizing, when working with a lot of cookies...)

module Cookies

open System
open System.Net
open System.Globalization

module private Internal =
   let charListToString xs = String( Array.ofSeq xs )

   let tspecials = set ['(';')';'<';'>';'@';',';';';':';'\\';'\"';'/';
                        '[';']';'?';'=';'{';'}';' ';'\009']

   let tokenChar x = not (Char.IsControl x) && not (Set.contains x tspecials)

   // Some patterns that return (result, rest of input):

   let (|Token|_|) (xs:char list) =
      match xs with
      | x::_ when tokenChar x -> Some (List.takeDropWhile tokenChar xs)
      | _ -> None

   let (|QuotedString|_|) (xs:char list) =
      match xs with
      | '"'::t -> let str, rest = List.takeDropWhile ((<>) '"') t
                  match rest with
                  | '"'::remainder -> Some (str, remainder)
                  | _ -> None
      | _ -> None

   let (|Word|_|) = function
      | Token (w,r) | QuotedString(w,r) -> Some (w,r)
      | _ -> None

   let (|NameValuePair|_|) = function
      | Token (n, r) ->
         match r with
         | '='::r' -> 
            match r' with
            | Word(v,r'') -> Some (n |> charListToString, v |> charListToString, r'')
            | _ -> None
         | _ -> None
      | _ -> None

   exception NoCookie

   let parseNameValuePairs (s:string) =
      let rec loop = function
         | NameValuePair (n,v,r) ->
            match r with
            | ';'::' '::r | ';'::r | ','::' '::r | ','::r -> (n,v)::(loop r)
            | [] -> [n,v]
            | _ -> raise NoCookie
         | [] -> []
         | _ -> raise NoCookie
      try
         s |> List.ofSeq |> loop |> Some
      with
         | NoCookie -> None

   let cookieTimeFormat (dt:DateTime) =
      dt.ToUniversalTime().ToString("ddd, dd-MMM-yy hh:mm:ss \\G\\M\\T",
                                    DateTimeFormatInfo.InvariantInfo)

   let cookieNameValuePairs (c:Cookie) = seq {
      yield c.Name, Some c.Value
      if c.Comment <> "" then yield "Comment", Some c.Comment
      if c.Domain <> "" then yield "Domain", Some c.Domain
      if c.Expires <> DateTime() then yield "Expires", Some (cookieTimeFormat c.Expires)
      if c.Path <> "" then yield "Path", Some c.Path
      if c.Secure then yield "Secure", None
      yield "Version", Some (string c.Version)
   }

   let printCookie (cookie:Cookie) =
      String.Join(";", cookieNameValuePairs cookie
                       |> Seq.map (function | ("Version" as n, Some v) -> n + "=" + v
                                            | (n, Some v) -> n + "=\"" + v + "\""
                                            | (n, None) -> n))


open Internal

// s: Cookie header without the "Cookie:" prefix
let parseCookies (s:string) =
   match parseNameValuePairs s with
   | None -> seq []
   | Some ps ->
      seq {
         let version = ref 0 // important: init of mutable state INSIDE seq expr
         let currentCookie : Cookie ref = ref null
         for (name,value) in ps do
            match name with
            | "$Version" -> match Int32.TryParse value with
                            | (true, v) -> version := v
                            | (false, _) -> ()
            | "$Path" -> (!currentCookie).Path <- value
            | "$Domain" -> (!currentCookie).Domain <- value
            | _ -> 
               if !currentCookie <> null then
                  yield !currentCookie;
               currentCookie := new Cookie(name, value)
               (!currentCookie).Version <- !version
         if !currentCookie <> null then
            yield !currentCookie
      }

let printCookies cookies =
   String.Join(",", Seq.map printCookie cookies)
