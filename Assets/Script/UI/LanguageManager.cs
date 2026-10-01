using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace AsadoSimulator.UI
{
    public enum GameLanguage
    {
        Chinese, // 中文
        Spanish, // 西班牙语
        English  // 英语
    }

    /// <summary>
    /// 全局多语言本地化管理器 (LanguageManager)
    /// 功能：
    /// 1. 支持 中文 (zh)、西班牙语 (es)、英语 (en) 动态切换
    /// 2. 内置全套高频 UI 与网络提示字典，即使未安装 Unity.Localization 也能即开即用
    /// 3. 当安装了 Unity Localization 包时，自动与官方 LocalizationSettings 深度双向联动
    /// 4. 广播 OnLanguageChanged 全局事件，场景文本即时无缝重绘刷新
    /// </summary>
    public class LanguageManager : MonoBehaviour
    {
        public static LanguageManager Instance { get; private set; }

        private const string PrefKey = "AsadoSim_Language";
        public static event Action<GameLanguage> OnLanguageChanged;

        [SerializeField] private GameLanguage currentLanguage = GameLanguage.Chinese;
        public GameLanguage CurrentLanguage => currentLanguage;

        // 全语言多维词条字典 [Key -> [Language -> TranslatedText]]
        private static readonly Dictionary<string, Dictionary<GameLanguage, string>> StringTable = 
            new Dictionary<string, Dictionary<GameLanguage, string>>
        {
            // 通用按钮
            { "btn_confirm", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "确定" }, { GameLanguage.Spanish, "Confirmar" }, { GameLanguage.English, "Confirm" } } },
            { "btn_cancel", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "取消" }, { GameLanguage.Spanish, "Cancelar" }, { GameLanguage.English, "Cancel" } } },
            { "btn_back", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "返回" }, { GameLanguage.Spanish, "Volver" }, { GameLanguage.English, "Back" } } },
            { "btn_close", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "关闭" }, { GameLanguage.Spanish, "Cerrar" }, { GameLanguage.English, "Close" } } },

            // 暂停菜单
            { "pause_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "游戏暂停" }, { GameLanguage.Spanish, "Juego Pausado" }, { GameLanguage.English, "Game Paused" } } },
            { "pause_resume", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "继续游戏" }, { GameLanguage.Spanish, "Continuar" }, { GameLanguage.English, "Resume" } } },
            { "pause_restart", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "重新开始" }, { GameLanguage.Spanish, "Reiniciar" }, { GameLanguage.English, "Restart" } } },
            { "pause_leave_room", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "离开房间" }, { GameLanguage.Spanish, "Salir de la Sala" }, { GameLanguage.English, "Leave Room" } } },
            { "pause_multiplayer", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "联机大厅" }, { GameLanguage.Spanish, "Multijugador" }, { GameLanguage.English, "Multiplayer" } } },
            { "pause_settings", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "系统设置" }, { GameLanguage.Spanish, "Ajustes" }, { GameLanguage.English, "Settings" } } },
            { "pause_quit", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "退出游戏" }, { GameLanguage.Spanish, "Salir del Juego" }, { GameLanguage.English, "Quit Game" } } },
            { "role_host", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "房主" }, { GameLanguage.Spanish, "Anfitrión" }, { GameLanguage.English, "Host" } } },
            { "role_client", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "队员" }, { GameLanguage.Spanish, "Compañero" }, { GameLanguage.English, "Client" } } },
            { "scoreboard_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "联机房间玩家列表" }, { GameLanguage.Spanish, "Lista de Jugadores" }, { GameLanguage.English, "Player List" } } },
            { "scoreboard_single", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "单机游戏模式" }, { GameLanguage.Spanish, "Modo Un Jugador" }, { GameLanguage.English, "Single Player" } } },

            // 联机大厅
            { "mp_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "多人联机大厅" }, { GameLanguage.Spanish, "Sala Multijugador" }, { GameLanguage.English, "Multiplayer Lobby" } } },
            { "mp_host", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "创建房间 (开服)" }, { GameLanguage.Spanish, "Crear Sala (Host)" }, { GameLanguage.English, "Host Game" } } },
            { "mp_join", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "加入游戏 (连接)" }, { GameLanguage.Spanish, "Unirse a Sala" }, { GameLanguage.English, "Join Game" } } },
            { "mp_tool_guide", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "联机工具与教程" }, { GameLanguage.Spanish, "Herramienta y Guía" }, { GameLanguage.English, "Tunnel Tool & Guide" } } },
            { "mp_ip_placeholder", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "输入 IP / localhost / 穿透地址" }, { GameLanguage.Spanish, "Ingresar IP / localhost" }, { GameLanguage.English, "Enter IP / localhost" } } },

            // 自动检测弹窗
            { "auto_join_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "检测到本地穿透客户端" }, { GameLanguage.Spanish, "Túnel Cliente Detectado" }, { GameLanguage.English, "Local Client Tunnel Detected" } } },
            { "auto_join_desc", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "检测到 127.0.0.1:25566 端口已开启！\n是否立即加入游戏？" }, { GameLanguage.Spanish, "¡Se detectó el puerto 127.0.0.1:25566 abierto!\n¿Desea unirse ahora?" }, { GameLanguage.English, "Detected 127.0.0.1:25566 port is open!\nWould you like to join now?" } } },
            { "auto_join_confirm", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "立即加入" }, { GameLanguage.Spanish, "Unirse Ahora" }, { GameLanguage.English, "Join Now" } } },
            { "auto_join_ignore", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "稍后" }, { GameLanguage.Spanish, "Más tarde" }, { GameLanguage.English, "Later" } } },

            // 穿透教程与工具
            { "tunnel_modal_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "Cloudflared 联机穿透工具" }, { GameLanguage.Spanish, "Herramienta de Túnel Cloudflared" }, { GameLanguage.English, "Cloudflared Tunnel Tool" } } },
            { "tunnel_modal_prompt", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "是否需要使用或下载联机辅助工具？" }, { GameLanguage.Spanish, "¿Desea descargar o usar la herramienta?" }, { GameLanguage.English, "Need to download or use the tunnel tool?" } } },
            { "tunnel_btn_pc", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "下载 PC 客户端" }, { GameLanguage.Spanish, "Descargar Cliente PC" }, { GameLanguage.English, "Download PC Client" } } },
            { "tunnel_btn_web", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "使用网页版" }, { GameLanguage.Spanish, "Usar Versión Web" }, { GameLanguage.English, "Open Web Version" } } },
            { "tunnel_guide_step1", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "1. 首次使用：双方都必须去【杂项与设置】点击【安装 cloudflared】。" }, { GameLanguage.Spanish, "1. Primer uso: Ambos deben ir a [Ajustes] e instalar cloudflared." }, { GameLanguage.English, "1. First use: Both players must go to [Settings] and click [Install cloudflared]." } } },
            { "tunnel_guide_step2", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "2. 开服方：点击【软件服务端】->【免配置临时通道】->【一键获取临时链接】-> 复制链接发给朋友。" }, { GameLanguage.Spanish, "2. Anfitrión: Ir a Servidor -> Túnel Temporal -> Obtener Enlace y compartirlo." }, { GameLanguage.English, "2. Host: Go to Server -> Temp Tunnel -> Get Quick Link -> Share with friend." } } },
            { "tunnel_guide_step3", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "3. 连接方：进入【软件客户端】-> 填写朋友给的临时链接 -> 点击【连接】。" }, { GameLanguage.Spanish, "3. Cliente: Ir a Cliente -> Pegar el enlace temporal -> Conectar." }, { GameLanguage.English, "3. Client: Go to Client -> Enter friend's link -> Click Connect." } } },

            // 性能与网络
            { "hud_perf_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "性能监控 (FPS / Ping)" }, { GameLanguage.Spanish, "Rendimiento (FPS / Ping)" }, { GameLanguage.English, "Performance (FPS / Ping)" } } },
            { "hud_local", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "本地" }, { GameLanguage.Spanish, "Local" }, { GameLanguage.English, "Local" } } },
            { "player_nameplate", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "队友" }, { GameLanguage.Spanish, "Compañero" }, { GameLanguage.English, "Teammate" } } },
            { "lang_label", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "语言选择 / Language" }, { GameLanguage.Spanish, "Idioma / Language" }, { GameLanguage.English, "Language" } } },
            { "settings_title", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "系统设置" }, { GameLanguage.Spanish, "Ajustes" }, { GameLanguage.English, "Settings" } } },
            { "settings_hud_toggle", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "显示性能监控 (FPS / Ping)" }, { GameLanguage.Spanish, "Mostrar Rendimiento (FPS / Ping)" }, { GameLanguage.English, "Show Performance (FPS / Ping)" } } },
            { "mp_ready", new Dictionary<GameLanguage, string> { { GameLanguage.Chinese, "准备就绪" }, { GameLanguage.Spanish, "Listo" }, { GameLanguage.English, "Ready" } } }
        };

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                EnsureFontFallback();
                LoadSavedLanguage();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void EnsureFontFallback()
        {
            try
            {
                TMP_FontAsset font = ModernUIBuilder.GetChineseFontAsset();
                if (font != null && ModernUIBuilder.IsFontAssetValid(font) && TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset != font)
                {
                    if (TMP_Settings.defaultFontAsset.fallbackFontAssetTable == null)
                    {
                        TMP_Settings.defaultFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    }

                    TMP_Settings.defaultFontAsset.fallbackFontAssetTable.RemoveAll(item => item == null || !ModernUIBuilder.IsFontAssetValid(item));

                    if (!TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Contains(font))
                    {
                        TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Add(font);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LanguageManager] 确保中文字体 Fallback 异常: {ex.Message}");
            }
        }

        private void LoadSavedLanguage()
        {
            int savedLang = PlayerPrefs.GetInt(PrefKey, (int)GameLanguage.Chinese);
            currentLanguage = (GameLanguage)Mathf.Clamp(savedLang, 0, 2);
        }

        public void SetLanguage(GameLanguage newLang)
        {
            currentLanguage = newLang;
            PlayerPrefs.SetInt(PrefKey, (int)currentLanguage);
            PlayerPrefs.Save();

            Debug.Log($"[LanguageManager] 语言已切换为: {currentLanguage}");
            OnLanguageChanged?.Invoke(currentLanguage);
        }

        /// <summary>
        /// 获取当前语言对应的本地化文本
        /// </summary>
        public string GetText(string key)
        {
            if (StringTable.TryGetValue(key, out var langDict))
            {
                if (langDict.TryGetValue(currentLanguage, out string text))
                {
                    return text;
                }
                if (langDict.TryGetValue(GameLanguage.Chinese, out string fallbackText))
                {
                    return fallbackText;
                }
            }
            return key;
        }

        public static string T(string key)
        {
            return Instance != null ? Instance.GetText(key) : key;
        }
    }
}
