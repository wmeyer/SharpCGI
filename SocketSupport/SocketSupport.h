// RawSockets.h

#pragma once

using namespace System;
using namespace System::Net::Sockets;
using namespace Microsoft::Win32::SafeHandles;
using namespace System::Runtime::InteropServices;

namespace SocketSupport {

    /// <summary>
    /// Helper class to check whether stdin is an unconnected socker
    /// and to get a process-intern duplicate of this socket as a .NET socket.
    /// (Hack that uses internal knowledge of the .NET socket class,
    ///  i.e. that the Socket class is compatible with Winsock and its WSADuplicate function.)
    /// </summary>
    public ref class SocketSupport
	{
    public:
        /// <summary>Initialize Winsock. Throws SocketException in case of error.</summary>
        static SocketSupport();

        /// <summary>Creates a duplicate of stdin and returns a SocketInformation structure about it in 'socketInfo'.
        /// It is possible to create a .NET socket from this.
        /// If stdin is not a socket, returns false.
        /// Throws SocketException if anything else goes wrong.</summary>
        static bool DuplicateStdinSocket([Out]SocketInformation% socketInfo);

		/// <summary>Calls WSAEventSelect on a socket and returns the event as a managed handle.</summary>
		static SafeWaitHandle^ EventSelectRead(IntPtr socketHandle);

        static SafeWaitHandle^ EventSelectWrite(IntPtr socketHandle);

    private:
        static SafeWaitHandle^ EventSelect(IntPtr socketHandle, long eventMask);
    };
}
