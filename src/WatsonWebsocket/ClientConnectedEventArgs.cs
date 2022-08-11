using System;
using System.Net;

namespace WatsonWebsocket;

/// <summary>
///     Event arguments for when a client connects to the server.
/// </summary>
public class ClientConnectedEventArgs : EventArgs
{
#region Constructors-and-Factories

    internal ClientConnectedEventArgs(Guid id, HttpListenerRequest http)
    {
        Id = id;
        HttpRequest = http;
    }

#endregion

#region Public-Members

    /// <summary>
    ///     The IP:port of the client.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     The HttpListenerRequest from the client.  Helpful for accessing HTTP request related metadata such as the querystring.
    /// </summary>
    public HttpListenerRequest HttpRequest { get; }

#endregion
}