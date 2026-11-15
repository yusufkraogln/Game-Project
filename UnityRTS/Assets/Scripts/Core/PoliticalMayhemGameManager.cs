using System;
using System.Collections.Generic;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Master authoritative state machine for Political Mayhem: Battle for the Cities.
    /// Coordinates seeded city initialization, 6 alternating Rally Rounds, Election Night,
    /// Balcony Speech, Fog-of-War boundaries, and match victory resolution.
    /// </summary>
    public class PoliticalMayhemGameManager : MonoBehaviour
    {
        public static PoliticalMayhemGameManager Instance { get; private set; }

        [Header("Match Identity & Seed")]
        [SerializeField] private string _matchId = "pm-match-2026-01";
        [SerializeField] private int _matchSeed = 20260377;
        [SerializeField] private string _localPlayerId = PlayerIdentity.PlayerAId;
        [SerializeField] private bool _autoStartOnAwake = true;

        [Header("Core Managers")]
        [SerializeField] private CityManager _cityManager;
        [SerializeField] private OverallManager _overallManager;
        [SerializeField] private PartyBudgetManager _partyBudgetManager;

        [Header("Runtime Match State")]
        [SerializeField] private int _roundNumber = 1;
        [SerializeField] private TurnPhase _currentPhase = TurnPhase.CITY_VOTE;
        [SerializeField] private CityId _activeRallyCity = CityId.Baskent;
        [SerializeField] private bool _hasActiveRallyCity;
        [SerializeField] private int _activeElectionCityIndex;
        [SerializeField] private string _matchWinnerId = string.Empty;
        [SerializeField] private bool _matchCompleted;

        private PlayerState _playerA = new PlayerState { playerId = PlayerIdentity.PlayerAId, displayName = "Oyuncu A" };
        private PlayerState _playerB = new PlayerState { playerId = PlayerIdentity.PlayerBId, displayName = "Oyuncu B" };

        private readonly List<MatchEventRecord> _publicEvents = new List<MatchEventRecord>();
        private readonly List<VisibleOpponentActivityRecord> _opponentActivities = new List<VisibleOpponentActivityRecord>();
        private readonly List<ElectionCityResult> _electionResults = new List<ElectionCityResult>();

        private System.Random _matchRng;

        public event Action OnMatchInitialized;
        public event Action<int, TurnPhase> OnPhaseChanged;
        public event Action<string> OnMatchCompleted;

        public string MatchId => _matchId;
        public int MatchSeed => _matchSeed;
        public string LocalPlayerId => _localPlayerId;
        public int RoundNumber => _roundNumber;
        public TurnPhase CurrentPhase => _currentPhase;
        public CityId ActiveRallyCity => _activeRallyCity;
        public bool HasActiveRallyCity => _hasActiveRallyCity;
        public int ActiveElectionCityIndex => _activeElectionCityIndex;
        public string MatchWinnerId => _matchWinnerId;
        public bool MatchCompleted => _matchCompleted;

        public CityManager CityManager => _cityManager;
        public OverallManager OverallManager => _overallManager;
        public PartyBudgetManager PartyBudgetManager => _partyBudgetManager;
        public PlayerState PlayerA => _playerA;
        public PlayerState PlayerB => _playerB;
        public IReadOnlyList<MatchEventRecord> PublicEvents => _publicEvents;
        public IReadOnlyList<VisibleOpponentActivityRecord> OpponentActivities => _opponentActivities;
        public IReadOnlyList<ElectionCityResult> ElectionResults => _electionResults;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResolveSiblingManagers();
            EnsureSceneEnvironmentVisuals();
            CampaignUnit.OnUnitDefeated += HandleUnitDefeated;
        }

        private void OnDestroy()
        {
            CampaignUnit.OnUnitDefeated -= HandleUnitDefeated;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (_autoStartOnAwake && (_cityManager == null || _cityManager.Cities.Count == 0))
            {
                StartNewMatch(_matchSeed);
            }
        }

        public void ResolveSiblingManagers()
        {
            if (_cityManager == null) _cityManager = GetComponent<CityManager>() ?? FindAnyObjectByType<CityManager>();
            if (_overallManager == null) _overallManager = GetComponent<OverallManager>() ?? FindAnyObjectByType<OverallManager>();
            if (_partyBudgetManager == null) _partyBudgetManager = GetComponent<PartyBudgetManager>() ?? FindAnyObjectByType<PartyBudgetManager>();
        }

        public void StartNewMatch(int seed)
        {
            ResolveSiblingManagers();
            EnsureSceneEnvironmentVisuals();
            _matchSeed = seed;
            _matchRng = new System.Random(_matchSeed);
            _roundNumber = 1;
            _currentPhase = TurnPhase.CITY_VOTE;
            _hasActiveRallyCity = false;
            _activeElectionCityIndex = 0;
            _matchWinnerId = string.Empty;
            _matchCompleted = false;

            _playerA = new PlayerState
            {
                playerId = PlayerIdentity.PlayerAId,
                displayName = "Oyuncu A",
                partyBudget = 0f,
                overall = new IdeologyScoresDto(20f, 20f, 20f, 20f, 20f)
            };
            _playerB = new PlayerState
            {
                playerId = PlayerIdentity.PlayerBId,
                displayName = "Oyuncu B",
                partyBudget = 0f,
                overall = new IdeologyScoresDto(20f, 20f, 20f, 20f, 20f)
            };

            _publicEvents.Clear();
            _opponentActivities.Clear();
            _electionResults.Clear();

            if (_cityManager != null) _cityManager.InitializeCities(_matchSeed);
            if (_overallManager != null) _overallManager.BindPlayers(_playerA, _playerB);
            if (_partyBudgetManager != null) _partyBudgetManager.BindPlayers(_playerA, _playerB);

            RecordPublicEvent("MATCH_START", $"Maç başladı (Seed: {_matchSeed}). Tur 1 Şehir Seçimi açık.", string.Empty);
            OnMatchInitialized?.Invoke();
            OnPhaseChanged?.Invoke(_roundNumber, _currentPhase);
        }

        public void SetLocalPlayerPerspective(string playerId)
        {
            _localPlayerId = PlayerIdentity.Normalize(playerId);
        }

        public PlayerState GetPlayerState(string playerId)
        {
            return PlayerIdentity.ToSlot(playerId) == PlayerSlot.PlayerB ? _playerB : _playerA;
        }

        public PlayerState GetLocalPlayerState()
        {
            return GetPlayerState(_localPlayerId);
        }

        /// <summary>
        /// Rounds 1..6 alternate Speaker/Saboteur strictly:
        /// Odd rounds (1,3,5): Player A Speaker, Player B Saboteur.
        /// Even rounds (2,4,6): Player B Speaker, Player A Saboteur.
        /// </summary>
        public string GetSpeakerForRound(int round)
        {
            if (round < 1 || round > 6) return string.Empty;
            return (round % 2 == 1) ? PlayerIdentity.PlayerAId : PlayerIdentity.PlayerBId;
        }

        public string GetSaboteurForRound(int round)
        {
            if (round < 1 || round > 6) return string.Empty;
            return (round % 2 == 1) ? PlayerIdentity.PlayerBId : PlayerIdentity.PlayerAId;
        }

        public PlayerRole GetPlayerRole(string playerId)
        {
            string normalized = PlayerIdentity.Normalize(playerId);
            if (_currentPhase == TurnPhase.ELECTION_FINAL_SPEECH
                || _currentPhase == TurnPhase.ELECTION_BRIBE
                || _currentPhase == TurnPhase.ELECTION_RESULT)
            {
                return PlayerRole.ELECTION_CANDIDATE;
            }

            if (_currentPhase == TurnPhase.BALCONY_SPEECH)
            {
                return normalized == _matchWinnerId ? PlayerRole.SPEAKER : PlayerRole.NONE;
            }

            if (_roundNumber >= 1 && _roundNumber <= 6)
            {
                if (normalized == GetSpeakerForRound(_roundNumber)) return PlayerRole.SPEAKER;
                if (normalized == GetSaboteurForRound(_roundNumber)) return PlayerRole.SABOTEUR;
            }

            return PlayerRole.NONE;
        }

        public bool IsRallyRoundPhase()
        {
            return _currentPhase == TurnPhase.CITY_VOTE
                || _currentPhase == TurnPhase.PREPARATION
                || _currentPhase == TurnPhase.RALLY_SPEECH
                || _currentPhase == TurnPhase.AI_EVALUATION
                || _currentPhase == TurnPhase.REVOLVER_DUEL;
        }

        public int GetDeploymentLimitForRound(string playerId, UnitType unitType)
        {
            UnitStatDefinition stats = UnitCatalog.GetBaseStats(unitType);
            int limit = stats.deploymentLimitPerRound;
            if (unitType == UnitType.CampaignVolunteer && _overallManager != null)
            {
                limit += _overallManager.GetVolunteerRoundCapBonus(playerId);
            }
            return limit;
        }

        public int GetRemainingDeploymentCapacity(string playerId, UnitType unitType)
        {
            if (!UnitCatalog.IsDeployableByPlayer(unitType)) return 0;
            PlayerState player = GetPlayerState(playerId);
            int limit = GetDeploymentLimitForRound(playerId, unitType);
            int deployed = player.GetDeployedThisRound(unitType);
            return Mathf.Max(0, limit - deployed);
        }

        public void SetRoundAndPhase(int roundNumber, TurnPhase newPhase)
        {
            _roundNumber = Mathf.Clamp(roundNumber, 0, 12);
            _currentPhase = newPhase;
            if (_cityManager != null)
            {
                _cityManager.RefreshAllVisuals(_hasActiveRallyCity ? _activeRallyCity : (CityId?)null);
            }
            OnPhaseChanged?.Invoke(_roundNumber, _currentPhase);
        }

        public void SetActiveRallyCity(CityId cityId)
        {
            _activeRallyCity = cityId;
            _hasActiveRallyCity = true;
            CityState city = _cityManager != null ? _cityManager.GetCity(cityId) : null;
            if (city != null)
            {
                city.rallyRoundNumber = _roundNumber;
                city.rallySpeakerSlot = PlayerIdentity.ToSlot(GetSpeakerForRound(_roundNumber));
            }
            if (_cityManager != null)
            {
                _cityManager.RefreshAllVisuals(_activeRallyCity);
            }
        }

        public void MarkCurrentRallyCompleted()
        {
            if (!_hasActiveRallyCity || _cityManager == null) return;
            CityState city = _cityManager.GetCity(_activeRallyCity);
            if (city != null)
                city.rallyDone = true;
            _hasActiveRallyCity = false;
            _cityManager.RefreshAllVisuals(null);
        }

        public void ResetRoundUnitCounters()
        {
            _playerA.ResetRoundDeployments();
            _playerB.ResetRoundDeployments();
            _playerA.cityVoteChoice = string.Empty;
            _playerB.cityVoteChoice = string.Empty;
        }

        public CityId PickRandomCandidate(CityId optionA, CityId optionB)
        {
            if (_matchRng == null) _matchRng = new System.Random(_matchSeed);
            return _matchRng.Next(0, 2) == 0 ? optionA : optionB;
        }

        public int PickRandomSuspectIndex(int suspectCount = 5)
        {
            if (_matchRng == null) _matchRng = new System.Random(_matchSeed);
            return _matchRng.Next(0, Mathf.Max(1, suspectCount));
        }

        public void RecordPublicEvent(string eventType, string summary, string cityName)
        {
            _publicEvents.Add(new MatchEventRecord
            {
                eventType = string.IsNullOrWhiteSpace(eventType) ? "EVENT" : eventType,
                summary = string.IsNullOrWhiteSpace(summary) ? "-" : summary,
                cityName = cityName ?? string.Empty,
                timestampSeconds = Time.realtimeSinceStartup
            });
            if (_publicEvents.Count > 40)
                _publicEvents.RemoveAt(0);
        }

        public void RecordPublicSpeechActivity(string speakerPlayerId, CityId cityId, string transcript)
        {
            string normalizedSpeaker = PlayerIdentity.Normalize(speakerPlayerId);
            string observerId = PlayerIdentity.GetOpponentId(normalizedSpeaker);
            string excerpt = string.IsNullOrEmpty(transcript)
                ? "(Sessiz konuşma)"
                : (transcript.Length > 200 ? transcript.Substring(0, 200) : transcript);

            _opponentActivities.Add(new VisibleOpponentActivityRecord
            {
                observerPlayerId = observerId,
                activityType = "PUBLIC_SPEECH",
                cityName = CityCatalog.ToKey(cityId),
                unitType = string.Empty,
                count = 0,
                zone = CityCatalog.GetPrimaryZoneName(cityId),
                speechExcerpt = excerpt,
                timestampSeconds = Time.realtimeSinceStartup
            });
            if (_opponentActivities.Count > 30)
                _opponentActivities.RemoveAt(0);
        }

        private void HandleUnitDefeated(CampaignUnit victim, CampaignUnit attacker)
        {
            if (victim == null) return;
            string victimOwner = PlayerIdentity.GetDisplayName(victim.OwnerPlayerId);
            string attackerName = attacker != null ? UnitCatalog.GetDisplayNameTr(attacker.UnitType) : "Rakip";
            RecordPublicEvent(
                "UNIT_DEFEATED",
                $"{victimOwner} birimi ({UnitCatalog.GetDisplayNameTr(victim.UnitType)}) {CityCatalog.GetDisplayName(victim.CurrentCity)} bölgesinde {attackerName} tarafından dağıtıldı.",
                CityCatalog.ToKey(victim.CurrentCity));
        }

        /// <summary>
        /// Fog-of-War visibility boundary enforced deterministically in C# before snapshot serialization.
        /// Opponent units are visible ONLY when within vision range of own units, in the same city as own units,
        /// or inside the currently active public Rally city.
        /// </summary>
        public bool IsOpponentUnitVisibleTo(string viewerPlayerId, CampaignUnit enemyUnit)
        {
            if (enemyUnit == null || !enemyUnit.IsAlive) return false;
            string viewer = PlayerIdentity.Normalize(viewerPlayerId);
            if (enemyUnit.OwnerPlayerId == viewer) return false;

            if (_hasActiveRallyCity && enemyUnit.CurrentCity == _activeRallyCity &&
                (_currentPhase == TurnPhase.PREPARATION || _currentPhase == TurnPhase.RALLY_SPEECH || _currentPhase == TurnPhase.AI_EVALUATION))
            {
                return true;
            }

            IReadOnlyList<CampaignUnit> units = CampaignUnit.ActiveUnits;
            for (int i = 0; i < units.Count; i++)
            {
                CampaignUnit friendly = units[i];
                if (friendly == null || !friendly.IsAlive || friendly.OwnerPlayerId != viewer) continue;

                if (friendly.CurrentCity == enemyUnit.CurrentCity)
                    return true;

                float dist = Vector3.Distance(friendly.transform.position, enemyUnit.transform.position);
                if (dist <= friendly.VisionRadius)
                    return true;
            }

            return false;
        }

        public void AddElectionResult(ElectionCityResult result)
        {
            if (result == null) return;
            _electionResults.Add(result);
            _activeElectionCityIndex = _electionResults.Count;
            if (_cityManager != null)
            {
                _cityManager.RefreshAllVisuals(null);
            }
        }

        public void ResolveMatchVictory()
        {
            int winsA = 0;
            int winsB = 0;
            int popVotesA = 0;
            int popVotesB = 0;

            if (_cityManager != null)
            {
                foreach (CityState city in _cityManager.Cities)
                {
                    if (city.winnerSlot == PlayerSlot.PlayerA) winsA++;
                    else if (city.winnerSlot == PlayerSlot.PlayerB) winsB++;

                    popVotesA += city.finalVotesA;
                    popVotesB += city.finalVotesB;
                }
            }

            _playerA.citiesWon = winsA;
            _playerB.citiesWon = winsB;
            _playerA.totalPopularVotes = popVotesA;
            _playerB.totalPopularVotes = popVotesB;

            string reason;
            if (winsA != winsB)
            {
                _matchWinnerId = winsA > winsB ? PlayerIdentity.PlayerAId : PlayerIdentity.PlayerBId;
                reason = $"Şehir çoğunluğu ({winsA} - {winsB})";
            }
            else if (popVotesA != popVotesB)
            {
                _matchWinnerId = popVotesA > popVotesB ? PlayerIdentity.PlayerAId : PlayerIdentity.PlayerBId;
                reason = $"3-3 eşitlik! Toplam halk oyu ({popVotesA:N0} vs {popVotesB:N0})";
            }
            else
            {
                // Deterministic fallback tie-breaker (total accumulated donations, then Player A)
                _matchWinnerId = _playerA.totalDonationsEarned >= _playerB.totalDonationsEarned
                    ? PlayerIdentity.PlayerAId
                    : PlayerIdentity.PlayerBId;
                reason = "Tam eşitlik — deterministik bağış/sıra eşitlik bozma kuralı uygulandı";
            }

            _matchCompleted = true;
            SetRoundAndPhase(12, TurnPhase.BALCONY_SPEECH);
            RecordPublicEvent(
                "MATCH_WINNER",
                $"Seçim Galibi: {PlayerIdentity.GetDisplayName(_matchWinnerId)}! ({reason}). 60s Balkon Konuşması başladı!",
                "Baskent");
            OnMatchCompleted?.Invoke(_matchWinnerId);
        }

        public void EnsureSceneEnvironmentVisuals()
        {
            if (Application.isPlaying)
            {
                CityVisualBuilder.EnsureCityLandmarksUpgraded();
            }
            RepairSceneEnvironmentVisuals();
        }

        /// <summary>
        /// Repairs ground, ocean, camera backdrop, plazas, and any legacy Standard materials
        /// at runtime or during Editor reloads so the playfield stays matte dark charcoal instead of URP pink.
        /// </summary>
        public static void RepairSceneEnvironmentVisuals()
        {
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            }

            MapEnvironmentVisuals envVisuals = FindAnyObjectByType<MapEnvironmentVisuals>();
            if (envVisuals != null)
            {
                envVisuals.ApplyAll();
            }

            GameObject mapGround = GameObject.Find("MapGround");
            if (mapGround != null)
            {
                if (mapGround.TryGetComponent(out Renderer groundRend))
                {
                    MaterialHelper.ApplyColor(groundRend, new Color(0.10f, 0.11f, 0.14f), 0.08f);
                }

                MeshFilter mf = mapGround.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    if (mf.sharedMesh.name.Contains("Plane") &&
                        (mapGround.transform.localScale.x < 10f || mapGround.transform.localScale.z < 10f))
                    {
                        mapGround.transform.localScale = new Vector3(13f, 1f, 11f);
                    }
                    else if (mf.sharedMesh.name.Contains("Cube") &&
                             (mapGround.transform.localScale.x < 60f || mapGround.transform.localScale.z < 60f))
                    {
                        mapGround.transform.localScale = new Vector3(88f, 0.5f, 78f);
                    }
                }
            }

            GameObject ocean = GameObject.Find("OceanWater");
            if (ocean != null && ocean.TryGetComponent(out Renderer oceanRend))
            {
                MaterialHelper.ApplyColor(oceanRend, new Color(0.05f, 0.06f, 0.08f), 0.08f);
            }

            Renderer[] allRenderers = FindObjectsByType<Renderer>();
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer rend = allRenderers[i];
                if (rend == null || rend.GetComponent<TextMesh>() != null)
                    continue;

                if (rend.gameObject.name == "MapGround")
                {
                    MaterialHelper.ApplyColor(rend, new Color(0.10f, 0.11f, 0.14f), 0.08f);
                }
                else if (rend.gameObject.name == "CityPlaza")
                {
                    MaterialHelper.ApplyColor(rend, new Color(0.28f, 0.31f, 0.36f), 0.12f);
                }
                else if (rend is MeshRenderer || rend is SkinnedMeshRenderer)
                {
                    // Never overwrite already URP-valid (e.g. textured .fbx) materials on CitiesRoot/UnitsRoot
                    if (MaterialHelper.IsValidForActivePipeline(rend.sharedMaterial))
                        continue;

                    MaterialHelper.ApplyColor(rend, new Color(0.38f, 0.42f, 0.48f), 0.15f);
                }
            }
        }
    }
}
