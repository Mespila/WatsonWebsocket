﻿using System;
using System.Threading;

namespace WatsonWebsocket;

/// <summary>
///     WatsonWebsocket statistics.
/// </summary>
public class Statistics
{
#region Constructors-and-Factories

    /// <summary>
    ///     Initialize the statistics object.
    /// </summary>
    public Statistics()
    {
    }

#endregion

#region Public-Members

    /// <summary>
    ///     The time at which the client or server was started.
    /// </summary>
    public DateTime StartTime { get; } = DateTime.Now.ToUniversalTime();

    /// <summary>
    ///     The amount of time which the client or server has been up.
    /// </summary>
    public TimeSpan UpTime => DateTime.Now.ToUniversalTime() - StartTime;

    /// <summary>
    ///     The number of bytes received.
    /// </summary>
    public long ReceivedBytes => _ReceivedBytes;

    /// <summary>
    ///     The number of messages received.
    /// </summary>
    public long ReceivedMessages => _ReceivedMessages;

    /// <summary>
    ///     Average received message size in bytes.
    /// </summary>
    public int ReceivedMessageSizeAverage
    {
        get
        {
            if (_ReceivedBytes > 0 && _ReceivedMessages > 0)
            {
                return (int)(_ReceivedBytes / _ReceivedMessages);
            }

            return 0;
        }
    }

    /// <summary>
    ///     The number of bytes sent.
    /// </summary>
    public long SentBytes => _SentBytes;

    /// <summary>
    ///     The number of messages sent.
    /// </summary>
    public long SentMessages => _SentMessages;

    /// <summary>
    ///     Average sent message size in bytes.
    /// </summary>
    public decimal SentMessageSizeAverage
    {
        get
        {
            if (_SentBytes > 0 && _SentMessages > 0)
            {
                return (int)(_SentBytes / _SentMessages);
            }

            return 0;
        }
    }

#endregion

#region Private-Members

    private long _ReceivedBytes;
    private long _ReceivedMessages;
    private long _SentBytes;
    private long _SentMessages;

#endregion

#region Public-Methods

    /// <summary>
    ///     Return human-readable version of the object.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        var ret =
            "--- Statistics ---" + Environment.NewLine        +
            "    Started     : " + StartTime                  + Environment.NewLine +
            "    Uptime      : " + UpTime                     + Environment.NewLine +
            "    Received    : " + Environment.NewLine        +
            "       Bytes    : " + ReceivedBytes              + Environment.NewLine +
            "       Messages : " + ReceivedMessages           + Environment.NewLine +
            "       Average  : " + ReceivedMessageSizeAverage + " bytes"            + Environment.NewLine +
            "    Sent        : " + Environment.NewLine        +
            "       Bytes    : " + SentBytes                  + Environment.NewLine +
            "       Messages : " + SentMessages               + Environment.NewLine +
            "       Average  : " + SentMessageSizeAverage     + " bytes"            + Environment.NewLine;
        return ret;
    }

    /// <summary>
    ///     Reset statistics other than StartTime and UpTime.
    /// </summary>
    public void Reset()
    {
        _ReceivedBytes = 0;
        _ReceivedMessages = 0;
        _SentBytes = 0;
        _SentMessages = 0;
    }

#endregion

#region Internal-Methods

    internal void IncrementReceivedMessages()
    {
        _ReceivedMessages = Interlocked.Increment(ref _ReceivedMessages);
    }

    internal void IncrementSentMessages()
    {
        _SentMessages = Interlocked.Increment(ref _SentMessages);
    }

    internal void AddReceivedBytes(long bytes)
    {
        _ReceivedBytes = Interlocked.Add(ref _ReceivedBytes, bytes);
    }

    internal void AddSentBytes(long bytes)
    {
        _SentBytes = Interlocked.Add(ref _SentBytes, bytes);
    }

#endregion

#region Private-Methods

#endregion
}