using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsadoSimulator.Multiplayer
{
    /// <summary>
    /// 网络物理对象同步组件 (NetworkSyncObject)
    /// 自动为场景中的食材、木炭、烹饪用具等可抓取物理对象提供全局唯一的网络标识，
    /// 并在玩家抓取、放置、位移时在多台设备间高精度无缝同步。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkSyncObject : MonoBehaviour
    {
        [Tooltip("全局唯一网络同步 ID")]
        [SerializeField] private int syncId = 0;
        public int SyncId
        {
            get => syncId;
            set
            {
                if (syncId != value)
                {
                    if (syncId != 0) Registry.Remove(syncId);
                    syncId = value;
                    if (syncId != 0) Registry[syncId] = this;
                }
            }
        }

        public static readonly Dictionary<int, NetworkSyncObject> Registry = new Dictionary<int, NetworkSyncObject>();
        private static int _nextDynamicId = 2000;

        public static int GetNextDynamicId()
        {
            return _nextDynamicId++;
        }

        public static bool TryGet(int id, out NetworkSyncObject syncObj)
        {
            return Registry.TryGetValue(id, out syncObj) && syncObj != null;
        }

        private Rigidbody _rigidbody;
        public Rigidbody Rigidbody => _rigidbody;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (syncId == 0)
            {
                // 基于初始世界坐标与名称生成确定性 Hash，确保场景预置物体在双方客户端具有相同 SyncId
                int hash = Mathf.Abs((int)(transform.position.x * 100f + transform.position.z * 1000f + gameObject.name.GetHashCode())) % 1000 + 1;
                syncId = hash;
            }

            Registry[syncId] = this;
        }

        private void OnDestroy()
        {
            if (Registry.ContainsKey(syncId) && Registry[syncId] == this)
            {
                Registry.Remove(syncId);
            }
        }

        /// <summary>
        /// 玩家拿起物体时通知网络管理器广播
        /// </summary>
        public static void NotifyGrab(GameObject obj)
        {
            if (obj == null) return;
            var sync = obj.GetComponent<NetworkSyncObject>() ?? obj.AddComponent<NetworkSyncObject>();
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                AsadoNetworkManager.Instance.BroadcastObjectGrab(sync.SyncId);
            }
        }

        /// <summary>
        /// 玩家释放/扔出物体时通知网络管理器广播
        /// </summary>
        public static void NotifyDrop(GameObject obj, Vector3 releaseVelocity)
        {
            if (obj == null) return;
            var sync = obj.GetComponent<NetworkSyncObject>() ?? obj.AddComponent<NetworkSyncObject>();
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                AsadoNetworkManager.Instance.BroadcastObjectDrop(sync.SyncId, obj.transform.position, obj.transform.rotation, releaseVelocity);
            }
        }
    }
}
