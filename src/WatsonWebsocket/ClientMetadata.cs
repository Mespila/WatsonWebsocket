using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace WatsonWebsocket;

internal class ClientMetadata
{
    internal readonly HttpListenerContext HttpContext;
    internal readonly Guid Id;
    internal readonly string Ip;
    internal readonly int Port;
    internal readonly SemaphoreSlim SendLock = new(1);
    internal readonly CancellationTokenSource TokenSource;
    internal readonly WebSocket Ws;
    internal WebSocketContext WsContext;

    internal ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext, CancellationTokenSource tokenSource)
    {
        HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
        Ws = ws                   ?? throw new ArgumentNullException(nameof(ws));
        WsContext = wsContext     ?? throw new ArgumentNullException(nameof(wsContext));
        TokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        Ip = HttpContext.Request.RemoteEndPoint.Address.ToString();
        Port = HttpContext.Request.RemoteEndPoint.Port;
        Id = Guid.NewGuid();
    }

    //internal string IpPort => Ip + ":" + Port;
}