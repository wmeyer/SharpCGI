
// Automatically tests the library with a number of different web servers.
// The web servers must be configured but not started.
// Assumes port 9000 for communication between web server and FCGI server.
// Uses FastCGITestServer as an external program.
//
// Don't run this on production machines!  (It starts and stops web servers!)
//
// Needs to be run as administrator (to start/stop IIS).

open System
open System.IO
open System.Net


type Webserver =
   {
      Name : string           // descriptive name
      CD : string option      // optionally: change to directory
      Start : string*string   // filename, arguments
      Stop : string*string    // filename, arguments
      StartFCGIServer: bool   // whether to start the fcgi server as an external program
      CopyTo : string option  // optionally: copy FastCGIServer.exe anf FastCGI.DLL to a directory
      Timeout: int
      MaxFailed : int
      MaxTime : int
      DontUsePostForCorrectnessTest : bool
   }

let IIS = 
    {Name="IIS 7"; CD=None; Start="net", "start W3SVC"; Stop="net", "stop W3SVC";
     CopyTo=Some @"C:\inetpub\bin\FastCGI.exe"
     StartFCGIServer=false; Timeout=5; MaxFailed=2; MaxTime=35; DontUsePostForCorrectnessTest=false}

let webservers =
   [
    {Name="Apache 2.2"; CD=Some @"C:\Program Files (x86)\Apache Software Foundation\Apache2.2\bin"; Start="httpd.exe", "-k start"; Stop="httpd.exe", "-k stop";
     CopyTo=None; StartFCGIServer=true; Timeout=5; MaxFailed=2; MaxTime=30;  DontUsePostForCorrectnessTest=false}
    {Name="LightTDP 1.4"; CD=Some @"C:\LightTPD"; Start="LightTPD.exe", "-f ./conf/lighttpd.conf"; Stop="taskkill", "/im lighttpd.exe /F";
     CopyTo=None; StartFCGIServer=true; Timeout=5; MaxFailed=2; MaxTime=25;  DontUsePostForCorrectnessTest=true}
    {Name="nginx 0.8"; CD=Some @"C:\nginx-0.8.54"; Start="nginx", ""; Stop="nginx", "-s quit";
     CopyTo=None; StartFCGIServer=true; Timeout=5; MaxFailed=2; MaxTime=25;  DontUsePostForCorrectnessTest=false}
   ]

let FCGIServer = @"..\..\..\FastCGITestServer\bin\Release\FastCGITestServer.exe"
let FCGIDLL = @"..\..\..\FastCGI\bin\Release\FastCGI.dll"

let testurl = "http://127.0.0.1:8080/test.fs"

let numConcurrentClients = 20
let numRequestsPerClient = 200


let getCookieValue (cc:seq<Cookie>) name =
   let c = cc |> Seq.find (fun c -> c.Name = name )
   c.Value

let correctnessTest (s:Webserver) =
   try
      let mutable contentsText = "content from client"
      let request = System.Net.WebRequest.Create(testurl);
      let headerText = "header from client"
      request.Headers.Add(HttpRequestHeader.From, headerText)
      let cc = new CookieContainer()
      let keksText = "clientcookie"
      cc.Add(Uri(testurl), Cookie("keks", keksText) )
      let cookieHeader = cc.GetCookieHeader(Uri(testurl))
      request.Headers.Add(HttpRequestHeader.Cookie, cookieHeader )
      if s.DontUsePostForCorrectnessTest then
         request.Method <- "GET"
         contentsText <- ""
      else
         let contents = Text.Encoding.UTF8.GetBytes contentsText
         request.Method <- "POST"
         request.ContentLength <- (int64) contents.Length
         let str = request.GetRequestStream()
         str.Write(contents, 0, contents.Length)
         str.Close()
      
      use response  = request.GetResponse()
      use reader = new IO.StreamReader(response.GetResponseStream())
      let responseData = reader.ReadToEnd()
      let ch = response.Headers.[HttpResponseHeader.SetCookie]
      let rcc = CookieContainer()
      rcc.SetCookies(Uri(testurl), ch)
      let rcc = Seq.cast<Cookie> (rcc.GetCookies( Uri(testurl) ))
      let repliedContents = getCookieValue rcc "contents"
      let repliedHeader = getCookieValue rcc "ReceivedHeader"
      let repliedKeks = getCookieValue rcc "ReceivedCookie"
      let responseCookie = getCookieValue rcc "ResponseCookie"
      let responseHeader = response.Headers.Item(HttpResponseHeader.ContentLanguage)

      repliedContents = "\"" + contentsText + "\""
      && repliedHeader = "\"" + headerText + "\""
      && repliedKeks =  "\"" + keksText + "\""
      && responseData = "reply from server"
      && responseCookie = "\"" + "cookie from server" + "\""
      && responseHeader = "klingon"
   with | :? WebException as ex -> printfn "Exception : %s" (ex.ToString())
                                   false


let performanceTest timeout =
   let failures = ref 0
   let before = DateTime.Now

   let client i =
     async {
       let w = new WebClient()
       do! Async.Sleep (i * 100) // delayed starting to avoid timeouts during warming up of the web servers
       for i in 1..numRequestsPerClient do
         let completed = ref false
         let cts = new Threading.CancellationTokenSource()
         let timer = new Timers.Timer( double(timeout*1000), AutoReset=false )
         timer.Elapsed.Add( fun _ -> if not !completed then try printf "t"; incr failures; cts.Cancel()  with |_->())
         timer.Start()
         let r = try
                  Some (Async.RunSynchronously( w.AsyncDownloadString( Uri(testurl)), cancellationToken=cts.Token))
                  with | ex -> printf "E"; incr failures; None
         completed := true
         if i % 100 = 0 then printf "."
     }

   List.init numConcurrentClients client
   |> Async.Parallel
   |> Async.RunSynchronously
   |> ignore
   (DateTime.Now - before), !failures


let startFCGIServer connectionType handlerType =
   Directory.SetCurrentDirectory AppDomain.CurrentDomain.BaseDirectory
   let si = Diagnostics.ProcessStartInfo()
   si.FileName <- FCGIServer
   si.Arguments <- "CreateSocket " + connectionType + " " + handlerType
   si.CreateNoWindow <- true
   let p = Diagnostics.Process.Start si
   Console.CancelKeyPress.Add (fun _ -> try p.Kill() with | _ -> ())
   Threading.Thread.Sleep 500


let stopFCGIServer() =
   let p = Diagnostics.Process.Start("taskkill", "/im " + Path.GetFileName FCGIServer + " /F")
   p.WaitForExit()


let testConfig (s:Webserver) (loop, handler) =
   printfn "\nconfiguration: connection %s, handler %s" loop handler

   if s.StartFCGIServer then
      printfn "starting fcgi server"
      startFCGIServer loop handler
      Threading.Thread.Sleep 500
   try
      if not (correctnessTest s) then
         printfn "correctness test FAILED"
         false
      else
         printfn "correctness test succeeded"
         printfn "starting performance test"
         let time, failures = performanceTest s.Timeout
         printfn "\nTime: %A, number of failures: %d" time failures
         let success = 
            if failures > s.MaxFailed then
               printfn "FAILED: too many failures. Expected at most: %d, measured: %d" s.MaxFailed failures
               false
            elif time.TotalSeconds > float(s.MaxTime) then
               printfn "FAILED: took too much time. Expected: %A, measured: %A" s.MaxTime time
               false
            else
               true
         success
   finally
      stopFCGIServer()


let testOneServer (s:Webserver) =
   [
    "simple", "blocking"
    "simple", "async"
    "multiplexing", "blocking"
   ]
   |> List.map (testConfig s)
   |> List.forall id


let startWebserver (s:Webserver) =
   match s.CopyTo with
   | Some dest ->
      Directory.SetCurrentDirectory AppDomain.CurrentDomain.BaseDirectory
      File.Copy(FCGIServer, dest, true)
      File.Copy(FCGIDLL, Path.Combine(Path.GetDirectoryName dest, Path.GetFileName FCGIDLL), true)
   | None -> ()
   let si = Diagnostics.ProcessStartInfo(fst s.Start, snd s.Start)
   si.CreateNoWindow <- false
   match s.CD with
   | Some cd -> si.WorkingDirectory <- cd
   | None -> ()
   let p = Diagnostics.Process.Start si
   Threading.Thread.Sleep 3000


let stopWebserver (s:Webserver) =
   let si = Diagnostics.ProcessStartInfo(fst s.Stop, snd s.Stop)
   si.CreateNoWindow <- true
   si.Arguments <- snd s.Stop
   match s.CD with
   | Some cd -> si.WorkingDirectory <- cd
   | None -> ()
   let p = Diagnostics.Process.Start si
   p.WaitForExit()


// testing IIS (only one config because we cannot pass arguments; the FastCGIServer.exe is started by IIS)
printfn "Testing webserver %s" IIS.Name
Console.CancelKeyPress.Add (fun _ -> stopWebserver IIS)
startWebserver IIS
try
   if not (correctnessTest IIS) then
      printfn "correctness test FAILED"
   else
      printfn "correctness test succeeded" 
      if not (testConfig IIS ("simple", "async")) then printfn "%s test FAILED" IIS.Name
      else printfn "%s test succeeded." IIS.Name
finally
   stopWebserver IIS

// testing the other servers
for server in webservers do
   printfn "\n\n\nTesting webserver %s" server.Name
   Console.CancelKeyPress.Add (fun _ -> stopWebserver server)
   startWebserver server
   try
      if not (testOneServer server) then printfn "%s test FAILED." server.Name
      else printfn "%s test succeeded." server.Name
   finally
      stopWebserver server
