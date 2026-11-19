using System;
using System.Collections.Generic;
using PoliticalMayhem.AI;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using PoliticalMayhem.Networking;
using PoliticalMayhem.Rally;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.UI
{
    /// <summary>
    /// High-contrast, opaque strategy HUD with:
    /// 1) Main Menu / Match Setup Screen
    /// 2) Unobstructed 3D map view with Top Status Bar, Compact Bottom Phase Workbench,
    ///    and collapsible Left (Cities/Overall/Units) & Right (AI Commander MCP+RAG+HITL) Drawers
    /// 3) Election Night & Balcony Victory Summary Screen
    /// </summary>
    public class PoliticalMayhemHUD : MonoBehaviour
    {
        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private UnitController _unitController;
        [SerializeField] private RallyController _rallyController;
        [SerializeField] private SabotageAndRevolverController _sabotageController;
        [SerializeField] private CrowdReactionController _crowdReaction;
        [SerializeField] private ElectionNightController _electionController;
        [SerializeField] private PhotonFusionAuthority _authority;
        [SerializeField] private AiGatewayClient _gatewayClient;
        [SerializeField] private AiCommanderPanel _commanderPanel;
        [SerializeField] private RtsCameraController _cameraController;

        // Screen & Drawer State
        private bool _hudVisible = true;
        private bool _showStartMenu = true;
        private bool _showHelpModal = false;
        private bool _showCitiesDrawer = false;
        private bool _showAiDrawer = false;

        private const string DefaultSpeechDraft = "Üretim, istihdam ve özgürlük temelinde herkes için güçlü bir yarın kuracağız!";
        private const string DefaultFinalSpeechA = "Şehrimize şeffaflık, refah ve güçlü gelecek getiriyoruz!";
        private const string DefaultFinalSpeechB = "Boş sözlere değil, somut emek ve üretime oy verin!";

        // Gameplay Draft Inputs
        private CityId _deployTargetCity = CityId.Baskent;
        private string _speechDraft = DefaultSpeechDraft;
        private bool _clearFocusNextGui;
        private string _verbalSabotageDraft = "Bu vaatlerin bütçe karşılığı yok; rakamlar halkı yanıltıyor!";
        private string _finalSpeechDraftA = DefaultFinalSpeechA;
        private string _finalSpeechDraftB = DefaultFinalSpeechB;
        private string _balconyDraft = "Bu zafer tüm şehirlerimizin ve halkımızın ortak zaferidir! Yaşasın demokrasi!";
        private string _bribeAmountInput = "1000";
        private string _actionFeedback = string.Empty;

        private int _leftTabIndex = 0; // 0 = Birimler, 1 = Şehirler
        private int _aiTabIndex = 0; // 0 = Plan & Araçlar, 1 = Doğrulama, 2 = Güvenli Durum JSON
        private Vector2 _leftScroll;
        private Vector2 _bottomScroll;
        private Vector2 _rightScroll;

        private readonly List<Rect> _hudBlockingRects = new List<Rect>();
        private float _currentUiScale = 1f;

        // Opaque Texture & Styles
        private Texture2D _whiteTex;
        private GUIStyle _titleStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _cardStyle;
        private bool _stylesReady;

        // Palette
        private static readonly Color BgDark = new Color(0.07f, 0.09f, 0.14f, 0.96f);
        private static readonly Color BgCard = new Color(0.11f, 0.15f, 0.22f, 0.97f);
        private static readonly Color BgCardInner = new Color(0.16f, 0.21f, 0.30f, 0.98f);
        private static readonly Color BorderSubtle = new Color(0.28f, 0.36f, 0.48f, 1f);
        private static readonly Color AccentGold = new Color(0.98f, 0.80f, 0.28f, 1f);
        private static readonly Color BtnGold = new Color(0.72f, 0.50f, 0.08f, 1f);
        private static readonly Color AccentCyan = new Color(0.45f, 0.88f, 0.98f, 1f);
        private static readonly Color AccentBlue = new Color(0.22f, 0.52f, 0.96f, 1f);
        private static readonly Color AccentRed = new Color(0.88f, 0.24f, 0.26f, 1f);
        private static readonly Color AccentGreen = new Color(0.18f, 0.68f, 0.44f, 1f);
        private static readonly Color BtnSlate = new Color(0.20f, 0.26f, 0.36f, 1f);
        private static readonly Color BtnHover = new Color(0.28f, 0.36f, 0.49f, 1f);

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            if (_unitController != null)
                _unitController.IsScreenPointOverHud = IsScreenPointOverHud;

            // Pause round timer while user is reading the Start Menu
            SetTimersPaused(true);
        }

        private void Update()
        {
            if (InputCompat.WasKeyPressedThisFrame(KeyCode.F1))
                _hudVisible = !_hudVisible;

            if (InputCompat.GetMouseButtonDown(0) && !IsScreenPointOverHud(InputCompat.MousePosition))
                _clearFocusNextGui = true;

            if (_rallyController != null && _rallyController.TryConsumeLatestMicTranscript(out string micText))
            {
                _speechDraft = string.IsNullOrWhiteSpace(_speechDraft) || _speechDraft == DefaultSpeechDraft
                    ? micText
                    : $"{_speechDraft.TrimEnd()} {micText}";
                _rallyController.SetLiveSpeakerTranscript(_speechDraft);
            }
        }

        private void ResolveReferences()
        {
            if (_gameManager == null) _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_unitController == null) _unitController = FindAnyObjectByType<UnitController>();
            if (_rallyController == null) _rallyController = FindAnyObjectByType<RallyController>();
            if (_sabotageController == null) _sabotageController = FindAnyObjectByType<SabotageAndRevolverController>();
            if (_crowdReaction == null) _crowdReaction = FindAnyObjectByType<CrowdReactionController>();
            if (_electionController == null) _electionController = FindAnyObjectByType<ElectionNightController>();
            if (_authority == null) _authority = FindAnyObjectByType<PhotonFusionAuthority>();
            if (_gatewayClient == null) _gatewayClient = FindAnyObjectByType<AiGatewayClient>();
            if (_commanderPanel == null) _commanderPanel = FindAnyObjectByType<AiCommanderPanel>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<RtsCameraController>();
        }

        private void SetTimersPaused(bool paused)
        {
            if (_rallyController != null) _rallyController.PauseTimers = paused;
            if (_electionController != null) _electionController.PauseTimers = paused;
        }

        private bool AreTimersPaused()
        {
            if (_electionController != null && _electionController.IsElectionPhase())
                return _electionController.PauseTimers;
            return _rallyController != null && _rallyController.PauseTimers;
        }

        public bool IsScreenPointOverHud(Vector2 bottomLeftScreenPos)
        {
            if (!_hudVisible) return false;
            float scale = Mathf.Max(0.01f, _currentUiScale);
            Vector2 guiPos = new Vector2(
                bottomLeftScreenPos.x / scale,
                (Screen.height - bottomLeftScreenPos.y) / scale);
            for (int i = 0; i < _hudBlockingRects.Count; i++)
            {
                if (_hudBlockingRects[i].Contains(guiPos))
                    return true;
            }
            return false;
        }

        public bool IsPointerOverHud(Vector3 screenPos)
        {
            return IsScreenPointOverHud(new Vector2(screenPos.x, screenPos.y));
        }

        private void EnsureStyles()
        {
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
                _stylesReady = false;
            }

            if (_stylesReady) return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 16,
                wordWrap = true
            };
            _titleStyle.normal.textColor = AccentGold;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                wordWrap = true
            };
            _headerStyle.normal.textColor = AccentGold;

            _subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                wordWrap = true
            };
            _subHeaderStyle.normal.textColor = AccentCyan;

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true
            };
            _bodyStyle.normal.textColor = new Color(0.95f, 0.97f, 1.0f, 1f);

            _mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
            _mutedStyle.normal.textColor = new Color(0.72f, 0.78f, 0.88f, 1f);

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(8, 8, 5, 5)
            };
            _buttonStyle.normal.background = _whiteTex;
            _buttonStyle.hover.background = _whiteTex;
            _buttonStyle.active.background = _whiteTex;
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = Color.white;
            _buttonStyle.active.textColor = Color.white;

            _textFieldStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(6, 6, 5, 5)
            };
            _textFieldStyle.normal.background = _whiteTex;
            _textFieldStyle.focused.background = _whiteTex;
            _textFieldStyle.normal.textColor = Color.white;
            _textFieldStyle.focused.textColor = Color.white;

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(2, 2, 2, 2)
            };
            _cardStyle.normal.background = _whiteTex;

            _stylesReady = true;
        }

        private void DrawSolidRect(Rect rect, Color bgColor, Color? borderColor = null, int borderWidth = 1)
        {
            Color prev = GUI.color;
            if (borderColor.HasValue && borderWidth > 0)
            {
                GUI.color = borderColor.Value;
                GUI.DrawTexture(rect, _whiteTex);
                Rect inner = new Rect(
                    rect.x + borderWidth,
                    rect.y + borderWidth,
                    Mathf.Max(0f, rect.width - borderWidth * 2),
                    Mathf.Max(0f, rect.height - borderWidth * 2));
                GUI.color = bgColor;
                GUI.DrawTexture(inner, _whiteTex);
            }
            else
            {
                GUI.color = bgColor;
                GUI.DrawTexture(rect, _whiteTex);
            }
            GUI.color = prev;
        }

        private bool SolidButton(string label, Color bgColor, params GUILayoutOption[] options)
        {
            Color prevBg = GUI.backgroundColor;
            Color prevCol = GUI.color;
            GUI.backgroundColor = bgColor;
            GUI.color = Color.white;
            bool clicked = GUILayout.Button(label, _buttonStyle, options);
            if (clicked)
            {
                GUIUtility.keyboardControl = 0;
                GameAudioManager.EnsureInstance().PlayUiClick();
            }
            GUI.backgroundColor = prevBg;
            GUI.color = prevCol;
            return clicked;
        }

        private string SolidTextArea(string value, params GUILayoutOption[] options)
        {
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 1f);
            string updated = GUILayout.TextArea(value ?? string.Empty, _textFieldStyle, options);
            GUI.backgroundColor = prevBg;
            return updated;
        }

        private string SolidTextField(string value, params GUILayoutOption[] options)
        {
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 1f);
            string updated = GUILayout.TextField(value ?? string.Empty, _textFieldStyle, options);
            GUI.backgroundColor = prevBg;
            return updated;
        }

        private void OnGUI()
        {
            ResolveReferences();
            if (_gameManager == null) return;
            EnsureStyles();
            if (_clearFocusNextGui && Event.current != null && Event.current.type == EventType.Layout)
            {
                GUIUtility.keyboardControl = 0;
                _clearFocusNextGui = false;
            }
            _hudBlockingRects.Clear();

            float uiScale = 1f;
            if (Screen.width < 1340f || Screen.height < 740f)
            {
                uiScale = Mathf.Clamp(Mathf.Min(Screen.width / 1340f, Screen.height / 740f), 0.62f, 1.0f);
            }
            _currentUiScale = uiScale;

            float w = Screen.width / uiScale;
            float h = Screen.height / uiScale;

            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            try
            {
                // Always reset global tint
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;

                if (!_hudVisible)
                {
                    Rect showBtn = new Rect(10, 10, 175, 32);
                    _hudBlockingRects.Add(showBtn);
                    DrawSolidRect(showBtn, BgDark, AccentGold, 1);
                    GUILayout.BeginArea(showBtn);
                    if (SolidButton("Arayüzü Göster (F1)", BtnSlate, GUILayout.ExpandHeight(true)))
                        _hudVisible = true;
                    GUILayout.EndArea();
                    return;
                }

                if (_showStartMenu)
                {
                    DrawStartMenuOverlay(w, h);
                    return;
                }

                // 1. Top Header Bar (66px so Row 1 and Row 2 never clip)
                Rect topRect = new Rect(8, 8, w - 16, 66);
                _hudBlockingRects.Add(topRect);
                DrawTopBar(topRect);

                // 2. Optional Collapsible Left Drawer (Cities, Overall Bars & Units)
                float leftWidth = _showCitiesDrawer ? Mathf.Min(275f, w * 0.31f) : 0f;
                if (_showCitiesDrawer)
                {
                    Rect leftRect = new Rect(8, 80, leftWidth, h - 88);
                    _hudBlockingRects.Add(leftRect);
                    DrawLeftCitiesDrawer(leftRect);
                }

                // 3. Optional Collapsible Right Drawer (AI Strategic Commander)
                float rightWidth = _showAiDrawer ? Mathf.Min(335f, w * 0.37f) : 0f;
                if (_showAiDrawer)
                {
                    Rect rightRect = new Rect(w - rightWidth - 8, 80, rightWidth, h - 88);
                    _hudBlockingRects.Add(rightRect);
                    DrawRightAiDrawer(rightRect);
                }

                // 4. Optional Help Modal
                if (_showHelpModal)
                {
                    DrawHelpModal(w, h);
                    return;
                }

                // 5. Election Victory / Balcony Side Card (kept right-aligned so 3D Victory Balcony stays unobstructed!) OR Compact Bottom Phase Bar
                if (_gameManager.CurrentPhase == TurnPhase.BALCONY_SPEECH)
                {
                    float mw = Mathf.Min(360f, w * 0.36f);
                    float mh = Mathf.Min(380f, h - 104f);
                    Rect modalRect = new Rect(w - mw - 10f, 82f, mw, mh);
                    _hudBlockingRects.Add(modalRect);
                    DrawVictoryBalconyModal(modalRect);
                }
                else
                {
                    float startX = _showCitiesDrawer ? (8f + leftWidth + 8f) : Mathf.Max(8f, (w - Mathf.Min(720f, w - 16f)) * 0.5f);
                    float endX = _showAiDrawer ? (w - rightWidth - 16f) : Mathf.Min(w - 8f, startX + Mathf.Min(720f, w - 16f));
                    float bottomW = Mathf.Max(260f, endX - startX);
                    float desiredBottomH = _gameManager.CurrentPhase == TurnPhase.RALLY_SPEECH ? 162f : 104f;
                    float bottomH = Mathf.Min(desiredBottomH, h * 0.34f);
                    Rect bottomRect = new Rect(startX, h - bottomH - 8f, bottomW, bottomH);
                    _hudBlockingRects.Add(bottomRect);
                    DrawBottomPhaseBar(bottomRect);
                }
            }
            finally
            {
                GUI.matrix = prevMatrix;
            }
        }

        private void DrawStartMenuOverlay(float w, float h)
        {

            // Subtle dark backdrop so 3D cities remain visible underneath
            DrawSolidRect(new Rect(0, 0, w, h), new Color(0.03f, 0.05f, 0.09f, 0.55f));

            float cardW = Mathf.Min(540f, w - 32f);
            float cardH = Mathf.Min(430f, h - 32f);
            Rect cardRect = new Rect((w - cardW) * 0.5f, (h - cardH) * 0.5f, cardW, cardH);
            _hudBlockingRects.Add(cardRect);

            DrawSolidRect(cardRect, BgDark, AccentGold, 2);
            GUILayout.BeginArea(new Rect(cardRect.x + 18, cardRect.y + 14, cardRect.width - 36, cardRect.height - 28));

            GUILayout.Label("POLITICAL MAYHEM: Battle for the Cities", _titleStyle);
            GUILayout.Label("Yapay Zekâ Destekli Multiplayer Siyasi Strateji Oyunu (Unity 6 + Photon Fusion 2 + MCP + RAG)", _mutedStyle);
            GUILayout.Space(8);

            DrawCardSection(() =>
            {
                GUILayout.Label("OYUN AKIŞI (3 ADIMDA ZAFER)", _subHeaderStyle);
                GUILayout.Label("1. 6 Miting Turu: Şehir seç, konuşma/sabotaj yap, ideolojik Overall puanını geliştir ve parti bağışı biriktir.", _bodyStyle);
                GUILayout.Label("2. Gerçek Zamanlı Birimler & AI Komutan: Gönüllü, Propagandacı ve Korumaları yönet; MCP + RAG destekli AI'dan onaylı öneri al.", _bodyStyle);
                GUILayout.Label("3. Seçim Gecesi: Biriktirdiğin parayla kritik şehirlerde stratejik rüşvet kullan ve 6 şehrin çoğunluğunu kazan!", _bodyStyle);
            });

            GUILayout.Space(8);
            if (_commanderPanel != null)
            {
                GUILayout.Label("AI Komutan Sağlayıcısı:", _subHeaderStyle);
                GUILayout.BeginHorizontal();
                AiProviderMode mode = _commanderPanel.CurrentProviderMode;
                if (SolidButton("Otomatik", mode == AiProviderMode.Auto ? AccentBlue : BtnSlate))
                    _commanderPanel.SetProviderMode(AiProviderMode.Auto);
                if (SolidButton("Bulut AI (Hızlı)", mode == AiProviderMode.CloudApi ? AccentBlue : BtnSlate))
                    _commanderPanel.SetProviderMode(AiProviderMode.CloudApi);
                if (SolidButton("Yerel Ollama", mode == AiProviderMode.LocalOllama ? AccentBlue : BtnSlate))
                    _commanderPanel.SetProviderMode(AiProviderMode.LocalOllama);
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (SolidButton("MAÇA BAŞLA (Rakip Bot'a Karşı)", AccentGreen, GUILayout.Height(36)))
            {
                LocalOpponentBotController.EnsureInstance().BotEnabled = true;
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerAId);
                _showStartMenu = false;
                _showCitiesDrawer = true;
                SetTimersPaused(false);
            }
            if (SolidButton("2 Oyuncu (Manuel)", BtnHover, GUILayout.Width(160), GUILayout.Height(36)))
            {
                LocalOpponentBotController.EnsureInstance().BotEnabled = false;
                _showStartMenu = false;
                _showCitiesDrawer = true;
                SetTimersPaused(false);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            if (SolidButton("Photon Host Başlat", AccentBlue, GUILayout.Height(30)))
            {
                if (_authority != null) _authority.StartHost();
                _showStartMenu = false;
                _showCitiesDrawer = true;
                SetTimersPaused(false);
            }
            if (SolidButton("Photon Client Katıl", BtnSlate, GUILayout.Height(30)))
            {
                if (_authority != null) _authority.StartClient();
                _showStartMenu = false;
                _showCitiesDrawer = true;
                SetTimersPaused(false);
            }
            if (SolidButton("Kontroller", BtnSlate, GUILayout.Width(105), GUILayout.Height(30)))
            {
                _showStartMenu = false;
                _showHelpModal = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawTopBar(Rect rect)
        {
            DrawSolidRect(rect, BgDark, BorderSubtle, 1);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 4, rect.width - 16, rect.height - 8));

            PlayerState self = _gameManager.GetLocalPlayerState();
            float timer = (_electionController != null && _electionController.IsElectionPhase())
                ? _electionController.PhaseTimeRemaining
                : (_rallyController != null ? _rallyController.PhaseTimeRemaining : 0f);
            bool paused = AreTimersPaused();

            // Row 1: Round/Phase, Timer Controls, Perspective, Menu
            GUILayout.BeginHorizontal();
            string phaseTr = GetPhaseDisplayNameTr(_gameManager.CurrentPhase);
            string roundPrefix = _gameManager.CurrentPhase == TurnPhase.BALCONY_SPEECH
                ? "FİNAL"
                : (_gameManager.RoundNumber > 6
                    ? $"SEÇİM {Mathf.Clamp(_gameManager.RoundNumber - 6, 1, 6)}/6"
                    : $"TUR {_gameManager.RoundNumber}/6");
            GUILayout.Label($"{roundPrefix} | {phaseTr} ({Mathf.Max(0f, timer):F0} sn)", _headerStyle, GUILayout.Width(285));

            if (SolidButton(paused ? "Devam Et" : "Duraklat", paused ? AccentGreen : BtnSlate, GUILayout.Width(86), GUILayout.Height(25)))
                SetTimersPaused(!paused);

            if (SolidButton("Sonraki Aşama >>", AccentBlue, GUILayout.Width(134), GUILayout.Height(25)))
            {
                if (_electionController != null && _electionController.IsElectionPhase())
                    _electionController.ForceAdvanceElectionPhase();
                else if (_rallyController != null)
                    _rallyController.ForceAdvancePhase();
            }

            GUILayout.FlexibleSpace();

            LocalOpponentBotController botCtrl = LocalOpponentBotController.EnsureInstance();
            bool botOn = botCtrl != null && botCtrl.BotEnabled;
            if (SolidButton(botOn ? "Bot: Açık" : "Bot: Kapalı", botOn ? AccentGreen : BtnSlate, GUILayout.Width(86), GUILayout.Height(25)))
            {
                if (botCtrl != null) botCtrl.BotEnabled = !botCtrl.BotEnabled;
            }

            bool isA = _gameManager.LocalPlayerId == PlayerIdentity.PlayerAId;
            if (SolidButton("Oyuncu A", isA ? AccentBlue : BtnSlate, GUILayout.Width(78), GUILayout.Height(25)))
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerAId);
            if (SolidButton("Oyuncu B", !isA ? AccentRed : BtnSlate, GUILayout.Width(78), GUILayout.Height(25)))
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerBId);

            if (SolidButton("Menü", BtnSlate, GUILayout.Width(54), GUILayout.Height(25)))
            {
                _showStartMenu = true;
                SetTimersPaused(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // Row 2: Budget, Score, Role, Drawer Toggles (clean ASCII/Turkish, never clipped)
            GUILayout.BeginHorizontal();
            string roleTr = GetRoleDisplayNameTr(_gameManager.GetPlayerRole(_gameManager.LocalPlayerId));
            GUILayout.Label($"Bütçe: {self.partyBudget:N0} TL   |   Şehirler: A {_gameManager.PlayerA.citiesWon} - {_gameManager.PlayerB.citiesWon} B   |   Rol: {roleTr}", _bodyStyle);
            GUILayout.FlexibleSpace();

            if (SolidButton(_showCitiesDrawer ? "Şehirler [Açık]" : "Şehirler & Birimler", _showCitiesDrawer ? AccentBlue : BtnSlate, GUILayout.Width(138), GUILayout.Height(25)))
                _showCitiesDrawer = !_showCitiesDrawer;

            if (SolidButton(_showAiDrawer ? "AI Komutan [Açık]" : "AI Komutan", _showAiDrawer ? AccentGreen : BtnSlate, GUILayout.Width(128), GUILayout.Height(25)))
                _showAiDrawer = !_showAiDrawer;

            if (_cameraController != null)
            {
                bool freeCam = _cameraController.IsFreeCameraMode;
                if (SolidButton(freeCam ? "Serbest [C]" : "Harita [C]", freeCam ? BtnGold : BtnSlate, GUILayout.Width(86), GUILayout.Height(25)))
                    _cameraController.ToggleFreeCameraMode();
            }

            GameAudioManager audioMgr = GameAudioManager.EnsureInstance();
            bool isMuted = audioMgr != null && audioMgr.IsMuted;
            if (SolidButton(isMuted ? "Sessiz" : "Ses Açık", isMuted ? AccentRed : BtnSlate, GUILayout.Width(76), GUILayout.Height(25)))
                audioMgr?.ToggleMute();

            if (SolidButton("?", BtnSlate, GUILayout.Width(30), GUILayout.Height(25)))
                _showHelpModal = !_showHelpModal;

            if (SolidButton("F1", BtnSlate, GUILayout.Width(34), GUILayout.Height(25)))
                _hudVisible = false;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawLeftCitiesDrawer(Rect rect)
        {
            DrawSolidRect(rect, BgCard, BorderSubtle, 1);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));

            PlayerState self = _gameManager.GetLocalPlayerState();
            GUILayout.Label($"SİYASİ OVERALL ({self.displayName})", _headerStyle);
            DrawIdeologyBar("Milliyetçi", self.overall.nationalist, new Color(0.86f, 0.35f, 0.25f));
            DrawIdeologyBar("Liberal", self.overall.liberal, new Color(0.25f, 0.72f, 0.92f));
            DrawIdeologyBar("Demokrat", self.overall.democrat, new Color(0.32f, 0.80f, 0.45f));
            DrawIdeologyBar("Komünist", self.overall.communist, new Color(0.90f, 0.22f, 0.32f));
            DrawIdeologyBar("Emperyalist", self.overall.imperialist, new Color(0.75f, 0.48f, 0.92f));

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (SolidButton("Birimler & Görev", _leftTabIndex == 0 ? BtnGold : BtnSlate, GUILayout.Height(26)))
                _leftTabIndex = 0;
            if (SolidButton("6 Şehir", _leftTabIndex == 1 ? BtnGold : BtnSlate, GUILayout.Height(26)))
                _leftTabIndex = 1;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _leftScroll = GUILayout.BeginScrollView(_leftScroll);

            if (_leftTabIndex == 0)
            {
                GUILayout.Label($"HEDEF ŞEHİR: {CityCatalog.GetDisplayName(_deployTargetCity)}", _subHeaderStyle);
                // Clean 3x2 city target selector so long names like Üniversitekent never wrap or clip
                for (int row = 0; row < 3; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int col = 0; col < 2; col++)
                    {
                        CityId cid = CityCatalog.All[row * 2 + col];
                        if (SolidButton(CityCatalog.GetDisplayName(cid), _deployTargetCity == cid ? AccentBlue : BtnSlate, GUILayout.Height(24)))
                        {
                            _deployTargetCity = cid;
                            if (_cameraController != null) _cameraController.FocusCity(cid);
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);
                GUILayout.Label("BİRİM GÖREVLENDİR (Ücretsiz / Tur Limiti)", _headerStyle);
                foreach (UnitType ut in UnitCatalog.DeployableTypes)
                {
                    int rem = _gameManager.GetRemainingDeploymentCapacity(_gameManager.LocalPlayerId, ut);
                    int max = _gameManager.GetDeploymentLimitForRound(_gameManager.LocalPlayerId, ut);
                    if (SolidButton($"+1 {UnitCatalog.GetDisplayNameTr(ut)} ({rem}/{max})", BtnHover, GUILayout.Height(27)))
                    {
                        if (_authority != null)
                            _authority.RequestDeployUnit(_gameManager.LocalPlayerId, ut, _deployTargetCity, CityCatalog.GetPrimaryZoneName(_deployTargetCity), 1, out _actionFeedback);
                    }
                    GUILayout.Space(2);
                }

                if (_unitController != null)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("SEÇİM & SEVKİYAT", _headerStyle);
                    GUILayout.BeginHorizontal();
                    if (SolidButton("Tümünü Seç", BtnSlate)) _unitController.SelectAllOwnedUnits(_gameManager.LocalPlayerId);
                    if (SolidButton("Temizle", BtnSlate)) _unitController.ClearSelection();
                    GUILayout.EndHorizontal();
                    if (_unitController.SelectedUnits.Count > 0)
                    {
                        if (SolidButton($"Seçilileri {CityCatalog.GetDisplayName(_deployTargetCity)} Şehrine Sevk Et", AccentBlue, GUILayout.Height(28)) && _authority != null)
                        {
                            Vector3 dest = _gameManager.CityManager.GetCityWorldPosition(_deployTargetCity);
                            _authority.RequestMoveGroup(_gameManager.LocalPlayerId, _unitController.GetSelectedUnitIds(), dest, _deployTargetCity, CityCatalog.GetPrimaryZoneName(_deployTargetCity));
                        }
                    }
                    GUILayout.Label($"Seçili Birim: {_unitController.SelectedUnits.Count} | Haritada: {CampaignUnit.ActiveUnits.Count}", _mutedStyle);
                }
            }
            else
            {
                GUILayout.Label("6 STRATEJİK ŞEHİR", _headerStyle);
                if (_gameManager.CityManager != null)
                {
                    IReadOnlyList<CityState> cities = _gameManager.CityManager.Cities;
                    for (int i = 0; i < cities.Count; i++)
                    {
                        CityState city = cities[i];
                        DrawCardSection(() =>
                        {
                            string status = _gameManager.HasActiveRallyCity && _gameManager.ActiveRallyCity == city.cityId
                                ? " [MİTİNG]"
                                : (city.rallyDone ? " [BİTTİ]" : "");
                            string win = city.winnerSlot != PlayerSlot.None
                                ? (city.winnerSlot == PlayerSlot.PlayerA ? " [Oyuncu A]" : " [Oyuncu B]")
                                : "";
                            IdeologyType dom = city.ideologyDistribution.GetDominantIdeology();
                            GUILayout.Label($"{city.displayName} ({city.population:N0}){status}{win}", _subHeaderStyle);
                            GUILayout.Label($"Baskın: {IdeologyCodec.GetDisplayNameTr(dom)} %{city.ideologyDistribution.Get(dom):F0}", _mutedStyle);

                            GUILayout.BeginHorizontal();
                            if (SolidButton("Odakla", BtnSlate))
                            {
                                _deployTargetCity = city.cityId;
                                if (_cameraController != null) _cameraController.FocusCity(city.cityId);
                            }
                            if (_gameManager.CurrentPhase == TurnPhase.CITY_VOTE && !city.rallyDone)
                            {
                                if (SolidButton("Oy Ver", AccentGreen) && _authority != null)
                                    _authority.RequestVoteRallyCity(_gameManager.LocalPlayerId, city.cityId, out _actionFeedback);
                            }
                            if (_unitController != null && _unitController.SelectedUnits.Count > 0)
                            {
                                if (SolidButton("Sevk Et", AccentBlue) && _authority != null)
                                {
                                    Vector3 dest = _gameManager.CityManager.GetCityWorldPosition(city.cityId);
                                    _authority.RequestMoveGroup(_gameManager.LocalPlayerId, _unitController.GetSelectedUnitIds(), dest, city.cityId, CityCatalog.GetPrimaryZoneName(city.cityId));
                                }
                            }
                            GUILayout.EndHorizontal();
                        });
                        GUILayout.Space(4);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_actionFeedback))
                GUILayout.Label($"İşlem: {_actionFeedback}", _subHeaderStyle);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBottomPhaseBar(Rect rect)
        {
            DrawSolidRect(rect, BgCard, BorderSubtle, 1);
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12));
            _bottomScroll = GUILayout.BeginScrollView(_bottomScroll);

            TurnPhase phase = _gameManager.CurrentPhase;
            string localId = _gameManager.LocalPlayerId;

            if (phase == TurnPhase.CITY_VOTE)
            {
                GUILayout.Label("AŞAMA 1 — MİTİNG YAPILACAK ŞEHRİ SEÇ (15 sn)", _headerStyle);
                GUILayout.Label($"Oyuncu A Oyu: {_gameManager.PlayerA.cityVoteChoice}  |  Oyuncu B Oyu: {_gameManager.PlayerB.cityVoteChoice}", _bodyStyle);
                if (_gameManager.CityManager != null)
                {
                    GUILayout.BeginHorizontal();
                    IReadOnlyList<CityState> cities = _gameManager.CityManager.Cities;
                    for (int i = 0; i < cities.Count; i++)
                    {
                        CityState c = cities[i];
                        GUI.enabled = !c.rallyDone;
                        if (SolidButton(c.rallyDone ? $"{c.displayName} [OK]" : c.displayName, c.rallyDone ? BtnSlate : AccentGreen))
                        {
                            if (_authority != null)
                                _authority.RequestVoteRallyCity(localId, c.cityId, out _actionFeedback);
                            if (_cameraController != null)
                                _cameraController.FocusCity(c.cityId);
                        }
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else if (phase == TurnPhase.PREPARATION)
            {
                CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;
                GUILayout.Label($"AŞAMA 2 - HAZIRLIK ({city?.displayName ?? ""}) | Birimlerini Konuşlandır veya AI'a Danış", _headerStyle);
                if (city != null)
                    GUILayout.Label($"Şehir İdeolojileri: {city.ideologyDistribution.ToShortSummary()}", _bodyStyle);
                GUILayout.BeginHorizontal();
                if (SolidButton("Şehir & Birim Paneli", AccentBlue)) _showCitiesDrawer = true;
                if (SolidButton("AI Komutandan Plan Al", AccentGreen)) _showAiDrawer = true;
                if (SolidButton("Konuşmaya Geç >>", BtnHover) && _rallyController != null) _rallyController.ForceAdvancePhase();
                GUILayout.EndHorizontal();
            }
            else if (phase == TurnPhase.RALLY_SPEECH)
            {
                string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
                string saboteurId = _gameManager.GetSaboteurForRound(_gameManager.RoundNumber);
                CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;

                GUILayout.Label($"AŞAMA 3 — MİTİNG ({city?.displayName}) | Konuşmacı: {PlayerIdentity.GetDisplayName(speakerId)}", _headerStyle);
                if (_crowdReaction != null)
                    GUILayout.Label($"Seyirci: {_crowdReaction.CurrentReaction} ({_crowdReaction.SentimentScore:F0}/100) — {_crowdReaction.LastFeedback}", _subHeaderStyle);

                if (localId == speakerId)
                {
                    PlayerState speaker = _gameManager.GetPlayerState(speakerId);
                    if (speaker.isNextSpeechBlocked)
                    {
                        GUILayout.Label("UYARI: Revolver düellosunda vurulduğunuz için bu tur konuşma hakkınız BLOKE!", _headerStyle);
                    }
                    else
                    {
                        string newDraft = SolidTextArea(_speechDraft, GUILayout.Height(36));
                        if (newDraft != _speechDraft)
                        {
                            _speechDraft = newDraft;
                            if (_rallyController != null) _rallyController.SetLiveSpeakerTranscript(_speechDraft);
                        }

                        if (_rallyController != null && !string.IsNullOrEmpty(_rallyController.MicStatus))
                            GUILayout.Label(_rallyController.MicStatus, _rallyController.IsMicRecording ? _subHeaderStyle : _mutedStyle);

                        GUILayout.BeginHorizontal();
                        string presetSpeech = null;
                        if (SolidButton("Milliyetçi", BtnSlate)) presetSpeech = "Vatanımız, yerli sanayimiz ve sınır güvenliğimiz her şeyin üzerindedir!";
                        if (SolidButton("Liberal", BtnSlate)) presetSpeech = "Serbest piyasa, açık ticaret ve bireysel girişim özgürlüğü ile zenginleşeceğiz!";
                        if (SolidButton("Demokrat", BtnSlate)) presetSpeech = "Katılımcı demokrasi, hukuk devleti ve toplumsal uzlaşı ile büyüyeceğiz!";
                        if (SolidButton("Komünist", BtnSlate)) presetSpeech = "İşçinin, emekçinin ve sendikaların ortak refahı için kamu yatırımı!";
                        if (SolidButton("Emperyalist", BtnSlate)) presetSpeech = "Büyük güç vizyonu ve dev altyapı projeleriyle bölgesel lider olacağız!";
                        if (presetSpeech != null)
                        {
                            _speechDraft = presetSpeech;
                            if (_rallyController != null) _rallyController.SetLiveSpeakerTranscript(_speechDraft);
                        }
                        if (_rallyController != null && SolidButton(_rallyController.IsMicRecording ? "Kaydı Durdur" : "Mikrofon", _rallyController.IsMicRecording ? AccentRed : BtnHover, GUILayout.Width(92)))
                            _rallyController.ToggleMicrophoneCapture();
                        if (_rallyController != null && SolidButton("Konuşmayı Bitir", AccentGreen, GUILayout.Width(122)))
                            _rallyController.SubmitRallySpeech(speakerId, _speechDraft);
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label($"Sabotajcı Rolündesin ({PlayerIdentity.GetDisplayName(saboteurId)}):", _bodyStyle);
                    _verbalSabotageDraft = SolidTextField(_verbalSabotageDraft);
                    GUILayout.BeginHorizontal();
                    if (SolidButton("Sözlü Sabotaj Gönder (Cezasız)", AccentBlue) && _sabotageController != null)
                        _sabotageController.SubmitVerbalSabotage(saboteurId, _verbalSabotageDraft);
                    if (SolidButton("Fiziksel Kavga Çıkar (Riskli!)", AccentRed) && _sabotageController != null)
                        _sabotageController.TriggerPhysicalSabotage(saboteurId);
                    GUILayout.EndHorizontal();
                }
            }
            else if (phase == TurnPhase.AI_EVALUATION)
            {
                GUILayout.Label("AŞAMA 4 - AI KONUŞMA DEĞERLENDİRMESİ & BAĞIŞ", _headerStyle);
                if (_rallyController != null && _rallyController.LastSpeechEval != null)
                {
                    SpeechEvalResult ev = _rallyController.LastSpeechEval;
                    GUILayout.Label($"Etki: {ev.speech_effectiveness:F1}/100 | Tepki: {ev.crowd_reaction} | Bağış: +{_rallyController.LastDonationEarned:N0} TL", _subHeaderStyle);
                    GUILayout.Label(ev.feedback_summary, _bodyStyle);
                }
                else
                {
                    GUILayout.Label("Konuşma analiz ediliyor...", _bodyStyle);
                }
            }
            else if (phase == TurnPhase.REVOLVER_DUEL)
            {
                string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
                GUILayout.Label($"REVOLVER DÜELLOSU — {PlayerIdentity.GetDisplayName(speakerId)} 5 Şüpheli Arasından Sabotajcıyı Seçiyor!", _headerStyle);
                if (_sabotageController != null && !_sabotageController.RevolverResolved)
                {
                    if (localId == speakerId)
                    {
                        GUILayout.BeginHorizontal();
                        for (int i = 0; i < 5; i++)
                        {
                            if (SolidButton($"Şüpheli #{i + 1}", AccentRed))
                                _sabotageController.ResolveRevolverShot(speakerId, i);
                        }
                        if (SolidButton("Pas Geç", BtnSlate))
                            _sabotageController.HoldFire(speakerId);
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"Sabotajcı rolündesin: {PlayerIdentity.GetDisplayName(speakerId)} şüphelilere nişan alıyor...", _bodyStyle);
                        if (SolidButton($"Konuşmacı Görünümüne Geç ({PlayerIdentity.GetDisplayName(speakerId)})", AccentBlue, GUILayout.Width(250)))
                            _gameManager.SetLocalPlayerPerspective(speakerId);
                        GUILayout.EndHorizontal();
                    }
                }
                else if (_sabotageController != null)
                {
                    GUILayout.Label(_sabotageController.LastOutcomeMessage, _subHeaderStyle);
                }
            }
            else if (_electionController != null && _electionController.IsElectionPhase())
            {
                CityId eCity = _electionController.CurrentElectionCityId;
                GUILayout.Label($"SEÇİM GECESİ ({_electionController.CurrentCityIndex + 1}/6) — {CityCatalog.GetDisplayName(eCity)} ({CityCatalog.GetPopulation(eCity):N0} Oy)", _headerStyle);

                if (phase == TurnPhase.ELECTION_FINAL_SPEECH)
                {
                    bool isPlayerA = localId == PlayerIdentity.PlayerAId;
                    GUILayout.Label($"{PlayerIdentity.GetDisplayName(localId)} — {CityCatalog.GetDisplayName(eCity)} Gizli Son Konuşması:", _mutedStyle);
                    GUILayout.BeginHorizontal();
                    if (isPlayerA)
                        _finalSpeechDraftA = SolidTextField(_finalSpeechDraftA);
                    else
                        _finalSpeechDraftB = SolidTextField(_finalSpeechDraftB);

                    if (SolidButton("Son Konuşmayı Gönder >>", AccentGreen, GUILayout.Width(175)))
                    {
                        string localDraft = isPlayerA ? _finalSpeechDraftA : _finalSpeechDraftB;
                        _electionController.SubmitFinalSpeech(localId, localDraft);

                        string otherId = isPlayerA ? PlayerIdentity.PlayerBId : PlayerIdentity.PlayerAId;
                        string otherDraft = isPlayerA ? _finalSpeechDraftB : _finalSpeechDraftA;
                        string defaultOther = isPlayerA ? DefaultFinalSpeechB : DefaultFinalSpeechA;
                        if (string.IsNullOrWhiteSpace(otherDraft) || otherDraft == defaultOther)
                        {
                            CityState eCityState = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(eCity) : null;
                            IdeologyType domIdeo = eCityState != null
                                ? eCityState.ideologyDistribution.GetDominantIdeology()
                                : IdeologyType.Democrat;
                            string autoSpeech = BuildAutoOpponentFinalSpeech(eCityState, domIdeo);
                            if (isPlayerA) _finalSpeechDraftB = autoSpeech;
                            else _finalSpeechDraftA = autoSpeech;
                            _electionController.SubmitFinalSpeech(otherId, autoSpeech);
                        }
                        else
                        {
                            _electionController.SubmitFinalSpeech(otherId, otherDraft);
                        }

                        _electionController.ForceAdvanceElectionPhase();
                    }
                    GUILayout.EndHorizontal();
                }
                else if (phase == TurnPhase.ELECTION_BRIBE)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Gizli Rüşvet (Bütçe {_gameManager.GetLocalPlayerState().partyBudget:N0} TL, maks +/-%2 etki):", _bodyStyle, GUILayout.Width(325));
                    _bribeAmountInput = SolidTextField(_bribeAmountInput, GUILayout.Width(80));
                    if (SolidButton("Rüşveti Onayla", BtnGold, GUILayout.Width(120)) && _authority != null)
                    {
                        if (float.TryParse(_bribeAmountInput, out float amt))
                            _authority.RequestAllocateElectionBribe(localId, eCity, amt, out _actionFeedback);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Label($"A Kilitli: {(_electionController.BribeLockedA ? "Evet" : "Hayır")} | B Kilitli: {(_electionController.BribeLockedB ? "Evet" : "Hayır")}", _mutedStyle);
                }
                else if (phase == TurnPhase.ELECTION_RESULT && _electionController.LastCityResult != null)
                {
                    ElectionCityResult res = _electionController.LastCityResult;
                    GUILayout.Label(res.headline, _subHeaderStyle);
                    GUILayout.Label($"Oyuncu A: {res.votesA:N0} oy | Oyuncu B: {res.votesB:N0} oy | Rüşvet Etkisi: {res.bribeShiftApplied * 100f:+0.00;-0.00;0.00}%", _bodyStyle);
                }
            }

            if (!string.IsNullOrEmpty(_actionFeedback))
                GUILayout.Label($"Bildirim: {_actionFeedback}", _mutedStyle);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRightAiDrawer(Rect rect)
        {
            DrawSolidRect(rect, BgCard, BorderSubtle, 1);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));

            if (_commanderPanel == null || _gatewayClient == null)
            {
                GUILayout.Label("AI Strategic Commander bulunamadı.", _headerStyle);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("AI STRATEJİ KOMUTANI", _headerStyle);
            GUILayout.FlexibleSpace();
            if (SolidButton("Yenile", BtnSlate, GUILayout.Width(58))) _gatewayClient.CheckHealth(null);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Sunucu: {_gatewayClient.HealthStatusLabel}", _mutedStyle);

            // Provider Switch
            GUILayout.BeginHorizontal();
            AiProviderMode mode = _commanderPanel.CurrentProviderMode;
            if (SolidButton("Otomatik", mode == AiProviderMode.Auto ? AccentBlue : BtnSlate)) _commanderPanel.SetProviderMode(AiProviderMode.Auto);
            if (SolidButton("Bulut AI", mode == AiProviderMode.CloudApi ? AccentBlue : BtnSlate)) _commanderPanel.SetProviderMode(AiProviderMode.CloudApi);
            if (SolidButton("Ollama", mode == AiProviderMode.LocalOllama ? AccentBlue : BtnSlate)) _commanderPanel.SetProviderMode(AiProviderMode.LocalOllama);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (SolidButton("Plan & Araçlar", _aiTabIndex == 0 ? BtnGold : BtnSlate)) _aiTabIndex = 0;
            if (SolidButton("Doğrulama", _aiTabIndex == 1 ? BtnGold : BtnSlate)) _aiTabIndex = 1;
            if (SolidButton("Güvenli Durum", _aiTabIndex == 2 ? BtnGold : BtnSlate))
            {
                _aiTabIndex = 2;
                _commanderPanel.RefreshSnapshotPreview();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _rightScroll = GUILayout.BeginScrollView(_rightScroll);

            if (_aiTabIndex == 0)
            {
                _commanderPanel.QuestionInput = SolidTextArea(_commanderPanel.QuestionInput, GUILayout.Height(46));
                GUI.enabled = !_commanderPanel.IsRequestInFlight;
                if (SolidButton(_commanderPanel.IsRequestInFlight ? "AI Düşünüyor..." : "AI Komutana Sor", AccentGreen, GUILayout.Height(28)))
                    _commanderPanel.AskCommander();

                GUILayout.BeginHorizontal();
                if (SolidButton("1. Bileşik Strateji", BtnSlate)) _commanderPanel.RunPresetCompoundStrategy();
                if (SolidButton("2. RAG Bilgi", BtnSlate)) _commanderPanel.RunPresetKnowledgeOnly();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (SolidButton("3. Geçersiz Plan", BtnSlate)) _commanderPanel.RunPresetInvalidPlanReplay();
                if (SolidButton("4. Güvenlik Testi", AccentRed)) _commanderPanel.RunPresetPromptInjection();
                GUILayout.EndHorizontal();
                GUI.enabled = true;

                GUILayout.Label(_commanderPanel.StatusMessage, _subHeaderStyle);

                CommanderAskResponse res = _commanderPanel.LastResponse;
                bool hasRealResponse = res != null && (
                    !string.IsNullOrWhiteSpace(res.provider_used) ||
                    !string.IsNullOrWhiteSpace(res.explanation) ||
                    res.security_blocked ||
                    (res.tool_calls != null && res.tool_calls.Count > 0));

                if (hasRealResponse)
                {
                    DrawCardSection(() =>
                    {
                        GUILayout.Label($"Sağlayıcı: {res.provider_used} ({res.latency_ms} ms) | Güvenlik Bloğu: {(res.security_blocked ? "Evet" : "Hayır")}", _mutedStyle);
                        if (!string.IsNullOrEmpty(res.explanation))
                            GUILayout.Label(res.explanation, _bodyStyle);

                        if (res.tool_calls != null && res.tool_calls.Count > 0)
                        {
                            GUILayout.Label("Kullanılan Araçlar:", _headerStyle);
                            for (int i = 0; i < res.tool_calls.Count; i++)
                            {
                                ToolCallTraceDto tc = res.tool_calls[i];
                                GUILayout.Label($"- {tc.tool} [{tc.status}] {tc.duration_ms} ms", _mutedStyle);
                            }
                        }

                        if (res.rag_sources != null && res.rag_sources.Count > 0)
                        {
                            GUILayout.Label("Bilgi Kaynakları:", _headerStyle);
                            for (int i = 0; i < res.rag_sources.Count; i++)
                            {
                                RagSourceTraceDto rs = res.rag_sources[i];
                                GUILayout.Label($"- {rs.source_id} ({rs.score:F2})", _mutedStyle);
                            }
                        }
                    });
                }

                ActionPlan plan = _commanderPanel.PendingPlan;
                bool hasRealPlan = plan != null && (
                    !string.IsNullOrWhiteSpace(plan.summary) ||
                    (plan.actions != null && plan.actions.Count > 0));

                if (hasRealPlan)
                {
                    DrawCardSection(() =>
                    {
                        GUILayout.Label($"Önerilen Plan (Güven: {plan.confidence:F2})", _headerStyle);
                        GUILayout.Label(plan.summary, _bodyStyle);
                        if (plan.actions != null)
                        {
                            for (int i = 0; i < plan.actions.Count; i++)
                            {
                                AiActionItem a = plan.actions[i];
                                GUILayout.Label($"[{i + 1}] {FormatAiActionSummary(a)}", _subHeaderStyle);
                                GUILayout.Label($"    {a.reason}", _mutedStyle);
                            }
                        }

                        GUILayout.BeginHorizontal();
                        if (SolidButton("ONAYLA", AccentGreen)) _commanderPanel.ApprovePendingPlan();
                        if (SolidButton("REDDET", AccentRed)) _commanderPanel.RejectPendingPlan();
                        if (SolidButton("Neden?", BtnSlate)) _commanderPanel.AskWhyPendingPlan();
                        GUILayout.EndHorizontal();
                    });
                }
            }
            else if (_aiTabIndex == 1)
            {
                GUILayout.Label("DOĞRULAMA DENETİM KAYDI", _headerStyle);
                IReadOnlyList<ValidationLogEntry> logs = GameActionValidator.AuditLog;
                if (logs.Count == 0)
                    GUILayout.Label("Henüz denetim kaydı yok.", _mutedStyle);
                for (int i = logs.Count - 1; i >= 0; i--)
                {
                    ValidationLogEntry e = logs[i];
                    GUILayout.Label($"[{(e.accepted ? "OK" : "RED")}] {e.playerId}/{e.actionType}: {e.message}", _bodyStyle);
                }

                GUILayout.Space(6);
                GUILayout.Label("ONAY GEÇMİŞİ", _headerStyle);
                IReadOnlyList<HitlDecisionRecord> decisions = _commanderPanel.DecisionHistory;
                if (decisions.Count == 0)
                    GUILayout.Label("Henüz onay/red kararı yok.", _mutedStyle);
                for (int i = decisions.Count - 1; i >= 0; i--)
                {
                    HitlDecisionRecord d = decisions[i];
                    GUILayout.Label($"- {d.decision} ({d.playerId}): {d.summary}", _mutedStyle);
                }
            }
            else
            {
                GUILayout.Label("GÜVENLİ OYUN DURUMU (GİZLİLİK FİLTRELİ)", _headerStyle);
                GUILayout.Label("Rakip bütçesi ve gizli destek oranları burada ASLA yer almaz.", _mutedStyle);
                if (SolidButton("Durumu Yenile", BtnSlate)) _commanderPanel.RefreshSnapshotPreview();
                SolidTextArea(_commanderPanel.LastSnapshotJson, GUILayout.ExpandHeight(true));
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawVictoryBalconyModal(Rect rect)
        {
            DrawSolidRect(rect, BgDark, AccentGold, 2);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 14, rect.width - 32, rect.height - 28));

            GUILayout.Label($"BALKON KONUŞMASI - KAZANAN: {PlayerIdentity.GetDisplayName(_gameManager.MatchWinnerId)}!", _titleStyle);
            GUILayout.Label($"Şehir Skoru: Oyuncu A {_gameManager.PlayerA.citiesWon} - {_gameManager.PlayerB.citiesWon} Oyuncu B", _subHeaderStyle);
            GUILayout.Space(6);

            if (_gameManager.CityManager != null)
            {
                DrawCardSection(() =>
                {
                    IReadOnlyList<CityState> cities = _gameManager.CityManager.Cities;
                    for (int i = 0; i < cities.Count; i++)
                    {
                        CityState c = cities[i];
                        string winnerTr = c.winnerSlot == PlayerSlot.PlayerA ? "Oyuncu A" : (c.winnerSlot == PlayerSlot.PlayerB ? "Oyuncu B" : "-");
                        GUILayout.Label($"- {c.displayName} ({c.population:N0} oy): {winnerTr}", _bodyStyle);
                    }
                });
            }

            GUILayout.Space(6);
            GUILayout.Label("Zafer Konuşmanı Yaz (Seyirciler Coşkuyla Alkışlayacak!):", _subHeaderStyle);
            _balconyDraft = SolidTextField(_balconyDraft);
            if (_electionController != null && !string.IsNullOrEmpty(_electionController.BalconySpeechLog))
                GUILayout.Label(_electionController.BalconySpeechLog, _subHeaderStyle);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (SolidButton("Balkondan Konuş!", BtnGold, GUILayout.Height(34)) && _electionController != null)
                _electionController.SubmitBalconySpeech(_balconyDraft);
            if (SolidButton("Yeni Maç", AccentGreen, GUILayout.Height(34)))
                _gameManager.StartNewMatch(_gameManager.MatchSeed + 1);
            if (SolidButton("Ana Menü", BtnSlate, GUILayout.Height(34)))
                _showStartMenu = true;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawHelpModal(float w, float h)
        {
            float mw = Mathf.Min(500f, w - 32f);
            float mh = Mathf.Min(340f, h - 32f);
            Rect r = new Rect((w - mw) * 0.5f, (h - mh) * 0.5f, mw, mh);
            _hudBlockingRects.Add(r);

            DrawSolidRect(r, BgDark, AccentCyan, 2);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 14, r.width - 32, r.height - 28));
            GUILayout.Label("KONTROLLER & NASIL OYNANIR", _titleStyle);
            GUILayout.Space(6);
            GUILayout.Label("- Kamera: W/A/S/D veya Ok tuşları ile gez, Mouse tekerleği ile yakınlaş, 1-6 tuşlarıyla şehirlere odaklan.", _bodyStyle);
            GUILayout.Label("- RTS Birimleri: Sol tık veya sürükleyerek birim seç; Sağ tık ile haritaya yürüt veya düşmana saldır.", _bodyStyle);
            GUILayout.Label("- Şehirler & Birimler: Üst bardaki [Şehirler & Birimler] butonundan yeni birim konuşlandırabilirsin.", _bodyStyle);
            GUILayout.Label("- AI Strateji Komutanı: [AI Komutan] butonundan öneri isteyip [ONAYLA] veya [REDDET] yapabilirsin.", _bodyStyle);
            GUILayout.Label("- Gizle/Göster: F1 tuşu tüm arayüzü gizleyip haritayı tam ekran gösterir.", _bodyStyle);
            GUILayout.FlexibleSpace();
            if (SolidButton("Kapat", AccentBlue, GUILayout.Height(32)))
                _showHelpModal = false;
            GUILayout.EndArea();
        }

        private void DrawCardSection(Action drawContent)
        {
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = BgCardInner;
            GUILayout.BeginVertical(_cardStyle);
            GUI.backgroundColor = prevBg;
            drawContent?.Invoke();
            GUILayout.EndVertical();
        }

        private void DrawIdeologyBar(string label, float value, Color fillColor)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _bodyStyle, GUILayout.Width(82));
            Rect barRect = GUILayoutUtility.GetRect(100f, 14f, GUILayout.ExpandWidth(true));
            DrawSolidRect(barRect, new Color(0.05f, 0.07f, 0.11f, 1f), BorderSubtle, 1);
            Rect fillRect = new Rect(barRect.x + 1, barRect.y + 1, Mathf.Max(0f, (barRect.width - 2f) * (clamped / 100f)), barRect.height - 2f);
            DrawSolidRect(fillRect, fillColor);
            GUILayout.Label($"{clamped:F0}", _mutedStyle, GUILayout.Width(28));
            GUILayout.EndHorizontal();
        }

        private static string FormatAiActionSummary(AiActionItem a)
        {
            if (a == null) return string.Empty;
            switch (a.action_type)
            {
                case "DEPLOY_UNIT":
                    return $"Birim Görevlendir: {a.unit_type} x{Mathf.Max(1, a.count)} -> {a.city_name}";
                case "MOVE_GROUP":
                case "MOVE_UNIT":
                    return $"Birim Sevk Et: {a.unit_type} x{Mathf.Max(1, a.count)} -> {a.city_name}";
                case "VOTE_RALLY_CITY":
                    return $"Miting Şehri Oyla: {a.city_name}";
                case "CHOOSE_SPEECH_STRATEGY":
                    return $"Konuşma Odağı: {a.ideology_focus} ({a.city_name})";
                case "ALLOCATE_ELECTION_BRIBE":
                    return $"Seçim Rüşveti: {a.bribe_amount:N0} TL -> {a.city_name}";
                default:
                    return $"{a.action_type} -> {a.city_name}";
            }
        }

        private static string GetRoleDisplayNameTr(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.SPEAKER: return "Konuşmacı";
                case PlayerRole.SABOTEUR: return "Sabotajcı";
                case PlayerRole.ELECTION_CANDIDATE: return "Seçim Adayı";
                default: return "İzleyici";
            }
        }

        private static string BuildAutoOpponentFinalSpeech(CityState city, IdeologyType dominantIdeology)
        {
            string cityName = city != null ? city.displayName : "Şehrimiz";
            switch (dominantIdeology)
            {
                case IdeologyType.Nationalist:
                    return $"{cityName} için yerli üretim, milli kalkınma ve güçlü gelecek!";
                case IdeologyType.Liberal:
                    return $"{cityName} halkına serbest ticaret, yatırım ve ekonomik özgürlük sözü veriyoruz!";
                case IdeologyType.Communist:
                    return $"{cityName} emekçilerinin hakkı için adil paylaşım ve güçlü kamu yatırımı!";
                case IdeologyType.Imperialist:
                    return $"{cityName} bölgesel güç vizyonumuzun ve dev projelerimizin merkezi olacak!";
                default:
                    return $"{cityName} için şeffaf yönetim, toplumsal uzlaşı ve ortak refah!";
            }
        }

        private static string GetPhaseDisplayNameTr(TurnPhase phase)
        {
            switch (phase)
            {
                case TurnPhase.CITY_VOTE: return "ŞEHİR SEÇİMİ";
                case TurnPhase.PREPARATION: return "HAZIRLIK";
                case TurnPhase.RALLY_SPEECH: return "MİTİNG & SABOTAJ";
                case TurnPhase.AI_EVALUATION: return "AI DEĞERLENDİRMESİ";
                case TurnPhase.REVOLVER_DUEL: return "REVOLVER DÜELLOSU";
                case TurnPhase.ELECTION_FINAL_SPEECH: return "SEÇİM: SON KONUŞMA";
                case TurnPhase.ELECTION_BRIBE: return "SEÇİM: RÜŞVET";
                case TurnPhase.ELECTION_RESULT: return "SEÇİM SONUCU";
                case TurnPhase.BALCONY_SPEECH: return "BALKON KONUŞMASI";
                default: return phase.ToString();
            }
        }
    }
}
