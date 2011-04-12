// This is the main DLL file.

#include "stdafx.h"

#include "SocketSupport.h"



namespace SocketSupport
{
    static SocketSupport::SocketSupport()
    {
        WORD wVersionRequested = MAKEWORD(2, 0);
        WSADATA wsaData;
        int err = WSAStartup(wVersionRequested, &wsaData);
        if( err == SOCKET_ERROR )
        {
            throw gcnew SocketException(err);
        }
    }


    bool SocketSupport::DuplicateStdinSocket([Out]SocketInformation% result)
    {
        array<byte>^ protocolInfo = gcnew array<byte>(sizeof(WSAPROTOCOL_INFO));
        pin_ptr<byte> ptr = &protocolInfo[0];
        WSAPROTOCOL_INFOW* infoPtr = (WSAPROTOCOL_INFOW*)ptr;
        
	    result.ProtocolInformation = protocolInfo;
        result.Options = SocketInformationOptions::Listening;

        HANDLE oldHandle = GetStdHandle(STD_INPUT_HANDLE);
        int err = WSADuplicateSocket((SOCKET)oldHandle, GetCurrentProcessId(), infoPtr);
        if( err == SOCKET_ERROR )
        {
            int errCode = WSAGetLastError();
            if( errCode == WSAENOTSOCK )
                return false;
            else
                throw gcnew SocketException( errCode );
        }

        CloseHandle(oldHandle);

        return true;
    }


	SafeWaitHandle^ SocketSupport::EventSelectRead(IntPtr sInt)
	{
        return EventSelect(sInt, FD_READ);
    }

	SafeWaitHandle^ SocketSupport::EventSelectWrite(IntPtr sInt)
	{
        return EventSelect(sInt, FD_WRITE);
    }

    SafeWaitHandle^ SocketSupport::EventSelect(IntPtr sInt, long eventMask)
	{
		SOCKET s = (SOCKET)sInt.ToInt32();
		WSAEVENT ev = WSACreateEvent();
		int err = WSAEventSelect(s, ev, eventMask);
        if( err == SOCKET_ERROR )
        {
            throw gcnew SocketException(err);
        }
		return gcnew SafeWaitHandle( IntPtr(ev), TRUE );
	}
}