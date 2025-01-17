﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonWebsocket;

/// <summary>
///     Watson Websocket server.
/// </summary>
public class WatsonWsServer : IDisposable
{
#region Public-Members

    /// <summary>
    ///     Determine if the server is listening for new connections.
    /// </summary>
    public bool IsListening
    {
        get
        {
            if (_Listener != null)
            {
                return _Listener.IsListening;
            }

            return false;
        }
    }

    /// <summary>
    ///     Enable or disable statistics.
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    ///     Event fired when a client connects.
    /// </summary>
    public AsyncEvent<ClientConnectedEventArgs> ClientConnected;


    /// <summary>
    ///     Event fired when a client disconnects.
    /// </summary>
    public AsyncEvent<ClientDisconnectedEventArgs> ClientDisconnected;


    /// <summary>
    ///     Event fired when the server stops.
    /// </summary>
    public AsyncEvent<EventArgs> ServerStopped;

    /// <summary>
    ///     Event fired when a message is received.
    /// </summary>
    public AsyncEvent<MessageReceivedEventArgs> MessageReceived;

    /// <summary>
    ///     Indicate whether or not invalid or otherwise unverifiable certificates should be accepted.  Default is true.
    /// </summary>
    public bool AcceptInvalidCertificates { get; set; } = true;

    /// <summary>
    ///     Specify the IP addresses that are allowed to connect.  If none are supplied, all IP addresses are permitted.
    /// </summary>
    public List<string> PermittedIpAddresses = new();

    /// <summary>
    ///     Method to invoke when sending a log message.
    /// </summary>
    public Action<string> Logger = null;

    /// <summary>
    ///     Method to invoke when receiving a raw (non-websocket) HTTP request.
    /// </summary>
    public Action<HttpListenerContext> HttpHandler = null;

    /// <summary>
    ///     Statistics.
    /// </summary>
    public Statistics Stats { get; private set; } = new();

#endregion

#region Private-Members

    private readonly string _Header = "[WatsonWsServer] ";
    private readonly List<string> _ListenerPrefixes = new();
    private readonly HttpListener _Listener;
    private readonly object _PermittedIpsLock = new();
    private readonly ConcurrentDictionary<Guid, ClientMetadata> _Clients;
    private CancellationTokenSource _TokenSource;
    private CancellationToken _Token;
    private Task _AcceptConnectionsTask;

#endregion

#region Constructors-and-Factories

    /// <summary>
    ///     Initializes the Watson websocket server with a single listener prefix.
    ///     Be sure to call 'Start()' to start the server.
    ///     By default, Watson Websocket will listen on http://localhost:9000/.
    /// </summary>
    /// <param name="hostname">The hostname or IP address upon which to listen.</param>
    /// <param name="port">The TCP port on which to listen.</param>
    /// <param name="ssl">Enable or disable SSL.</param>
    public WatsonWsServer(string hostname = "localhost", int port = 9000, bool ssl = false)
    {
        if (port < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (string.IsNullOrEmpty(hostname))
        {
            hostname = "localhost";
        }

        if (ssl)
        {
            _ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
        }
        else
        {
            _ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");
        }

        _Listener = new HttpListener();
        foreach (var prefix in _ListenerPrefixes)
        {
            _Listener.Prefixes.Add(prefix);
        }

        _TokenSource = new CancellationTokenSource();
        _Token = _TokenSource.Token;
        _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
    }

    /// <summary>
    ///     Initializes the Watson websocket server with one or more listener prefixes.
    ///     Be sure to call 'Start()' to start the server.
    /// </summary>
    /// <param name="hostnames">The hostnames or IP addresses upon which to listen.</param>
    /// <param name="port">The TCP port on which to listen.</param>
    /// <param name="ssl">Enable or disable SSL.</param>
    public WatsonWsServer(List<string> hostnames, int port, bool ssl = false)
    {
        if (port < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (hostnames == null)
        {
            throw new ArgumentNullException(nameof(hostnames));
        }

        if (hostnames.Count < 1)
        {
            throw new ArgumentException("At least one hostname must be supplied.");
        }

        foreach (var hostname in hostnames)
        {
            if (ssl)
            {
                _ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
            }
            else
            {
                _ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");
            }
        }

        _Listener = new HttpListener();
        foreach (var prefix in _ListenerPrefixes)
        {
            _Listener.Prefixes.Add(prefix);
        }

        _TokenSource = new CancellationTokenSource();
        _Token = _TokenSource.Token;
        _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
    }

    /// <summary>
    ///     Initializes the Watson websocket server.
    ///     Be sure to call 'Start()' to start the server.
    /// </summary>
    /// <param name="uri">The URI on which you wish to listen, i.e. http://localhost:9090.</param>
    public WatsonWsServer(Uri uri)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (uri.Port < 0)
        {
            throw new ArgumentException("Port must be zero or greater.");
        }

        string host;
        if (!IPAddress.TryParse(uri.Host, out _))
        {
            var dnsLookup = Dns.GetHostEntry(uri.Host);
            if (dnsLookup.AddressList.Length > 0)
            {
                host = dnsLookup.AddressList.First().ToString();
            }
            else
            {
                throw new ArgumentException("Cannot resolve address to IP.");
            }
        }
        else
        {
            host = uri.Host;
        }

        var listenerUri = new UriBuilder(uri)
        {
            Host = host
        };

        _ListenerPrefixes.Add(listenerUri.ToString());

        _Listener = new HttpListener();
        foreach (var prefix in _ListenerPrefixes)
        {
            _Listener.Prefixes.Add(prefix);
        }

        _TokenSource = new CancellationTokenSource();
        _Token = _TokenSource.Token;
        _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
    }

#endregion

#region Public-Methods

    /// <summary>
    ///     Tear down the server and dispose of background workers.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Start accepting new connections.
    /// </summary>
    public void Start()
    {
        if (IsListening)
        {
            throw new InvalidOperationException("Watson websocket server is already running.");
        }

        Stats = new Statistics();

        var logMsg = _Header + "starting on:";
        foreach (var prefix in _ListenerPrefixes)
        {
            logMsg += " " + prefix;
        }

        Logger?.Invoke(logMsg);

        if (AcceptInvalidCertificates)
        {
            SetInvalidCertificateAcceptance();
        }

        _TokenSource = new CancellationTokenSource();
        _Token = _TokenSource.Token;
        _Listener.Start();

        _AcceptConnectionsTask = Task.Run(() => AcceptConnections(_Token), _Token);
    }

    /// <summary>
    ///     Start accepting new connections.
    /// </summary>
    /// <returns>Task.</returns>
    public Task StartAsync(CancellationToken token = default)
    {
        if (IsListening)
        {
            throw new InvalidOperationException("Watson websocket server is already running.");
        }

        Stats = new Statistics();

        var logMsg = _Header + "starting on:";
        foreach (var prefix in _ListenerPrefixes)
        {
            logMsg += " " + prefix;
        }

        Logger?.Invoke(logMsg);

        if (AcceptInvalidCertificates)
        {
            SetInvalidCertificateAcceptance();
        }

        _TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        _Token = token;

        _Listener.Start();

        _AcceptConnectionsTask = Task.Run(() => AcceptConnections(_Token), _Token);

        return Task.Delay(1);
    }

    /// <summary>
    ///     Stop accepting new connections.
    /// </summary>
    public void Stop()
    {
        if (!IsListening)
        {
            throw new InvalidOperationException("Watson websocket server is not running.");
        }

        Logger?.Invoke(_Header + "stopping");

        _Listener.Stop();
    }

    /// <summary>
    ///     Send text data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">ID of the recipient client.</param>
    /// <param name="data">String containing data.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, string data, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(data))
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (!_Clients.TryGetValue(id, out var client))
        {
            Logger?.Invoke(_Header + "unable to find client " + id);
            return Task.FromResult(false);
        }

        var task = MessageWriteAsync(client, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);

        client = null;
        return task;
    }

    /*
    /// <summary>
    ///     Send binary data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">IP:port of the recipient client.</param>
    /// <param name="data">Byte array containing data.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, byte[] data, CancellationToken token = default) => SendAsync(id, new ArraySegment<byte>(data), WebSocketMessageType.Binary, token);
*/

    /// <summary>
    ///     Send binary data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">IP:port of the recipient client.</param>
    /// <param name="data">Byte array containing data.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, ReadOnlyMemory<byte> data, CancellationToken token = default) => SendAsync(id, data, WebSocketMessageType.Binary, token);


    /// <summary>
    ///     Send binary data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">IP:port of the recipient client.</param>
    /// <param name="data">Byte array containing data.</param>
    /// <param name="msgType">Web socket message type.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, byte[] data, WebSocketMessageType msgType, CancellationToken token = default) => SendAsync(id, new ArraySegment<byte>(data), msgType, token);


    /// <summary>
    ///     Send binary data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">Id of the recipient client.</param>
    /// <param name="data">ArraySegment containing data.</param>
    /// <param name="msgType">Web socket message type.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, ReadOnlyMemory<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
    {
        if (data.IsEmpty || data.Length < 1)
        {
            return Task.FromResult(false);
        }

        if (!_Clients.TryGetValue(id, out var client))
        {
            Logger?.Invoke(_Header + "unable to find client " + id);
            return Task.FromResult(false);
        }

        var task = MessageWriteAsync(client, data, msgType, token);

        client = null;
        return task;
    }


    /// <summary>
    ///     Send binary data to the specified client, asynchronously.
    /// </summary>
    /// <param name="id">Id of the recipient client.</param>
    /// <param name="data">ArraySegment containing data.</param>
    /// <param name="msgType">Web socket message type.</param>
    /// <param name="token">Cancellation token allowing for termination of this request.</param>
    /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
    public Task<bool> SendAsync(Guid id, ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
    {
        if (data.Array == null || data.Count < 1)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (!_Clients.TryGetValue(id, out var client))
        {
            Logger?.Invoke(_Header + "unable to find client " + id);
            return Task.FromResult(false);
        }

        var task = MessageWriteAsync(client, data, msgType, token);

        client = null;
        return task;
    }

    /// <summary>
    ///     Determine whether or not the specified client is connected to the server.
    /// </summary>
    /// <param name="id">IP:port of the recipient client.</param>
    /// <returns>Boolean indicating if the client is connected to the server.</returns>
    public bool IsClientConnected(Guid id) => _Clients.ContainsKey(id);

    /// <summary>
    ///     List the IP:port of each connected client.
    /// </summary>
    /// <returns>A string list containing each client IP:port.</returns>
    public IEnumerable<Guid> ListClients() => _Clients.Keys.ToArray();

    /// <summary>
    ///     Forcefully disconnect a client.
    /// </summary>
    /// <param name="id">Id of the client</param>
    public Task DisconnectClientAsync(Guid id)
    {
        if (_Clients.TryRemove(id, out var client))
        {
            lock (client)
            {
                // lock because CloseOutputAsync can fail with InvalidOperationAsync with overlapping operations
                client.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token).Wait(_Token);
                client.TokenSource.Cancel();
                client.Ws.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Retrieve the awaiter.
    /// </summary>
    /// <returns>TaskAwaiter.</returns>
    public TaskAwaiter GetAwaiter() => _AcceptConnectionsTask.GetAwaiter();

#endregion

#region Private-Methods

    /// <summary>
    ///     Tear down the server and dispose of background workers.
    /// </summary>
    /// <param name="disposing">Disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_Clients != null)
            {
                foreach (var client in _Clients)
                {
                    client.Value.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", client.Value.TokenSource.Token);
                    client.Value.TokenSource.Cancel();
                }
            }

            if (_Listener != null)
            {
                if (_Listener.IsListening)
                {
                    _Listener.Stop();
                }

                _Listener.Close();
            }

            _TokenSource.Cancel();
        }
    }

    private void SetInvalidCertificateAcceptance()
    {
        ServicePointManager.ServerCertificateValidationCallback += (_, _, _, _) => true;
    }

    private async Task AcceptConnections(CancellationToken cancelToken)
    {
        try
        {
            while (!cancelToken.IsCancellationRequested)
            {
                if (!_Listener.IsListening)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancelToken).WaitAsync(cancelToken);
                    continue;
                }

                var ctx = await _Listener.GetContextAsync().ConfigureAwait(false);
                var ip = ctx.Request.RemoteEndPoint.Address.ToString();
                var port = ctx.Request.RemoteEndPoint.Port;
                var ipPort = ip + ":" + port;

                lock (_PermittedIpsLock)
                {
                    if (PermittedIpAddresses          != null
                        && PermittedIpAddresses.Count > 0
                        && !PermittedIpAddresses.Contains(ip))
                    {
                        Logger?.Invoke(_Header + "rejecting " + ipPort + " (not permitted)");
                        ctx.Response.StatusCode = 401;
                        ctx.Response.Close();
                        continue;
                    }
                }

                if (!ctx.Request.IsWebSocketRequest)
                {
                    if (HttpHandler == null)
                    {
                        Logger?.Invoke(_Header + "non-websocket request rejected from " + ipPort);
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                    }
                    else
                    {
                        Logger?.Invoke(_Header + "non-websocket request from " + ipPort + " HTTP-forwarded: " + ctx.Request.HttpMethod + " " + ctx.Request.RawUrl);
                        HttpHandler.Invoke(ctx);
                    }

                    continue;
                }

                /*
                    HttpListenerRequest req = ctx.Request;
                    Console.WriteLine(Environment.NewLine + req.HttpMethod.ToString() + " " + req.RawUrl);
                    if (req.Headers != null && req.Headers.Count > 0)
                    {
                        Console.WriteLine("Headers:");
                        var items = req.Headers.AllKeys.SelectMany(req.Headers.GetValues, (k, v) => new { key = k, value = v });
                        foreach (var item in items)
                        {
                            Console.WriteLine("  {0}: {1}", item.key, item.value);
                        }
                    } 
                    */
                await Task.Run(() =>
                {
                    Logger?.Invoke(_Header + "starting data receiver for " + ipPort);

                    var tokenSource = new CancellationTokenSource();
                    var token = tokenSource.Token;

                    Task.Run(async () =>
                    {
                        WebSocketContext wsContext = await ctx.AcceptWebSocketAsync(null);
                        var ws = wsContext.WebSocket;
                        var md = new ClientMetadata(ctx, ws, wsContext, tokenSource);

                        _Clients.TryAdd(md.Id, md);

                        if (ClientConnected is not null)
                        {
                            await ClientConnected.InvokeAsync(this, new ClientConnectedEventArgs(md.Id, ctx.Request));
                        }

                        await Task.Run(() => DataReceiver(md), token);
                    }, token);
                }, _Token).ConfigureAwait(false);
            }
        }
        /*
        catch (HttpListenerException)
        {
            // thrown when disposed
        }
        */
        catch (TaskCanceledException)
        {
            // thrown when disposed
        }
        catch (OperationCanceledException)
        {
            // thrown when disposed
        }
        catch (ObjectDisposedException)
        {
            // thrown when disposed
        }
        catch (Exception e)
        {
            Logger?.Invoke(_Header + "listener exception:" + Environment.NewLine + e);
        }
        finally
        {
            ServerStopped?.InvokeAsync(this, EventArgs.Empty);
        }
    }

    private async Task DataReceiver(ClientMetadata md)
    {
        var header = "[WatsonWsServer " + md.Id + "] ";
        Logger?.Invoke(header           + "starting data receiver");
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var msg = await MessageReadAsync(md, buffer).ConfigureAwait(false);

                if (msg != null)
                {
                    if (EnableStatistics)
                    {
                        Stats.IncrementReceivedMessages();
                        Stats.AddReceivedBytes(msg.Data.Count);
                    }

                    if (msg.Data != null)
                    {
                        _ = Task.Run(() => MessageReceived?.InvokeAsync(this, msg), md.TokenSource.Token);
                    }
                    else
                    {
                        await Task.Delay(10, _Token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // thrown when disposed
        }
        catch (OperationCanceledException)
        {
            // thrown when disposed
        }
        catch (WebSocketException)
        {
            // thrown by MessageReadAsync
        }
        catch (Exception e)
        {
            Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
        }
        finally
        {
            ClientDisconnected?.InvokeAsync(this, new ClientDisconnectedEventArgs(md.Id));
            md.Ws.Dispose();
            Logger?.Invoke(header + "disconnected");
            _Clients.TryRemove(md.Id, out _);
        }
    }

    private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata md, byte[] buffer)
    {
        var header = "[WatsonWsServer " + md.Id + "] ";

        using (var ms = new MemoryStream())
        {
            var seg = new ArraySegment<byte>(buffer);

            while (true)
            {
                var result = await md.Ws.ReceiveAsync(seg, md.TokenSource.Token).ConfigureAwait(false);
                if (result.CloseStatus != null)
                {
                    Logger?.Invoke(header + "close received");
                    await md.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    throw new WebSocketException("Websocket closed.");
                }

                if (md.Ws.State != WebSocketState.Open)
                {
                    Logger?.Invoke(header + "websocket no longer open");
                    throw new WebSocketException("Websocket closed.");
                }

                if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "cancel requested");
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }

                if (result.EndOfMessage)
                {
                    return new MessageReceivedEventArgs(md.Id, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), result.MessageType);
                }
            }
        }
    }


    private async Task<bool> MessageWriteAsync(ClientMetadata md, ReadOnlyMemory<byte> data, WebSocketMessageType msgType, CancellationToken token)
    {
        var header = "[WatsonWsServer " + md.Id + "] ";

        var tokens = new CancellationToken[3];
        tokens[0] = _Token;
        tokens[1] = token;
        tokens[2] = md.TokenSource.Token;

        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens))
        {
            try
            {
            #region Send-Message

                await md.SendLock.WaitAsync(md.TokenSource.Token).ConfigureAwait(false);

                try
                {
                    await md.Ws.SendAsync(data, msgType, true, linkedCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    md.SendLock.Release();
                }

                if (EnableStatistics)
                {
                    Stats.IncrementSentMessages();
                    Stats.AddSentBytes(data.Length);
                }

                return true;

            #endregion
            }
            catch (TaskCanceledException)
            {
                if (_Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "server canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (OperationCanceledException)
            {
                if (_Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (ObjectDisposedException)
            {
                Logger?.Invoke(header + "disposed");
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(header + "websocket disconnected");
            }
            catch (SocketException)
            {
                Logger?.Invoke(header + "socket disconnected");
            }
            catch (InvalidOperationException)
            {
                Logger?.Invoke(header + "disconnected due to invalid operation");
            }
            catch (IOException)
            {
                Logger?.Invoke(header + "IO disconnected");
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
            }
            finally
            {
                md = null;
                tokens = null;
            }
        }

        return false;
    }

/*
    private async Task<bool> MessageWriteAsync(ClientMetadata md, ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
    {
        var header = "[WatsonWsServer " + md.Id + "] ";

        var tokens = new CancellationToken[3];
        tokens[0] = _Token;
        tokens[1] = token;
        tokens[2] = md.TokenSource.Token;

        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens))
        {
            try
            {
            #region Send-Message

                await md.SendLock.WaitAsync(md.TokenSource.Token).ConfigureAwait(false);

                try
                {
                    await md.Ws.SendAsync(data, msgType, true, linkedCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    md.SendLock.Release();
                }

                if (EnableStatistics)
                {
                    Stats.IncrementSentMessages();
                    Stats.AddSentBytes(data.Count);
                }

                return true;

            #endregion
            }
            catch (TaskCanceledException)
            {
                if (_Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "server canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (OperationCanceledException)
            {
                if (_Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "canceled");
                }
                else if (token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "message send canceled");
                }
                else if (md.TokenSource.Token.IsCancellationRequested)
                {
                    Logger?.Invoke(header + "client canceled");
                }
            }
            catch (ObjectDisposedException)
            {
                Logger?.Invoke(header + "disposed");
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(header + "websocket disconnected");
            }
            catch (SocketException)
            {
                Logger?.Invoke(header + "socket disconnected");
            }
            catch (InvalidOperationException)
            {
                Logger?.Invoke(header + "disconnected due to invalid operation");
            }
            catch (IOException)
            {
                Logger?.Invoke(header + "IO disconnected");
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e);
            }
            finally
            {
                md = null;
                tokens = null;
            }
        }

        return false;
    }
    */

#endregion
}