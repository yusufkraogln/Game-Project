using System.Collections.Generic;
using PoliticalMayhem.AI;
using PoliticalMayhem.Core;
using PoliticalMayhem.Rally;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.Election
{
    /// <summary>
    /// Controls Election Night sequentially across all 6 cities (15s Final Speech -> Secret Bribery -> Deterministic Vote)
    /// and concludes with the 60-second celebratory Balcony Speech.
    /// </summary>
    public class ElectionNightController : MonoBehaviour
    {
        public static ElectionNightController Instance { get; private set; }

        public const float FinalSpeechDuration = 15f;
        public const float BribePhaseDuration = 20f;
        public const float ResultDisplayDuration = 8f;
        public const float BalconySpeechDuration = 60f;
        public const float MaxBribeVoteShift = 0.02f; // Strictly capped at 2 percentage points (OurGame.md)

        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private AiGatewayClient _gatewayClient;
        [SerializeField] private CrowdReactionController _crowdReaction;

        [Header("Election Night Controls")]
        [SerializeField] private bool _pauseTimers = false;
        [SerializeField] private float _phaseTimeRemaining;

        private int _currentCityIdx;
        private string _finalSpeechA = string.Empty;
        private string _finalSpeechB = string.Empty;
        private bool _finalEvalStarted;
        private bool _finalEvalInFlight;
        private bool _finalEvalDoneA;
        private bool _finalEvalDoneB;

        private float _pendingBribeA;
        private float _pendingBribeB;
        private bool _bribeLockedA;
        private bool _bribeLockedB;

        private ElectionCityResult _lastCityResult;
        private string _balconySpeechLog = string.Empty;
        private bool _balconyFinished;

        private static readonly string[] BalconyCheerSlogans =
        {
            "HALK MEYDANDA COŞTU! 'BÜYÜK LİDER! BÜYÜK LİDER!'",
            "KONFETİLER GÖKYÜZÜNÜ KAPLADI! KALABALIK ALKIŞTAN YIKILIYOR!",
            "MUHALEFET BİLE ALKIŞLIYOR! TARİHİ BALKON KONUŞMASI!",
            "MEŞALELER YANDI, DAVULLAR ÇALIYOR: 'ŞAMPİYON BAŞKAN!'"
        };

        public bool PauseTimers { get => _pauseTimers; set => _pauseTimers = value; }
        public float PhaseTimeRemaining => _phaseTimeRemaining;
        public int CurrentCityIndex => _currentCityIdx;
        public ElectionCityResult LastCityResult => _lastCityResult;
        public string BalconySpeechLog => _balconySpeechLog;
        public bool BalconyFinished => _balconyFinished;
        public bool BribeLockedA => _bribeLockedA;
        public bool BribeLockedB => _bribeLockedB;
        public bool IsBribeLocked(string playerId) =>
            PlayerIdentity.Normalize(playerId) == PlayerIdentity.PlayerBId ? _bribeLockedB : _bribeLockedA;
        public float PendingBribeA => _pendingBribeA;
        public float PendingBribeB => _pendingBribeB;

        public CityId CurrentElectionCityId =>
            (_currentCityIdx >= 0 && _currentCityIdx < CityCatalog.All.Length)
                ? CityCatalog.All[_currentCityIdx]
                : CityId.Baskent;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            ResolveReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void ResolveReferences()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_gatewayClient == null)
                _gatewayClient = FindAnyObjectByType<AiGatewayClient>();
            if (_crowdReaction == null)
                _crowdReaction = FindAnyObjectByType<CrowdReactionController>();
        }

        public bool IsElectionPhase()
        {
            if (_gameManager == null) return false;
            return _gameManager.CurrentPhase == TurnPhase.ELECTION_FINAL_SPEECH
                || _gameManager.CurrentPhase == TurnPhase.ELECTION_BRIBE
                || _gameManager.CurrentPhase == TurnPhase.ELECTION_RESULT
                || _gameManager.CurrentPhase == TurnPhase.BALCONY_SPEECH;
        }

        public void BeginElectionNight()
        {
            ResolveReferences();
            if (_gameManager == null) return;

            _currentCityIdx = 0;
            _lastCityResult = null;
            _balconySpeechLog = string.Empty;
            _balconyFinished = false;
            StartCityFinalSpeech(0);
        }

        private void StartCityFinalSpeech(int cityIdx)
        {
            _currentCityIdx = cityIdx;
            _finalSpeechA = string.Empty;
            _finalSpeechB = string.Empty;
            _finalEvalStarted = false;
            _finalEvalInFlight = false;
            _finalEvalDoneA = false;
            _finalEvalDoneB = false;
            _pendingBribeA = 0f;
            _pendingBribeB = 0f;
            _bribeLockedA = false;
            _bribeLockedB = false;

            CityId cityId = CurrentElectionCityId;
            _phaseTimeRemaining = FinalSpeechDuration;
            // Map 6 Election Night cities into schema-valid round_number 7..12
            _gameManager.SetRoundAndPhase(7 + cityIdx, TurnPhase.ELECTION_FINAL_SPEECH);
            _gameManager.RecordPublicEvent(
                "ELECTION_CITY_START",
                $"Seçim Gecesi ({cityIdx + 1}/6): {CityCatalog.GetDisplayName(cityId)} için 15s son dakika konuşmaları başladı!",
                CityCatalog.ToKey(cityId));
        }

        private void Update()
        {
            if (!IsElectionPhase() || _gameManager == null) return;
            if (_pauseTimers || _balconyFinished) return;

            if (_gameManager.CurrentPhase == TurnPhase.ELECTION_FINAL_SPEECH && _finalEvalInFlight)
            {
                return;
            }

            _phaseTimeRemaining -= Time.deltaTime;
            if (_phaseTimeRemaining <= 0f)
            {
                AdvanceElectionPhase();
            }
        }

        public void SubmitFinalSpeech(string playerId, string transcript)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.ELECTION_FINAL_SPEECH)
                return;

            string normalized = PlayerIdentity.Normalize(playerId);
            if (normalized == PlayerIdentity.PlayerBId)
                _finalSpeechB = transcript ?? string.Empty;
            else
                _finalSpeechA = transcript ?? string.Empty;

            _gameManager.RecordPublicSpeechActivity(normalized, CurrentElectionCityId, transcript);
        }

        /// <summary>
        /// Secret, non-refundable simultaneous bribe allocation during ELECTION_BRIBE only.
        /// </summary>
        public bool SubmitBribeAllocation(string playerId, CityId cityId, float amount, out string message)
        {
            message = string.Empty;
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.ELECTION_BRIBE)
            {
                message = "Rüşvet yalnızca ELECTION_BRIBE aşamasında harcanabilir.";
                return false;
            }

            if (cityId != CurrentElectionCityId)
            {
                message = $"Şu anda yalnızca {CityCatalog.GetDisplayName(CurrentElectionCityId)} seçimi için rüşvet verilebilir.";
                return false;
            }

            string normalized = PlayerIdentity.Normalize(playerId);
            bool alreadyLocked = normalized == PlayerIdentity.PlayerBId ? _bribeLockedB : _bribeLockedA;
            if (alreadyLocked)
            {
                message = "Bu şehir için rüşvet tutarı zaten onaylandı (geri alınamaz).";
                return false;
            }

            string err = "Parti bütçesi yöneticisi bulunamadı.";
            if (_gameManager.PartyBudgetManager == null ||
                !_gameManager.PartyBudgetManager.TrySpendBribe(normalized, amount, _gameManager.CurrentPhase, out err))
            {
                message = string.IsNullOrEmpty(err) ? "Bütçe harcaması reddedildi." : err;
                return false;
            }

            CityState city = _gameManager.CityManager.GetCity(cityId);
            if (normalized == PlayerIdentity.PlayerBId)
            {
                _pendingBribeB = amount;
                _bribeLockedB = true;
                if (city != null) city.playerBBribeAllocated = amount;
            }
            else
            {
                _pendingBribeA = amount;
                _bribeLockedA = true;
                if (city != null) city.playerABribeAllocated = amount;
            }

            // Do NOT leak exact opponent bribe amount to public feed
            message = $"Gizli seçim bütçesi ayrıldı ({amount:N0}).";
            return true;
        }

        public void SubmitBalconySpeech(string winnerText)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.BALCONY_SPEECH)
                return;

            string slogan = BalconyCheerSlogans[Random.Range(0, BalconyCheerSlogans.Length)];
            _balconySpeechLog = $"\"{winnerText}\"\n-> {slogan}";
            if (_crowdReaction != null)
                _crowdReaction.TriggerBalconyCelebration(_gameManager.CityManager);

            _gameManager.RecordPublicEvent(
                "BALCONY_SPEECH",
                $"{PlayerIdentity.GetDisplayName(_gameManager.MatchWinnerId)} balkon konuşması yaptı: {slogan}",
                "Baskent");
        }

        public void ForceAdvanceElectionPhase()
        {
            AdvanceElectionPhase();
        }

        public void AdvanceElectionPhase()
        {
            if (_gameManager == null) return;

            switch (_gameManager.CurrentPhase)
            {
                case TurnPhase.ELECTION_FINAL_SPEECH:
                    if (!_finalEvalStarted)
                    {
                        EvaluateBothFinalSpeechesAndEnterBribe();
                    }
                    else if (_finalEvalDoneA && _finalEvalDoneB)
                    {
                        EnterBribePhase();
                    }
                    break;

                case TurnPhase.ELECTION_BRIBE:
                    ComputeAndRevealCityElectionResult();
                    break;

                case TurnPhase.ELECTION_RESULT:
                    if (_currentCityIdx + 1 < CityCatalog.All.Length)
                    {
                        StartCityFinalSpeech(_currentCityIdx + 1);
                    }
                    else
                    {
                        _gameManager.ResolveMatchVictory();
                        _phaseTimeRemaining = BalconySpeechDuration;
                        if (_crowdReaction != null)
                            _crowdReaction.TriggerBalconyCelebration(_gameManager.CityManager);
                    }
                    break;

                case TurnPhase.BALCONY_SPEECH:
                    _balconyFinished = true;
                    _gameManager.RecordPublicEvent("MATCH_END", "Balkon konuşması sona erdi. Maç tamamlandı!", "Baskent");
                    break;
            }
        }

        private void EvaluateBothFinalSpeechesAndEnterBribe()
        {
            _finalEvalStarted = true;
            _finalEvalInFlight = true;
            CityState city = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetCity(CurrentElectionCityId)
                : null;

            EvaluateSingleFinalSpeech(PlayerIdentity.PlayerAId, _finalSpeechA, city, score =>
            {
                if (city != null) city.playerAFinalSpeechScore = score;
                _finalEvalDoneA = true;
                if (_finalEvalDoneB)
                {
                    _finalEvalInFlight = false;
                    EnterBribePhase();
                }
            });

            EvaluateSingleFinalSpeech(PlayerIdentity.PlayerBId, _finalSpeechB, city, score =>
            {
                if (city != null) city.playerBFinalSpeechScore = score;
                _finalEvalDoneB = true;
                if (_finalEvalDoneA)
                {
                    _finalEvalInFlight = false;
                    EnterBribePhase();
                }
            });

            if (_finalEvalDoneA && _finalEvalDoneB)
                _finalEvalInFlight = false;
        }

        private void EvaluateSingleFinalSpeech(string playerId, string transcript, CityState city, System.Action<float> onScoreReady)
        {
            PlayerState player = _gameManager.GetPlayerState(playerId);
            SpeechEvaluateRequest req = new SpeechEvaluateRequest
            {
                match_id = _gameManager.MatchId,
                player_id = playerId,
                round_number = _gameManager.RoundNumber,
                city_name = city != null ? city.cityName : CityCatalog.ToKey(CurrentElectionCityId),
                phase = TurnPhase.ELECTION_FINAL_SPEECH.ToString(),
                speech_type = "final_speech",
                speaker_transcript = string.IsNullOrWhiteSpace(transcript)
                    ? $"{player.selectedSpeechStrategy} kapanış çağrısı."
                    : transcript,
                saboteur_transcript = string.Empty,
                city_ideologies = city != null ? city.ideologyDistribution.Clone() : new IdeologyScoresDto(),
                speaker_overall = player.overall.Clone()
            };

            if (_gatewayClient != null)
            {
                _gatewayClient.EvaluateSpeech(req, (eval, _) =>
                {
                    if (_gameManager.OverallManager != null)
                        _gameManager.OverallManager.ApplySpeechEvaluation(playerId, eval.ideology_scores);
                    onScoreReady?.Invoke(eval.speech_effectiveness);
                });
            }
            else
            {
                SpeechEvalResult fallback = AiGatewayClient.BuildDeterministicSpeechFallback(req);
                if (_gameManager.OverallManager != null)
                    _gameManager.OverallManager.ApplySpeechEvaluation(playerId, fallback.ideology_scores);
                onScoreReady?.Invoke(fallback.speech_effectiveness);
            }
        }

        private void EnterBribePhase()
        {
            _finalEvalInFlight = false;
            _phaseTimeRemaining = BribePhaseDuration;
            _gameManager.SetRoundAndPhase(7 + _currentCityIdx, TurnPhase.ELECTION_BRIBE);
            _gameManager.RecordPublicEvent(
                "ELECTION_BRIBE",
                $"{CityCatalog.GetDisplayName(CurrentElectionCityId)} için gizli Rüşvet aşaması açıldı (Maks net etki: ±%2).",
                CityCatalog.ToKey(CurrentElectionCityId));
        }

        private void ComputeAndRevealCityElectionResult()
        {
            CityId cityId = CurrentElectionCityId;
            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(cityId) : null;
            if (city == null) return;

            OverallManager overallMgr = _gameManager.OverallManager;

            // 1. Overall Alignment (30%)
            float overallAlignA = overallMgr != null
                ? overallMgr.ComputeOverallAlignment(PlayerIdentity.PlayerAId, city.ideologyDistribution)
                : 50f;
            float overallAlignB = overallMgr != null
                ? overallMgr.ComputeOverallAlignment(PlayerIdentity.PlayerBId, city.ideologyDistribution)
                : 50f;

            // 2. Rally Speech Score (30%) — non-speaker candidate uses their average speech score so cities remain competitive
            float avgRallyA = ComputeAverageRallySpeechScore(PlayerSlot.PlayerA);
            float avgRallyB = ComputeAverageRallySpeechScore(PlayerSlot.PlayerB);
            float rallyA = city.rallySpeakerSlot == PlayerSlot.PlayerA ? city.playerARallySpeechScore : avgRallyA;
            float rallyB = city.rallySpeakerSlot == PlayerSlot.PlayerB ? city.playerBRallySpeechScore : avgRallyB;

            // 3. Campaign Activity (15%) — accumulated city activity + active unit presence in city
            float activityA = ComputeTotalCampaignActivity(PlayerIdentity.PlayerAId, city);
            float activityB = ComputeTotalCampaignActivity(PlayerIdentity.PlayerBId, city);

            // 4. Sabotage Impact (10%) — relative disruption balance
            float sabotageA = Mathf.Clamp(50f + (city.playerBSabotageImpact - city.playerASabotageImpact) * 0.5f, 0f, 100f);
            float sabotageB = Mathf.Clamp(50f + (city.playerASabotageImpact - city.playerBSabotageImpact) * 0.5f, 0f, 100f);

            // 5. Final Speech (15%)
            float finalA = city.playerAFinalSpeechScore;
            float finalB = city.playerBFinalSpeechScore;

            // Exact OurGame.md Base Support formula:
            // BaseSupport = 0.30*Overall + 0.30*RallySpeech + 0.15*CampaignActivity + 0.10*Sabotage + 0.15*FinalSpeech
            float baseSupportA = Mathf.Max(1f,
                0.30f * overallAlignA +
                0.30f * rallyA +
                0.15f * activityA +
                0.10f * sabotageA +
                0.15f * finalA);

            float baseSupportB = Mathf.Max(1f,
                0.30f * overallAlignB +
                0.30f * rallyB +
                0.15f * activityB +
                0.10f * sabotageB +
                0.15f * finalB);

            float baseShareA = baseSupportA / (baseSupportA + baseSupportB);

            // Diminishing returns bribery curve strictly capped at +/- 2.0 percentage points (0.02f)
            float multA = overallMgr != null ? overallMgr.GetBribeEfficiencyMultiplier(PlayerIdentity.PlayerAId) : 1f;
            float multB = overallMgr != null ? overallMgr.GetBribeEfficiencyMultiplier(PlayerIdentity.PlayerBId) : 1f;
            float curveA = DiminishingBribeCurve(_pendingBribeA * multA);
            float curveB = DiminishingBribeCurve(_pendingBribeB * multB);
            float netBribeShift = Mathf.Clamp((curveA - curveB) * MaxBribeVoteShift, -MaxBribeVoteShift, MaxBribeVoteShift);

            float finalShareA = Mathf.Clamp(baseShareA + netBribeShift, 0.01f, 0.99f);
            float finalShareB = 1.0f - finalShareA;

            int votesA = Mathf.RoundToInt(city.population * finalShareA);
            int votesB = Mathf.Max(0, city.population - votesA);

            city.electionResolved = true;
            city.finalShareA = finalShareA;
            city.finalShareB = finalShareB;
            city.finalVotesA = votesA;
            city.finalVotesB = votesB;
            city.winnerSlot = votesA >= votesB ? PlayerSlot.PlayerA : PlayerSlot.PlayerB;

            string winnerId = PlayerIdentity.FromSlot(city.winnerSlot);
            string headline = $"{city.displayName.ToUpperInvariant()} — {city.population:N0} OY | Oyuncu A: %{finalShareA * 100f:F1} / Oyuncu B: %{finalShareB * 100f:F1}";

            _lastCityResult = new ElectionCityResult
            {
                cityName = city.cityName,
                displayName = city.displayName,
                population = city.population,
                shareA = finalShareA,
                shareB = finalShareB,
                votesA = votesA,
                votesB = votesB,
                bribeA = _pendingBribeA,
                bribeB = _pendingBribeB,
                bribeShiftApplied = netBribeShift,
                winnerPlayerId = winnerId,
                headline = headline
            };

            _gameManager.AddElectionResult(_lastCityResult);
            _gameManager.RecordPublicEvent(
                "ELECTION_RESULT",
                $"{headline} -> Kazanan: {PlayerIdentity.GetDisplayName(winnerId)}",
                city.cityName);

            _phaseTimeRemaining = ResultDisplayDuration;
            _gameManager.SetRoundAndPhase(7 + _currentCityIdx, TurnPhase.ELECTION_RESULT);
        }

        private static float DiminishingBribeCurve(float effectiveAmount)
        {
            if (effectiveAmount <= 0f) return 0f;
            return effectiveAmount / (effectiveAmount + 6000f);
        }

        private float ComputeAverageRallySpeechScore(PlayerSlot slot)
        {
            if (_gameManager == null || _gameManager.CityManager == null) return 50f;
            float sum = 0f;
            int count = 0;
            foreach (CityState c in _gameManager.CityManager.Cities)
            {
                if (c.rallySpeakerSlot == slot)
                {
                    sum += slot == PlayerSlot.PlayerA ? c.playerARallySpeechScore : c.playerBRallySpeechScore;
                    count++;
                }
            }
            return count > 0 ? sum / count : 50f;
        }

        private float ComputeTotalCampaignActivity(string playerId, CityState city)
        {
            float baseActivity = city.GetCampaignActivity(playerId);
            float unitPowerSum = 0f;
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit u = all[i];
                if (u != null && u.IsAlive && u.OwnerPlayerId == playerId && u.CurrentCity == city.cityId)
                {
                    unitPowerSum += u.CampaignPower;
                }
            }
            return Mathf.Clamp(baseActivity + unitPowerSum * 1.25f, 0f, 100f);
        }
    }
}
