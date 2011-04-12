
// FCGI definitions, as in the original C headers

module ProtocolConstants


let FCGI_HEADER_LEN = 8
let FCGI_VERSION = 1uy

// flags
let FCGI_KEEP_CONN = 01uy

[<Literal>]
let FCGI_MAX_CONNS  = "FCGI_MAX_CONNS"
[<Literal>]
let FCGI_MAX_REQS   = "FCGI_MAX_REQS"
[<Literal>]
let FCGI_MPXS_CONNS = "FCGI_MPXS_CONNS"