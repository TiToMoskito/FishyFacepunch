#if !FishyFacepunch
using FishNet.Transporting;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishyFacepunch.Server
{
    public class ServerSocket : CommonSocket
    {
        #region Public.
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionStates GetConnectionState(int connectionId)
        {
            //Remote clients can only have Started or Stopped states since we cannot know in between.
            if (_steamConnections.Second.ContainsKey(connectionId))
                return RemoteConnectionStates.Started;
            else
                return RemoteConnectionStates.Stopped;
        }
        #endregion

        #region Private.
        /// <summary>
        /// SteamConnections for ConnectionIds.
        /// </summary>
        private BidirectionalDictionary<Connection, int> _steamConnections = new BidirectionalDictionary<Connection, int>();
        /// <summary>
        /// SteamIds for ConnectionIds.
        /// </summary>
        private BidirectionalDictionary<SteamId, int> _steamIds = new BidirectionalDictionary<SteamId, int>();
        /// <summary>
        /// Maximum number of remote connections.
        /// </summary>
        private int _maximumClients;
        /// <summary>
        /// Next Id to use for a connection.
        /// </summary>
        private int _nextConnectionId;
        /// <summary>
        /// Socket for the connection.
        /// </summary>
        private FishySocketManager _socket;
        /// <summary>
        /// ConnectionIds which can be reused.
        /// </summary>
        private Queue<int> _cachedConnectionIds = new Queue<int>();
        #endregion

        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="t"></param>
        internal override void Initialize(Transport t)
        {
            base.Initialize(t);
        }

        /// <summary>
        /// Resets the socket if invalid.
        /// </summary>
        internal void ResetInvalidSocket()
        {
            /* Force connection state to stopped if listener is invalid.
            * Not sure if steam may change this internally so better
            * safe than sorry and check before trying to connect
            * rather than being stuck in the incorrect state. */
            if (_socket == default)
                base.SetConnectionState(LocalConnectionStates.Stopped, true);
        }
        /// <summary>
        /// Starts the server.
        /// </summary>
        internal bool StartConnection(string address, ushort port, int maximumClients, bool peerToPeer)
        {
            base.PeerToPeer = peerToPeer;

            SteamNetworkingSockets.OnConnectionStatusChanged += OnRemoteConnectionState;

            SetMaximumClients(maximumClients);
            _nextConnectionId = 0;
            _cachedConnectionIds.Clear();

            base.SetConnectionState(LocalConnectionStates.Starting, true);
            
            if (_socket != null)
            {
                _socket?.Close();
                _socket = default;
            }

#if UNITY_SERVER
            _socket = SteamNetworkingSockets.CreateNormalSocket<FishySocketManager>(NetAddress.From(address, port));
#else
            _socket = SteamNetworkingSockets.CreateRelaySocket<FishySocketManager>();
#endif
            _socket.ForwardMessage = OnMessageReceived;

            base.SetConnectionState(LocalConnectionStates.Started, true);

            return true;
        }

        /// <summary>
        /// Stops the local socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (base.GetConnectionState() == LocalConnectionStates.Stopped)
                return false;

            base.SetConnectionState(LocalConnectionStates.Stopping, true);

            if (_socket != null)
            {
                SteamNetworkingSockets.OnConnectionStatusChanged -= OnRemoteConnectionState;
                _socket?.Close();
                _socket = default;
            }
            
            base.SetConnectionState(LocalConnectionStates.Stopped, true);

            return true;
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        internal bool StopConnection(int connectionId)
        {
            if (_steamConnections.Second.TryGetValue(connectionId, out Connection steamConn))
            {
                return StopConnection(connectionId, steamConn);
            }
            else
            {
                Debug.LogError($"Steam connection not found for connectionId {connectionId}.");
                return false;
            }
        }
        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="socket"></param>
        private bool StopConnection(int connectionId, Connection socket)
        {
            socket.Close(false, 0, "Graceful disconnect");
            _steamConnections.Remove(connectionId);
            _steamIds.Remove(connectionId);
            Debug.Log($"Client with ConnectionID {connectionId} disconnected.");
            base.Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionStates.Stopped, connectionId));
            _cachedConnectionIds.Enqueue(connectionId);

            return true;
        }

        /// <summary>
        /// Called when a remote connection state changes.
        /// </summary>
        private void OnRemoteConnectionState(Connection conn, ConnectionInfo info)
        {
            ulong clientSteamID = info.Identity.SteamId;
            if (info.State == ConnectionState.Connecting)
            {
                if (_steamConnections.Count >= _maximumClients)
                {
                    Debug.Log($"Incoming connection {clientSteamID} would exceed max connection count. Rejecting.");
                    conn.Close(false, 0, "Max Connection Count");
                    return;
                }

                Result res;

                if ((res = conn.Accept()) == Result.OK)
                {
                    Debug.Log($"Accepting connection {clientSteamID}");
                }
                else
                {
                    Debug.Log($"Connection {clientSteamID} could not be accepted: {res.ToString()}");
                }
            }
            else if (info.State == ConnectionState.Connected)
            {
                int connectionId = (_cachedConnectionIds.Count > 0) ? _cachedConnectionIds.Dequeue() : _nextConnectionId++;
                _steamConnections.Add(conn, connectionId);
                _steamIds.Add(clientSteamID, connectionId);
                Debug.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connectionId}");
                base.Transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionStates.Started, connectionId));
            }
            else if (info.State == ConnectionState.ClosedByPeer || info.State == ConnectionState.ProblemDetectedLocally)
            {
                if (_steamConnections.TryGetValue(conn, out int connId))
                {
                    StopConnection(connId, conn);
                }
            }
            else
            {
                Debug.Log($"Connection {clientSteamID} state changed: {info.State.ToString()}");
            }
        }


        /// <summary>
        /// Allows for Outgoing queue to be iterated.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (base.GetConnectionState() != LocalConnectionStates.Started)
                return;

            foreach (Connection conn in _steamConnections.FirstTypes)
            {
                conn.Flush();
            }
        }

        /// <summary>
        /// Iterates the Incoming queue.
        /// </summary>
        /// <param name="transport"></param>
        internal void IterateIncoming()
        {
            //Stopped or trying to stop.
            if (base.GetConnectionState() == LocalConnectionStates.Stopped || base.GetConnectionState() == LocalConnectionStates.Stopping)
                return;            

            _socket.Receive(MAX_MESSAGES);
        }

        private void OnMessageReceived(Connection conn, IntPtr dataPtr, int size)
        {
            (byte[] data, int ch) = ProcessMessage(dataPtr, size);
            base.Transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(new ArraySegment<byte>(data), (Channel)ch, _steamConnections[conn]));
        }

        /// <summary>
        /// Sends data to a client.
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="segment"></param>
        /// <param name="connectionId"></param>
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (base.GetConnectionState() != LocalConnectionStates.Started)
                return;

            if (_steamConnections.TryGetValue(connectionId, out Connection steamConn))
            {
                Result res = base.Send(steamConn, segment, channelId);

                if (res == Result.NoConnection || res == Result.InvalidParam)
                {
                    Debug.Log($"Connection to {connectionId} was lost.");
                    StopConnection(connectionId, steamConn);
                }
                else if (res != Result.OK)
                {
                    Debug.LogError($"Could not send: {res.ToString()}");
                }
            }
            else
            {
                Debug.LogError("Trying to send on unknown connection: " + connectionId);
            }
        }

        /// <summary>
        /// Gets the address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        internal string GetConnectionAddress(int connectionId)
        {
            if (_steamIds.TryGetValue(connectionId, out SteamId steamId))
            {
                return steamId.ToString();
            }
            else
            {
                Debug.LogError("Trying to get info on unknown connection: " + connectionId);
                //OnReceivedError.Invoke(connectionId, new Exception("ERROR Unknown Connection"));
                return string.Empty;
            }
        }


        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        internal void SetMaximumClients(int value)
        {
            _maximumClients = value;
        }
        internal int GetMaximumClients()
        {
            return _maximumClients;
        }
    }
}
#endif // !DISABLESTEAMWORKS