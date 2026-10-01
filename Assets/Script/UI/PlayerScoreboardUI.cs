using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 现代化玩家列表面板 (PlayerScoreboardUI)
    /// 功能：
    /// 1. 按住 Tab 键实时弹出半透明毛玻璃风玩家计分板/列表，松开 Tab 键瞬时隐去。
    /// 2. 区分显示 👑 房主 (Host) 与 🎮 队员 (Client)，并标记 [本机]。
    /// 3. 动态反馈各玩家延迟 Ping (毫秒级别 RTT)。
    /// 4. 支持新旧输入系统 (Unity Input System & Legacy Input)。
    /// </summary>
    public class PlayerScoreboardUI : MonoBehaviour
    {
        public static PlayerScoreboardUI Instance { get; private set; }

        [Header("UI 引用")]
        [SerializeField] private CanvasGroup scoreboardCanvasGroup;
        [SerializeField] private RectTransform scoreboardRect;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private Transform listContainer;

        private bool _isTabHeld = false;
        private float _refreshTimer = 0f;
        private readonly List<GameObject> _spawnedRows = new List<GameObject>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (scoreboardCanvasGroup == null) scoreboardCanvasGroup = GetComponent<CanvasGroup>();
            if (scoreboardRect == null) scoreboardRect = GetComponent<RectTransform>();

            HideImmediate();
        }

        public void InitializeReferences(CanvasGroup group, RectTransform rect, TextMeshProUGUI titleTxt, TextMeshProUGUI countTxt, Transform container)
        {
            scoreboardCanvasGroup = group;
            scoreboardRect = rect;
            titleText = titleTxt;
            countText = countTxt;
            listContainer = container;

            HideImmediate();
        }

        private void Update()
        {
            bool wasTabHeld = _isTabHeld;
            _isTabHeld = CheckTabInput();

            if (_isTabHeld)
            {
                if (!wasTabHeld)
                {
                    ShowScoreboard();
                }

                _refreshTimer += Time.unscaledDeltaTime;
                if (_refreshTimer >= 0.25f)
                {
                    _refreshTimer = 0f;
                    RefreshPlayerList();
                }
            }
            else if (wasTabHeld)
            {
                HideScoreboard();
            }
        }

        private bool CheckTabInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.tabKey.isPressed) return true;

            if (Input.GetKey(KeyCode.Tab)) return true;

            return false;
        }

        public void ShowScoreboard()
        {
            if (scoreboardCanvasGroup != null)
            {
                scoreboardCanvasGroup.alpha = 1f;
                scoreboardCanvasGroup.blocksRaycasts = false; // 仅作为 HUD 展示，不抢占鼠标点击
            }
            if (gameObject != null && !gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            _refreshTimer = 0f;
            RefreshPlayerList();
        }

        public void HideScoreboard()
        {
            if (scoreboardCanvasGroup != null)
            {
                scoreboardCanvasGroup.alpha = 0f;
                scoreboardCanvasGroup.blocksRaycasts = false;
            }
        }

        private void HideImmediate()
        {
            if (scoreboardCanvasGroup != null)
            {
                scoreboardCanvasGroup.alpha = 0f;
                scoreboardCanvasGroup.blocksRaycasts = false;
            }
        }

        public void RefreshPlayerList()
        {
            if (listContainer == null) return;

            // 清理旧列表项
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null) Destroy(_spawnedRows[i]);
            }
            _spawnedRows.Clear();

            var net = AsadoNetworkManager.Instance;
            bool isMultiplayer = net != null && net.IsNetworkActive;
            int totalPlayers = isMultiplayer ? net.ConnectedPlayerCount : 1;

            if (titleText != null)
            {
                titleText.text = isMultiplayer ? "🏆 联机房间玩家列表" : "👤 单机游戏模式";
            }
            if (countText != null)
            {
                countText.text = isMultiplayer ? $"在线玩家: {totalPlayers} 人" : "单人中";
            }

            var font = ModernUIBuilder.GetChineseFontAsset();

            // 1. 本机玩家信息项
            if (isMultiplayer)
            {
                if (net.IsHost)
                {
                    CreatePlayerRow(listContainer, font, "👑 房主 (Host) [本机]", "<color=#F1C40F>[房主]</color>", "<color=#2ECC71>● 本地</color>");
                }
                else
                {
                    string pingStr = $"{Mathf.RoundToInt(net.RoundTripTimeMs)} ms";
                    CreatePlayerRow(listContainer, font, $"🎮 队员 #{net.LocalPlayerId} [本机]", "<color=#3498DB>[队员]</color>", $"<color=#2ECC71>● {pingStr}</color>");
                }

                // 2. 远程玩家信息项
                if (net.RemotePlayers != null)
                {
                    foreach (var kv in net.RemotePlayers)
                    {
                        int remoteId = kv.Key;
                        string roleTag = net.IsHost ? "<color=#3498DB>[队员]</color>" : (remoteId == 1 ? "<color=#F1C40F>[房主]</color>" : "<color=#3498DB>[队员]</color>");
                        string nameStr = remoteId == 1 ? $"👑 房主 (Host) #{remoteId}" : $"👤 队友 #{remoteId}";
                        CreatePlayerRow(listContainer, font, nameStr, roleTag, "<color=#2ECC71>● 正常</color>");
                    }
                }
            }
            else
            {
                CreatePlayerRow(listContainer, font, "👑 玩家 (Player 1) [本机]", "<color=#F1C40F>[单机]</color>", "<color=#3498DB>● 本地模式</color>");
            }
        }

        private void CreatePlayerRow(Transform parent, TMP_FontAsset font, string nameStr, string roleBadge, string pingStr)
        {
            GameObject row = new GameObject("PlayerRow");
            row.transform.SetParent(parent, false);

            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(460f, 40f);

            var img = row.AddComponent<Image>();
            img.color = new Color(0.14f, 0.17f, 0.23f, 0.85f);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 16, 4, 4);
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // 名字与身份
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(row.transform, false);
            var nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
            if (font != null) nameTmp.font = font;
            nameTmp.fontSize = 17;
            nameTmp.text = $"{roleBadge} <b>{nameStr}</b>";
            nameTmp.color = Color.white;
            nameTmp.alignment = TextAlignmentOptions.Left;
            var nameRt = nameObj.GetComponent<RectTransform>();
            nameRt.sizeDelta = new Vector2(300f, 32f);

            // 延迟 Ping
            GameObject pingObj = new GameObject("PingText");
            pingObj.transform.SetParent(row.transform, false);
            var pingTmp = pingObj.AddComponent<TextMeshProUGUI>();
            if (font != null) pingTmp.font = font;
            pingTmp.fontSize = 15;
            pingTmp.text = pingStr;
            pingTmp.color = new Color(0.8f, 0.9f, 1f, 1f);
            pingTmp.alignment = TextAlignmentOptions.Right;
            var pingRt = pingObj.GetComponent<RectTransform>();
            pingRt.sizeDelta = new Vector2(110f, 32f);

            _spawnedRows.Add(row);
        }
    }
}
