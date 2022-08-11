using System;

namespace WatsonWebsocket;

/// <summary>
///     Event arguments for when a client disconnects from the server.
/// </summary>
public class ClientDisconnectedEventArgs : EventArgs
{
    internal ClientDisconnectedEventArgs(Guid ipPort)
    {
        Id = ipPort;
    }


    /// <summary>
    ///     The IP:port of the client.
    /// </summary>
    public Guid Id { get; }
}