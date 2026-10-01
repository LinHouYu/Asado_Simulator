using System;
using UnityEngine;

namespace AsadoSimulator.Multiplayer
{
    public enum GameSessionState
    {
        SinglePlayer,
        MultiplayerHost,
        MultiplayerClient
    }

    /// <summary>
    /// 全局网络与单机状态协调器 (NetworkGameManager)
    /// 功能：
    /// 1. 监测当前是单机还是联机状态
    /// 2. 协调暂停时间缩放 (Time.timeScale)：单机支持完全静止暂停，联机时保持世界时间正常流逝
    /// 3. 管理玩家网络生成点与连接状态广播
    /// </summary>
    public class NetworkGameManager : MonoBehaviour
    {
        public static NetworkGameManager Instance { get; private set; }

        public GameSessionState CurrentSessionState
        {
            get
            {
                if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
                {
                    return AsadoNetworkManager.Instance.IsHost ? GameSessionState.MultiplayerHost : GameSessionState.MultiplayerClient;
                }
                return GameSessionState.SinglePlayer;
            }
        }

        public bool IsMultiplayer => CurrentSessionState != GameSessionState.SinglePlayer;

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

        /// <summary>
        /// 设置游戏暂停状态
        /// </summary>
        /// <param name="isPaused">是否暂停</param>
        public void SetPauseState(bool isPaused)
        {
            if (isPaused)
            {
                // 单机模式下完全冻结时间；联机模式下时间必须保持为 1.0f 正常运行！
                Time.timeScale = IsMultiplayer ? 1.0f : 0.0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Time.timeScale = 1.0f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
