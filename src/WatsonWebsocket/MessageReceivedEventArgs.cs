using System;
using System.Net.WebSockets;

namespace WatsonWebsocket;

/// <summary>
///     Event arguments for when a message is received.
/// </summary>
public class MessageReceivedEventArgs : EventArgs
{
#region Constructors-and-Factories

    internal MessageReceivedEventArgs(Guid id, ArraySegment<byte> data, WebSocketMessageType messageType)
    {
        Id = id;
        Data = data;
        MessageType = messageType;
    }

#endregion

#region Public-Members

    /// <summary>
    ///     Id of the sender.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     The data received.
    /// </summary>
    public ArraySegment<byte> Data { get; }

    /// <summary>
    ///     The type of payload included in the message (Binary or Text).
    /// </summary>
    public WebSocketMessageType MessageType { get; }

#endregion
}