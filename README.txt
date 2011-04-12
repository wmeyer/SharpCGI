
SharpCGI 0.2 - a FastCGI library for .NET
=========================================

Usable with C#, F# and other .NET languages.


Features:
---------
    - large number of concurrent connections possible (if supported by web server)

    - easy to use C# interface

    - for F#: request handlers can be implemented as non-blocking asynchronous workflows
              (improves resource usage and performance)

    - optionally supports request multiplexing
      (not completely tested as this is not supported by popular web servers)

    - highly configurable

    - permissive license (BSD3)


Tested with: 
------------
    - IIS 7.5

    - Apache 2.2 with mod_fastcgi [1]

    - LightTPD 1.4.28-1

    - nginx 0.8.54


Credits:
--------
    The initial design was influenced by Dan Knapp's "direct-fastcgi" Haskell package [2].
    (During further development the design diverged due to different language features.)


How to use:
-----------
    Supported tools: Visual Studio 2010, or
                     the 2010 version of the free tools for F# [3], or
                     Visual C# 2010 (Express Edition)

    Take a look at the example projects:
      C#: solution FastCGIApp.sln in directory FastCGIAppCSharp
      F#: "SimpleExampleApp(Async)" in main solution file FastCGI.sln

    Add "FastCGI.dll" as a reference to your own project.
      (The assembly is precompiled for .NET 4 Client Profile.)

    Read the documentation of your web server's FastCGI module.
      (By default the examples listen for connections from the webserver on 127.0.0.1:9000.)

    Explore the FastCGI namespace.


How to compile:
---------------
    Optionally (if you want to use IIS or other web servers that pass the socket through stdin):
        Compile the C++/CLI project "SocketSupport" with Visual C++ 2010 (Express Edition).

    Compile the FastCGI F# solution with Visual Studio 2010
      or with the 2010 version of the free tools [3].

    Not tested with Visual Studio 2008, but it should be possible
      to create a working solution by hand. As far as I'm aware, no .NET 4 features were used.

    If you want to use the library in a C# project, go to the project's property page
      and add "--standalone" to "Other flags" on the "Build" tab.


How to run the test suite:
--------------------------
    Read the source code and configure the web servers accordingly :)

    Note that FastCGITestServer.exe needs a place to write log files (see source code),
      which needs to be accessible for the IIS AppPool user.



Bugs, questions and suggestions go to:
--------------------------------------
     Wolfgang.Meyer@gmx.net


[1] Tested with mod_fastcgi-SNAP-0811090952-Win32.zip,
available at http://wiki.catalystframework.org/wiki/deployment/apache_fastcgi_win32#Install_Apache_FastCGI_module

[2] http://hackage.haskell.org/package/direct-fastcgi

[3] http://research.microsoft.com/en-us/um/cambridge/projects/fsharp/release.aspx

