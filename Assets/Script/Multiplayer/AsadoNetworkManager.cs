using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using TMPro;
using AsadoSimulator.UI;

namespace AsadoSimulator.Multiplayer
{
    /// <summary>
    /// 烤肉模拟器原生高并发 TCP 联机系统核心 (AsadoNetworkManager)
    /// 特性：
    /// 1. 基于高性能异步非阻塞 TCP Socket 构筑，零第三方插件依赖，开箱即用。
    /// 2. 完美适配本地与外部 Cloudflared TCP 穿透隧道 (端口: 25565)。
    /// 3. 支持 Host (房主开服+本地进入)、Client (一键加入远程/本地房间)。
    /// 4. 20Hz 实时双向同步玩家 3D 位置、视线朝向、下蹲姿态，并自动渲染联机队友 3D 形象与浮空头顶铭牌。
    /// 5. 线程安全写操作锁定，杜绝 TCP 数据包粘包/交错导致的断线。
    /// 6. 实时心跳 Ping (毫秒级别往返 RTT 计算)，精确反馈至 PerformanceHUD。
    /// </summary>
    public class AsadoNetworkManager : MonoBehaviour
    {
        public static AsadoNetworkManager Instance { get; private set; }

        [Header("TCP 端口与传输层配置")]
        [Tooltip("默认 TCP 服务端监听端口 (默认 25565)")]
        [SerializeField] private ushort defaultServerPort = 25565;

        [Tooltip("默认 TCP 客户端连接端口 (默认 25566)")]
        [SerializeField] private ushort defaultClientPort = 25566;

        public ushort ServerPort { get; set; } = 25565;
        public ushort ClientPort { get; set; } = 25566;

        // 全局事件回调
        public static event Action OnClientConnectedEvent;
        public static event Action OnClientDisconnectedEvent;
        public static event Action<string> OnNetworkErrorEvent;
        public static event Action<int> OnPlayerCountChangedEvent;

        // 网络状态
        private bool _isHost = false;
        private bool _isClientOnly = false;
        private bool _isServerRunning = false;
        private bool _isClientConnected = false;
        private int _localPlayerId = -1;
        private int _playerCounter = 0;
        private float _roundTripTimeMs = 0f;

        public bool IsNetworkActive => _isServerRunning || _isClientConnected;
        public bool IsHost => _isHost;
        public bool IsClientOnly => _isClientOnly;
        public float RoundTripTimeMs => _roundTripTimeMs;
        public int ConnectedPlayerCount => _isServerRunning ? Mathf.Max(1, _serverClients.Count) : (_remotePlayers.Count + 1);

        // TCP 服务端
        private TcpListener _tcpListener;
        private CancellationTokenSource _serverCts;
        private readonly ConcurrentDictionary<int, TcpClient> _serverClients = new ConcurrentDictionary<int, TcpClient>();
        private readonly ConcurrentDictionary<TcpClient, object> _clientWriteLocks = new ConcurrentDictionary<TcpClient, object>();
        private readonly ConcurrentDictionary<int, (Vector3 pos, float rotY, bool isCrouch)> _serverLatestTransforms = new ConcurrentDictionary<int, (Vector3, float, bool)>();

        // TCP 客户端
        private TcpClient _clientSocket;
        private NetworkStream _clientStream;
        private CancellationTokenSource _clientCts;
        private readonly object _clientSendLock = new object();

        // 主线程调度队列
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // 远程玩家化身管理
        private readonly Dictionary<int, RemotePlayerAvatar> _remotePlayers = new Dictionary<int, RemotePlayerAvatar>();

        // 同步计时
        private float _syncTimer = 0f;
        private float _pingTimer = 0f;
        private Transform _localPlayerTransform;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            // 1. 处理所有跨线程接收到的网络消息
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AsadoNetworkManager] 执行网络主线程任务异常: {ex.Message}");
                }
            }

            if (!IsNetworkActive) return;

            // 2. 本地玩家 Transform 同步广播 (20Hz)
            if (_isClientConnected && _localPlayerId > 0)
            {
                _syncTimer += Time.unscaledDeltaTime;
                if (_syncTimer >= 0.05f)
                {
                    _syncTimer = 0f;
                    SendLocalPlayerTransform();
                }

                // 3. 周期性发送 Ping 包测量真实 RTT
                _pingTimer += Time.unscaledDeltaTime;
                if (_pingTimer >= 1.0f)
                {
                    _pingTimer = 0f;
                    SendPing();
                }
            }
        }

        private void OnDestroy()
        {
            StopGame();
        }

        #region 1. 启停控制 (Host / Client / Stop)

        /// <summary>
        /// 启动房主模式 (Host: 本地服务器 + 本地客户端加入)
        /// 服务端默认端口: 25565
        /// </summary>
        public void StartHostGame(ushort port = 0)
        {
            StopGame();
            _isHost = true;
            _isClientOnly = false;
            _playerCounter = 0;

            if (port == 0) port = ServerPort > 0 ? ServerPort : defaultServerPort;
            ServerPort = port;

            try
            {
                // 1. 启动 TCP 服务端
                _serverCts = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                _isServerRunning = true;
                Task.Run(() => ServerAcceptLoop(_serverCts.Token));

                Debug.Log($"[AsadoNetworkManager] ✅ TCP 服务端已成功启动，监听端口: {port}");

                // 2. 本地客户端直连服务端口
                StartClientGame("127.0.0.1", port);
            }
            catch (Exception ex)
            {
                string err = $"启动 Host 失败: {ex.Message}";
                Debug.LogError($"[AsadoNetworkManager] {err}");
                OnNetworkErrorEvent?.Invoke(err);
                StopGame();
            }
        }

        /// <summary>
        /// 启动客户端模式并连接到目标地址
        /// 客户端默认端口: 25566 (支持在 targetAddress 中直接输入 IP:Port，如 127.0.0.1:25566)
        /// </summary>
        public void StartClientGame(string targetAddress, ushort port = 0)
        {
            if (string.IsNullOrWhiteSpace(targetAddress))
            {
                targetAddress = "127.0.0.1";
            }
            targetAddress = targetAddress.Trim();

            // 支持直接在地址栏输入 IP:Port (如 127.0.0.1:25566 或 mygame.trycloudflare.com:25565)
            if (targetAddress.Contains(":"))
            {
                var parts = targetAddress.Split(':');
                targetAddress = parts[0];
                if (ushort.TryParse(parts[1], out ushort parsedPort) && parsedPort > 0)
                {
                    port = parsedPort;
                }
            }

            if (port == 0) port = ClientPort > 0 ? ClientPort : defaultClientPort;
            ClientPort = port;

            if (!_isHost)
            {
                StopGame();
                _isClientOnly = true;
            }

            ushort connectPort = port;
            string connectAddr = targetAddress;

            Task.Run(async () =>
            {
                try
                {
                    _clientCts = new CancellationTokenSource();
                    _clientSocket = new TcpClient();
                    _clientSocket.NoDelay = true;

                    await _clientSocket.ConnectAsync(connectAddr, connectPort);
                    _clientStream = _clientSocket.GetStream();
                    _isClientConnected = true;

                    _mainThreadQueue.Enqueue(() =>
                    {
                        Debug.Log($"[AsadoNetworkManager] ✅ 成功连接至服务器: {connectAddr}:{connectPort}");
                        OnClientConnectedEvent?.Invoke();
                        OnPlayerCountChangedEvent?.Invoke(ConnectedPlayerCount);
                    });

                    // 启动客户端数据读取循环
                    _ = ClientReceiveLoop(_clientCts.Token);
                }
                catch (Exception ex)
                {
                    _mainThreadQueue.Enqueue(() =>
                    {
                        string err = $"连接服务器 {connectAddr}:{connectPort} 失败: {ex.Message}";
                        Debug.LogWarning($"[AsadoNetworkManager] {err}");
                        OnNetworkErrorEvent?.Invoke(err);
                        OnClientDisconnectedEvent?.Invoke();
                    });
                }
            });
        }

        /// <summary>
        /// 断开连接 / 停止服务
        /// </summary>
        public void StopGame()
        {
            _serverCts?.Cancel();
            _clientCts?.Cancel();

            lock (_clientSendLock)
            {
                try
                {
                    _clientStream?.Close();
                    _clientSocket?.Close();
                }
                catch {}
                _clientStream = null;
                _clientSocket = null;
            }

            try
            {
                _tcpListener?.Stop();
                foreach (var kvp in _serverClients)
                {
                    try { kvp.Value?.Close(); } catch {}
                }
                _serverClients.Clear();
            }
            catch {}

            _clientWriteLocks.Clear();
            _serverLatestTransforms.Clear();

            // 清理所有远程玩家化身
            foreach (var avatar in _remotePlayers.Values)
            {
                if (avatar != null && avatar.gameObject != null)
                {
                    Destroy(avatar.gameObject);
                }
            }
            _remotePlayers.Clear();

            bool wasActive = _isServerRunning || _isClientConnected;
            _isServerRunning = false;
            _isClientConnected = false;
            _isHost = false;
            _isClientOnly = false;
            _localPlayerId = -1;
            _roundTripTimeMs = 0f;

            if (wasActive)
            {
                Debug.Log("[AsadoNetworkManager] 联机连接已完全关闭。");
                OnClientDisconnectedEvent?.Invoke();
                OnPlayerCountChangedEvent?.Invoke(1);
            }
        }

        #endregion

        #region 2. 服务端通信底层

        private async Task ServerAcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isServerRunning)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    int newPlayerId = Interlocked.Increment(ref _playerCounter);

                    _serverClients[newPlayerId] = client;
                    _mainThreadQueue.Enqueue(() =>
                    {
                        Debug.Log($"[AsadoNetworkManager] 🎮 新客户端接入！分配 Player ID: {newPlayerId}");
                        OnPlayerCountChangedEvent?.Invoke(ConnectedPlayerCount);
                    });

                    // 1. 发送欢迎握手包给新客户端
                    SendPacketToClient(client, NetMsgType.Welcome, w =>
                    {
                        w.Write(newPlayerId);
                        w.Write(ConnectedPlayerCount);
                    });

                    // 2. 将现有已在场玩家的位置立即同步给新客户端，防止新进入者看不到已有玩家
                    foreach (var kvp in _serverLatestTransforms)
                    {
                        if (kvp.Key != newPlayerId)
                        {
                            var tr = kvp.Value;
                            SendPacketToClient(client, NetMsgType.PlayerTransform, w =>
                            {
                                w.Write(kvp.Key);
                                w.Write(tr.pos.x);
                                w.Write(tr.pos.y);
                                w.Write(tr.pos.z);
                                w.Write(tr.rotY);
                                w.Write(tr.isCrouch);
                            });
                        }
                    }

                    // 3. 启动该客户端的读取循环
                    _ = ServerClientReadLoop(newPlayerId, client, token);
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    Debug.LogWarning($"[AsadoNetworkManager] 服务端接入异常: {ex.Message}");
                }
            }
        }

        private async Task ServerClientReadLoop(int playerId, TcpClient client, CancellationToken token)
        {
            var stream = client.GetStream();
            byte[] headerBuffer = new byte[5];

            while (!token.IsCancellationRequested && _isServerRunning)
            {
                try
                {
                    int read = await ReadExactBytesAsync(stream, headerBuffer, 5, token);
                    if (read < 5) break;

                    int payloadLen = BitConverter.ToInt32(headerBuffer, 0);
                    byte msgType = headerBuffer[4];

                    // 数据包长度保护 (防止异常流导致崩溃)
                    if (payloadLen < 0 || payloadLen > 512 * 1024)
                    {
                        Debug.LogWarning($"[AsadoNetworkManager] 服务端接收到异常数据包大小 ({payloadLen} 字节)，中断该客户端 #{playerId}");
                        break;
                    }

                    byte[] payload = new byte[payloadLen];
                    if (payloadLen > 0)
                    {
                        await ReadExactBytesAsync(stream, payload, payloadLen, token);
                    }

                    // 处理接收包并向其他所有客户端广播转发
                    HandleServerIncomingMessage(playerId, msgType, payload);
                }
                catch { break; }
            }

            // 客户端断开连接
            _serverClients.TryRemove(playerId, out _);
            _clientWriteLocks.TryRemove(client, out _);
            _serverLatestTransforms.TryRemove(playerId, out _);

            _mainThreadQueue.Enqueue(() =>
            {
                Debug.Log($"[AsadoNetworkManager] 客户端 #{playerId} 已断开连接。");
                OnPlayerCountChangedEvent?.Invoke(ConnectedPlayerCount);
            });

            // 向所有其他客户端广播 PlayerLeave
            BroadcastPacketFromServer(NetMsgType.PlayerLeave, w =>
            {
                w.Write(playerId);
                w.Write(ConnectedPlayerCount);
            }, excludePlayerId: playerId);
        }

        private void HandleServerIncomingMessage(int senderId, byte msgType, byte[] payload)
        {
            switch ((NetMsgType)msgType)
            {
                case NetMsgType.Ping:
                    // 原路返回 Pong
                    if (_serverClients.TryGetValue(senderId, out var client))
                    {
                        SendPacketToClient(client, NetMsgType.Pong, w => w.Write(payload));
                    }
                    break;

                case NetMsgType.PlayerTransform:
                    if (payload.Length >= 17)
                    {
                        // 强制将 payload 中的 playerId 纠正为服务端记录的 senderId
                        Buffer.BlockCopy(BitConverter.GetBytes(senderId), 0, payload, 0, 4);

                        using (var ms = new MemoryStream(payload))
                        using (var br = new BinaryReader(ms))
                        {
                            br.ReadInt32(); // senderId
                            Vector3 p = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                            float r = br.ReadSingle();
                            bool c = br.ReadBoolean();
                            _serverLatestTransforms[senderId] = (p, r, c);
                        }
                    }
                    // 广播转发给除发送者外的所有玩家
                    BroadcastPacketFromServer((NetMsgType)msgType, w => w.Write(payload), excludePlayerId: senderId);
                    break;

                case NetMsgType.MeatSync:
                    // 广播转发给除发送者外的所有玩家
                    BroadcastPacketFromServer((NetMsgType)msgType, w => w.Write(payload), excludePlayerId: senderId);
                    break;
            }
        }

        private void SafeWriteToClient(TcpClient client, byte[] packet)
        {
            if (client == null || !client.Connected) return;
            var lockObj = _clientWriteLocks.GetOrAdd(client, _ => new object());
            lock (lockObj)
            {
                try
                {
                    var stream = client.GetStream();
                    if (stream != null && stream.CanWrite)
                    {
                        stream.Write(packet, 0, packet.Length);
                        stream.Flush();
                    }
                }
                catch {}
            }
        }

        private void BroadcastPacketFromServer(NetMsgType type, Action<BinaryWriter> writeAction, int excludePlayerId = -1)
        {
            byte[] packet = BuildPacket(type, writeAction);
            foreach (var kvp in _serverClients)
            {
                if (kvp.Key == excludePlayerId) continue;
                SafeWriteToClient(kvp.Value, packet);
            }
        }

        private void SendPacketToClient(TcpClient client, NetMsgType type, Action<BinaryWriter> writeAction)
        {
            byte[] packet = BuildPacket(type, writeAction);
            SafeWriteToClient(client, packet);
        }

        #endregion

        #region 3. 客户端通信底层

        private async Task ClientReceiveLoop(CancellationToken token)
        {
            byte[] header = new byte[5];
            while (!token.IsCancellationRequested && _isClientConnected)
            {
                try
                {
                    int read = await ReadExactBytesAsync(_clientStream, header, 5, token);
                    if (read < 5) break;

                    int len = BitConverter.ToInt32(header, 0);
                    byte type = header[4];

                    // 异常数据包长度保护
                    if (len < 0 || len > 512 * 1024)
                    {
                        Debug.LogWarning($"[AsadoNetworkManager] 客户端收到异常长度包 ({len} 字节)，断开连接");
                        break;
                    }

                    byte[] payload = new byte[len];
                    if (len > 0)
                    {
                        await ReadExactBytesAsync(_clientStream, payload, len, token);
                    }

                    _mainThreadQueue.Enqueue(() => HandleClientMessage((NetMsgType)type, payload));
                }
                catch { break; }
            }

            _mainThreadQueue.Enqueue(() =>
            {
                if (_isClientConnected)
                {
                    Debug.LogWarning("[AsadoNetworkManager] ⚠️ 与服务器连接断开。");
                    StopGame();
                }
            });
        }

        private void HandleClientMessage(NetMsgType type, byte[] payload)
        {
            using (var reader = new BinaryReader(new MemoryStream(payload)))
            {
                switch (type)
                {
                    case NetMsgType.Pong:
                        double sendTime = reader.ReadDouble();
                        _roundTripTimeMs = Mathf.Max(1f, (float)((Time.realtimeSinceStartupAsDouble - sendTime) * 1000.0));
                        break;

                    case NetMsgType.Welcome:
                        _localPlayerId = reader.ReadInt32();
                        int totalPlayers = reader.ReadInt32();
                        Debug.Log($"[AsadoNetworkManager] 握手成功，已分配 Local Player ID: {_localPlayerId}，当前房间人数: {totalPlayers}");
                        OnPlayerCountChangedEvent?.Invoke(totalPlayers);
                        break;

                    case NetMsgType.PlayerTransform:
                        int remoteId = reader.ReadInt32();
                        if (remoteId <= 0 || remoteId == _localPlayerId) return; // 忽略无效包或自身反射包

                        Vector3 pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        float rotY = reader.ReadSingle();
                        bool isCrouching = reader.ReadBoolean();

                        UpdateRemotePlayerAvatar(remoteId, pos, rotY, isCrouching);
                        break;

                    case NetMsgType.PlayerLeave:
                        int leaveId = reader.ReadInt32();
                        int count = reader.ReadInt32();
                        RemoveRemotePlayerAvatar(leaveId);
                        OnPlayerCountChangedEvent?.Invoke(count);
                        break;

                    case NetMsgType.MeatSync:
                        // 烤肉状态同步
                        int meatId = reader.ReadInt32();
                        Vector3 meatPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Quaternion meatRot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        float doneness = reader.ReadSingle();
                        bool isBurnt = reader.ReadBoolean();
                        ApplyMeatSync(meatId, meatPos, meatRot, doneness, isBurnt);
                        break;
                }
            }
        }

        private void SafeWriteLocalClient(byte[] packet)
        {
            if (!_isClientConnected || _clientStream == null) return;
            lock (_clientSendLock)
            {
                try
                {
                    if (_clientSocket != null && _clientSocket.Connected && _clientStream.CanWrite)
                    {
                        _clientStream.Write(packet, 0, packet.Length);
                        _clientStream.Flush();
                    }
                }
                catch {}
            }
        }

        private void SendPing()
        {
            if (!_isClientConnected || _clientStream == null || _localPlayerId <= 0) return;
            byte[] packet = BuildPacket(NetMsgType.Ping, w => w.Write(Time.realtimeSinceStartupAsDouble));
            SafeWriteLocalClient(packet);
        }

        private void EnsureLocalPlayerTransform()
        {
            if (_localPlayerTransform != null) return;

            var pc = UnityEngine.Object.FindAnyObjectByType<Player.PlayerController>();
            if (pc != null)
            {
                _localPlayerTransform = pc.transform;
                return;
            }

            var p = GameObject.Find("Player");
            if (p != null)
            {
                _localPlayerTransform = p.transform;
                return;
            }

            if (Camera.main != null)
            {
                _localPlayerTransform = Camera.main.transform.root;
            }
        }

        private void SendLocalPlayerTransform()
        {
            if (!_isClientConnected || _clientStream == null || _localPlayerId <= 0) return;

            EnsureLocalPlayerTransform();
            if (_localPlayerTransform == null) return;

            Vector3 pos = _localPlayerTransform.position;
            float rotY = _localPlayerTransform.eulerAngles.y;
            bool isCrouch = false;
            var pc = _localPlayerTransform.GetComponent<Player.PlayerController>();
            if (pc != null) isCrouch = pc.IsCrouching;

            byte[] packet = BuildPacket(NetMsgType.PlayerTransform, w =>
            {
                w.Write(_localPlayerId);
                w.Write(pos.x);
                w.Write(pos.y);
                w.Write(pos.z);
                w.Write(rotY);
                w.Write(isCrouch);
            });

            SafeWriteLocalClient(packet);
        }

        /// <summary>
        /// 广播同步烤肉位置与烹饪熟度
        /// </summary>
        public void BroadcastMeatState(int meatId, Vector3 pos, Quaternion rot, float doneness, bool isBurnt)
        {
            if (!_isClientConnected || _clientStream == null || _localPlayerId <= 0) return;

            byte[] packet = BuildPacket(NetMsgType.MeatSync, w =>
            {
                w.Write(meatId);
                w.Write(pos.x);
                w.Write(pos.y);
                w.Write(pos.z);
                w.Write(rot.x);
                w.Write(rot.y);
                w.Write(rot.z);
                w.Write(rot.w);
                w.Write(doneness);
                w.Write(isBurnt);
            });

            SafeWriteLocalClient(packet);
        }

        private void ApplyMeatSync(int meatId, Vector3 pos, Quaternion rot, float doneness, bool isBurnt)
        {
#pragma warning disable CS0618
            var meats = UnityEngine.Object.FindObjectsByType<Cooking.Meat>(FindObjectsInactive.Exclude);
            foreach (var m in meats)
            {
                if (m.gameObject.GetInstanceID() == meatId || m.name.Contains(meatId.ToString()))
                {
                    m.transform.position = Vector3.Lerp(m.transform.position, pos, 0.5f);
                    m.transform.rotation = Quaternion.Slerp(m.transform.rotation, rot, 0.5f);
                    break;
                }
            }
#pragma warning restore CS0618
        }

        #endregion

        #region 4. 远程玩家化身 3D 渲染与管理

        private void UpdateRemotePlayerAvatar(int playerId, Vector3 pos, float rotY, bool isCrouching)
        {
            if (playerId <= 0 || playerId == _localPlayerId) return;

            if (!_remotePlayers.TryGetValue(playerId, out var avatar) || avatar == null)
            {
                avatar = CreateRemotePlayerAvatar(playerId, pos, rotY);
                _remotePlayers[playerId] = avatar;
            }

            if (avatar != null)
            {
                avatar.SetTarget(pos, rotY, isCrouching);
            }
        }

        private void RemoveRemotePlayerAvatar(int playerId)
        {
            if (_remotePlayers.TryGetValue(playerId, out var avatar))
            {
                if (avatar != null && avatar.gameObject != null)
                {
                    Destroy(avatar.gameObject);
                }
                _remotePlayers.Remove(playerId);
            }
        }

        private RemotePlayerAvatar CreateRemotePlayerAvatar(int playerId, Vector3 initialPos, float rotY)
        {
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            obj.name = $"[RemotePlayer_{playerId}]";
            obj.transform.position = initialPos;
            obj.transform.rotation = Quaternion.Euler(0f, rotY, 0f);

            // 移除碰撞体，防止与本地玩家物理挤压冲突
            var collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            // 鲜亮烤肉主厨浅蓝材质 (安全获取 URP/Lit 或已有材质，杜绝粉红/不可见错误)
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = null;
                var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    mat = new Material(urpShader);
                }
                else if (renderer.sharedMaterial != null)
                {
                    mat = new Material(renderer.sharedMaterial);
                }
                else
                {
                    mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                }

                mat.color = new Color(0.18f, 0.65f, 0.98f, 1f);
                renderer.material = mat;
            }

            // 头顶 3D 名字公告板
            GameObject nameObj = new GameObject("Nameplate");
            nameObj.transform.SetParent(obj.transform, false);
            nameObj.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            var tmp = nameObj.AddComponent<TextMeshPro>();
            var font = ModernUIBuilder.GetChineseFontAsset();
            if (font != null) tmp.font = font;
            tmp.fontSize = 4.2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            var avatar = obj.AddComponent<RemotePlayerAvatar>();
            avatar.Initialize(nameObj.transform, playerId, tmp, initialPos, rotY);
            return avatar;
        }

        #endregion

        #region 5. 辅助方法与协议编码

        private static byte[] BuildPacket(NetMsgType type, Action<BinaryWriter> writePayload)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(0); // 长度占位符
                    writer.Write((byte)type); // 消息类型
                    writePayload?.Invoke(writer);

                    int length = (int)stream.Length - 5;
                    stream.Position = 0;
                    writer.Write(length); // 回写准确 Payload 长度
                    return stream.ToArray();
                }
            }
        }

        private static async Task<int> ReadExactBytesAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, token);
                if (read <= 0) return totalRead;
                totalRead += read;
            }
            return totalRead;
        }

        #endregion
    }

    /// <summary>
    /// 网络传输消息包类型
    /// </summary>
    public enum NetMsgType : byte
    {
        Ping = 1,
        Pong = 2,
        Welcome = 3,
        PlayerTransform = 4,
        PlayerLeave = 5,
        MeatSync = 6
    }

    /// <summary>
    /// 远程联机玩家平滑插值运动组件
    /// </summary>
    public class RemotePlayerAvatar : MonoBehaviour
    {
        private Vector3 _targetPos;
        private Quaternion _targetRot;
        private bool _isCrouching = false;
        private Transform _nameplateTransform;
        private int _playerId;
        private TextMeshPro _nameplateTmp;

        public void Initialize(Transform nameplate, int playerId, TextMeshPro tmp, Vector3 startPos, float startYaw)
        {
            _nameplateTransform = nameplate;
            _playerId = playerId;
            _nameplateTmp = tmp;
            _targetPos = startPos;
            _targetRot = Quaternion.Euler(0f, startYaw, 0f);
            transform.position = startPos;
            transform.rotation = _targetRot;

            LanguageManager.OnLanguageChanged += HandleLanguageChanged;
            UpdateNameplateText(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= HandleLanguageChanged;
        }

        private void HandleLanguageChanged(GameLanguage lang)
        {
            UpdateNameplateText(lang);
        }

        private void UpdateNameplateText(GameLanguage lang)
        {
            if (_nameplateTmp != null)
            {
                string roleTag = LanguageManager.T("player_nameplate");
                _nameplateTmp.text = $"<color=#F1C40F>● {roleTag} #{_playerId}</color>";
            }
        }

        public void SetTarget(Vector3 pos, float yaw, bool isCrouch)
        {
            _targetPos = pos;
            _targetRot = Quaternion.Euler(0f, yaw, 0f);
            _isCrouching = isCrouch;
        }

        private void Update()
        {
            // 位置与旋转平滑插值 (Lerp)
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 18f);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * 18f);

            // 下蹲身体缩放
            Vector3 targetScale = _isCrouching ? new Vector3(1f, 0.65f, 1f) : Vector3.one;
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 12f);

            // 头顶名字标签面向主相机保持水平正对 (Billboard)
            if (_nameplateTransform != null && Camera.main != null)
            {
                _nameplateTransform.rotation = Camera.main.transform.rotation;
            }
        }
    }
}
