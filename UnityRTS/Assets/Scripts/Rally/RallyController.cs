using System;
using System.Collections.Generic;
using System.IO;
using PoliticalMayhem.AI;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using UnityEngine;

namespace PoliticalMayhem.Rally
{
    /// <summary>
    /// Orchestrates the 6 Rally Rounds (15s City Vote -> 30s Prep -> 60s Speech & Sabotage -> 10s AI Eval -> optional Revolver Duel).
    /// Integrates text + microphone speech-to-text hooks and AI Gateway /api/speech/evaluate.
    /// </summary>
    public class RallyController : MonoBehaviour
    {
        public static RallyController Instance { get; private set; }

        public const float CityVoteDuration = 15f;
        public const float PreparationDuration = 30f;
        public const float SpeechDuration = 60f;
        public const float AiEvalDuration = 10f;

        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private SabotageAndRevolverController _sabotageController;
        [SerializeField] private CrowdReactionController _crowdReaction;
        [SerializeField] private ElectionNightController _electionController;
        [SerializeField] private AiGatewayClient _gatewayClient;

        [Header("Phase Timer Controls")]
        [SerializeField] private bool _pauseTimers = false;
        [SerializeField] private float _phaseTimeRemaining = CityVoteDuration;

        // Current round transcripts & evaluation
        private string _speakerTranscript = string.Empty;
        private SpeechEvalResult _lastSpeechEval;
        private float _lastDonationEarned;
        private bool _evaluationInFlight;
        private bool _evaluationApplied;
        private bool _lastEvalWasFallback;

        // Microphone Speech-to-Text hook state
        private bool _isMicRecording;
        private bool _isTranscribingMic;
        private string _micDevice = string.Empty;
        private float _micRecordStartTime;
        private float _micRmsLevel;
        private float _micPeakLevel;
        private string _micStatus = "Mikrofon hazır (metin veya sesli giriş desteklenir).";
        private AudioClip _micClip;
        private string _pendingMicTranscript = string.Empty;
        private bool _hasUnconsumedMicTranscript;

        public float PhaseTimeRemaining => _phaseTimeRemaining;
        public bool PauseTimers { get => _pauseTimers; set => _pauseTimers = value; }
        public string SpeakerTranscript => _speakerTranscript;
        public SpeechEvalResult LastSpeechEval => _lastSpeechEval;
        public float LastDonationEarned => _lastDonationEarned;
        public bool EvaluationPending => _evaluationInFlight;
        public bool EvaluationApplied => _evaluationApplied;
        public bool LastEvalWasFallback => _lastEvalWasFallback;
        public bool IsMicRecording => _isMicRecording;
        public bool IsRecordingMic => _isMicRecording;
        public bool IsTranscribingMic => _isTranscribingMic;
        public float MicRmsLevel => _micRmsLevel;
        public string MicStatus => _micStatus;

        public bool TryConsumeLatestMicTranscript(out string transcript)
        {
            if (_hasUnconsumedMicTranscript && !string.IsNullOrWhiteSpace(_pendingMicTranscript))
            {
                transcript = _pendingMicTranscript;
                _pendingMicTranscript = string.Empty;
                _hasUnconsumedMicTranscript = false;
                return true;
            }
            transcript = string.Empty;
            return false;
        }

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

        private void OnEnable()
        {
            ResolveReferences();
            if (_gameManager != null)
                _gameManager.OnMatchInitialized += HandleMatchInitialized;
        }

        private void OnDisable()
        {
            if (_gameManager != null)
                _gameManager.OnMatchInitialized -= HandleMatchInitialized;
            StopMicrophoneCapture(triggerTranscribe: false);
        }

        private void ResolveReferences()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_sabotageController == null)
                _sabotageController = GetComponent<SabotageAndRevolverController>() ?? FindAnyObjectByType<SabotageAndRevolverController>();
            if (_crowdReaction == null)
                _crowdReaction = FindAnyObjectByType<CrowdReactionController>();
            if (_electionController == null)
                _electionController = FindAnyObjectByType<ElectionNightController>();
            if (_gatewayClient == null)
                _gatewayClient = FindAnyObjectByType<AiGatewayClient>();
        }

        private void HandleMatchInitialized()
        {
            BeginRound(1);
        }

        public void BeginRound(int roundNumber)
        {
            ResolveReferences();
            if (_gameManager == null) return;

            _gameManager.ResetRoundUnitCounters();
            if (_sabotageController != null)
                _sabotageController.ResetForNewRally();

            _speakerTranscript = string.Empty;
            _lastSpeechEval = null;
            _lastDonationEarned = 0f;
            _evaluationInFlight = false;
            _evaluationApplied = false;
            _lastEvalWasFallback = false;
            _hasUnconsumedMicTranscript = false;
            _pendingMicTranscript = string.Empty;
            StopMicrophoneCapture(triggerTranscribe: false);

            _phaseTimeRemaining = CityVoteDuration;
            _gameManager.SetRoundAndPhase(roundNumber, TurnPhase.CITY_VOTE);

            string speaker = PlayerIdentity.GetDisplayName(_gameManager.GetSpeakerForRound(roundNumber));
            string saboteur = PlayerIdentity.GetDisplayName(_gameManager.GetSaboteurForRound(roundNumber));
            _gameManager.RecordPublicEvent(
                "ROUND_START",
                $"Miting Turu {roundNumber}/6 başladı. Konuşmacı: {speaker}, Sabotajcı: {saboteur}.",
                string.Empty);
        }

        private void Update()
        {
            if (_isMicRecording)
                SampleLiveMicrophoneLevel();

            if (_gameManager == null || !_gameManager.IsRallyRoundPhase()) return;
            if (_pauseTimers) return;

            if (_gameManager.CurrentPhase == TurnPhase.REVOLVER_DUEL)
            {
                if (_sabotageController != null && _sabotageController.RevolverResolved)
                {
                    _phaseTimeRemaining -= Time.deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                        CompleteCurrentRoundAndAdvance();
                }
                return;
            }

            if (_gameManager.CurrentPhase == TurnPhase.AI_EVALUATION && _evaluationInFlight)
            {
                if (_phaseTimeRemaining > 0f)
                    _phaseTimeRemaining = Mathf.Max(0f, _phaseTimeRemaining - Time.deltaTime);
                return;
            }

            _phaseTimeRemaining -= Time.deltaTime;
            if (_phaseTimeRemaining <= 0f)
            {
                AdvancePhase();
            }
        }

        public bool SubmitCityVote(string playerId, CityId cityId)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.CITY_VOTE)
                return false;

            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(cityId) : null;
            if (city == null || city.rallyDone)
                return false;

            PlayerState player = _gameManager.GetPlayerState(playerId);
            player.cityVoteChoice = CityCatalog.ToKey(cityId);

            _gameManager.RecordPublicEvent(
                "CITY_VOTE",
                $"{PlayerIdentity.GetDisplayName(playerId)} miting şehri olarak {city.displayName} için oy kullandı.",
                city.cityName);
            return true;
        }

        public bool CastCityVote(string playerId, CityId cityId)
        {
            return SubmitCityVote(playerId, cityId);
        }

        public void SetLiveSpeakerTranscript(string text)
        {
            _speakerTranscript = text ?? string.Empty;
            if (_gameManager != null && _gameManager.CurrentPhase == TurnPhase.RALLY_SPEECH && _crowdReaction != null)
            {
                CityState city = _gameManager.CityManager != null
                    ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity)
                    : null;
                string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
                IdeologyScoresDto speakerOverall = _gameManager.OverallManager != null
                    ? _gameManager.OverallManager.GetOverall(speakerId)
                    : null;

                _crowdReaction.UpdateLiveSentimentDuringSpeech(
                    _speakerTranscript,
                    city != null ? city.ideologyDistribution : null,
                    speakerOverall);
            }
        }

        public void SubmitRallySpeech(string playerId, string transcript)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.RALLY_SPEECH)
                return;

            string expectedSpeaker = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (PlayerIdentity.Normalize(playerId) != expectedSpeaker)
                return;

            SetLiveSpeakerTranscript(transcript);
            _gameManager.RecordPublicSpeechActivity(expectedSpeaker, _gameManager.ActiveRallyCity, _speakerTranscript);
            AdvancePhase();
        }

        public void ForceAdvancePhase()
        {
            AdvancePhase();
        }

        public void AdvancePhase()
        {
            if (_gameManager == null) return;

            switch (_gameManager.CurrentPhase)
            {
                case TurnPhase.CITY_VOTE:
                    ResolveCityVoteAndStartPrep();
                    break;

                case TurnPhase.PREPARATION:
                    StartRallySpeechPhase();
                    break;

                case TurnPhase.RALLY_SPEECH:
                    StartAiEvaluationPhase();
                    break;

                case TurnPhase.AI_EVALUATION:
                    if (!_evaluationApplied && !_evaluationInFlight)
                        TriggerSpeechEvaluation();
                    if (_evaluationApplied)
                        FinishAiEvaluationPhase();
                    break;

                case TurnPhase.REVOLVER_DUEL:
                    if (_sabotageController != null && !_sabotageController.RevolverResolved)
                        _sabotageController.HoldFire(_gameManager.GetSpeakerForRound(_gameManager.RoundNumber));
                    CompleteCurrentRoundAndAdvance();
                    break;
            }
        }

        private void ResolveCityVoteAndStartPrep()
        {
            List<CityState> remaining = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetRemainingRallyCities()
                : new List<CityState>();

            if (remaining.Count == 0)
            {
                TransitionToElectionNight();
                return;
            }

            CityId chosenCity = remaining[0].cityId;
            bool hasVoteA = CityCatalog.TryParse(_gameManager.PlayerA.cityVoteChoice, out CityId voteA) &&
                            !_gameManager.CityManager.GetCity(voteA).rallyDone;
            bool hasVoteB = CityCatalog.TryParse(_gameManager.PlayerB.cityVoteChoice, out CityId voteB) &&
                            !_gameManager.CityManager.GetCity(voteB).rallyDone;

            if (hasVoteA && hasVoteB)
                chosenCity = voteA == voteB ? voteA : _gameManager.PickRandomCandidate(voteA, voteB);
            else if (hasVoteA)
                chosenCity = voteA;
            else if (hasVoteB)
                chosenCity = voteB;

            _gameManager.SetActiveRallyCity(chosenCity);
            _phaseTimeRemaining = PreparationDuration;
            _gameManager.SetRoundAndPhase(_gameManager.RoundNumber, TurnPhase.PREPARATION);

            _gameManager.RecordPublicEvent(
                "CITY_SELECTED",
                $"Tur {_gameManager.RoundNumber} mitingi {CityCatalog.GetDisplayName(chosenCity)} şehrinde yapılacak! 30s Hazırlık başladı.",
                CityCatalog.ToKey(chosenCity));
        }

        private void StartRallySpeechPhase()
        {
            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            PlayerState speaker = _gameManager.GetPlayerState(speakerId);

            if (_crowdReaction != null)
            {
                _crowdReaction.PrepareCrowdAtCity(
                    _gameManager.ActiveRallyCity,
                    _gameManager.CityManager,
                    _gameManager.OverallManager,
                    speakerId);
            }

            _phaseTimeRemaining = SpeechDuration;
            _gameManager.SetRoundAndPhase(_gameManager.RoundNumber, TurnPhase.RALLY_SPEECH);

            if (speaker.isNextSpeechBlocked)
            {
                _gameManager.RecordPublicEvent(
                    "SPEECH_BLOCKED",
                    $"{PlayerIdentity.GetDisplayName(speakerId)} önceki Revolver düellosunda vurulduğu için bu tur konuşma hakkı BLOKE edildi!",
                    CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            }
        }

        private void StartAiEvaluationPhase()
        {
            StopMicrophoneCapture(triggerTranscribe: false);
            _phaseTimeRemaining = AiEvalDuration;
            _gameManager.SetRoundAndPhase(_gameManager.RoundNumber, TurnPhase.AI_EVALUATION);

            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (!string.IsNullOrWhiteSpace(_speakerTranscript))
            {
                _gameManager.RecordPublicSpeechActivity(speakerId, _gameManager.ActiveRallyCity, _speakerTranscript);
            }

            TriggerSpeechEvaluation();
        }

        private void TriggerSpeechEvaluation()
        {
            if (_evaluationInFlight || _evaluationApplied) return;

            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            PlayerState speaker = _gameManager.GetPlayerState(speakerId);
            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;

            if (speaker.isNextSpeechBlocked)
            {
                // Speaker's speaking turn was blocked by Revolver hit
                SpeechEvalResult blockedEval = new SpeechEvalResult
                {
                    speech_effectiveness = 0f,
                    ideology_scores = speaker.overall.Clone(),
                    persuasion_impact = 0f,
                    sabotage_effectiveness = 0f,
                    crowd_reaction = "BOO",
                    argument_quality = 0f,
                    consistency_score = 50f,
                    feedback_summary = "Konuşmacının sırası Revolver düellosu nedeniyle bloke edildi; bağış ve konuşma puanı alınamadı."
                };
                ApplyEvaluationResult(blockedEval, isFallback: true);
                return;
            }

            SpeechEvaluateRequest req = new SpeechEvaluateRequest
            {
                match_id = _gameManager.MatchId,
                player_id = speakerId,
                round_number = _gameManager.RoundNumber,
                city_name = city != null ? city.cityName : CityCatalog.ToKey(_gameManager.ActiveRallyCity),
                phase = TurnPhase.RALLY_SPEECH.ToString(),
                speech_type = "rally",
                speaker_transcript = string.IsNullOrWhiteSpace(_speakerTranscript)
                    ? $"{speaker.selectedSpeechStrategy} odaklı kısa miting konuşması."
                    : _speakerTranscript,
                saboteur_transcript = _sabotageController != null ? _sabotageController.VerbalSabotageText : string.Empty,
                city_ideologies = city != null ? city.ideologyDistribution.Clone() : new IdeologyScoresDto(),
                speaker_overall = speaker.overall.Clone()
            };

            _evaluationInFlight = true;
            if (_gatewayClient != null)
            {
                _gatewayClient.EvaluateSpeech(req, (result, isFallback) =>
                {
                    _evaluationInFlight = false;
                    ApplyEvaluationResult(result, isFallback);
                });
            }
            else
            {
                _evaluationInFlight = false;
                ApplyEvaluationResult(AiGatewayClient.BuildDeterministicSpeechFallback(req), true);
            }
        }

        private void ApplyEvaluationResult(SpeechEvalResult eval, bool isFallback)
        {
            if (_evaluationApplied || _gameManager == null) return;
            _evaluationApplied = true;
            _lastSpeechEval = eval;
            _lastEvalWasFallback = isFallback;

            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            PlayerState speaker = _gameManager.GetPlayerState(speakerId);
            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;

            float physicalRatio = _sabotageController != null ? _sabotageController.PhysicalDisruptionRatio : 0f;
            float verbalPenalty = Mathf.Clamp01((eval.sabotage_effectiveness / 100f) * 0.25f);
            float cheerRes = _gameManager.OverallManager != null ? _gameManager.OverallManager.GetCheerSabotageResistance(speakerId) : 0f;
            float totalSabotagePenalty = Mathf.Clamp01(Mathf.Max(0f, physicalRatio + verbalPenalty - cheerRes));

            if (!speaker.isNextSpeechBlocked && _gameManager.OverallManager != null)
            {
                List<string> unlocked = _gameManager.OverallManager.ApplySpeechEvaluation(speakerId, eval.ideology_scores);
                for (int i = 0; i < unlocked.Count; i++)
                {
                    _gameManager.RecordPublicEvent(
                        "TECH_UNLOCKED",
                        $"{PlayerIdentity.GetDisplayName(speakerId)} yeni ideolojik doktrin açtı: {unlocked[i]}",
                        city != null ? city.cityName : string.Empty);
                }
            }

            if (_gameManager.PartyBudgetManager != null && city != null)
            {
                _lastDonationEarned = _gameManager.PartyBudgetManager.AwardRallyDonations(
                    speakerId,
                    city,
                    eval,
                    _gameManager.OverallManager,
                    totalSabotagePenalty);
            }

            if (city != null)
            {
                float netSpeechScore = speaker.isNextSpeechBlocked
                    ? 0f
                    : Mathf.Clamp(eval.speech_effectiveness * (1f - physicalRatio * 0.5f), 0f, 100f);

                if (PlayerIdentity.ToSlot(speakerId) == PlayerSlot.PlayerA)
                {
                    city.playerARallySpeechScore = netSpeechScore;
                    city.playerASabotageImpact = Mathf.Clamp(eval.sabotage_effectiveness + physicalRatio * 100f, 0f, 100f);
                }
                else
                {
                    city.playerBRallySpeechScore = netSpeechScore;
                    city.playerBSabotageImpact = Mathf.Clamp(eval.sabotage_effectiveness + physicalRatio * 100f, 0f, 100f);
                }
            }

            // Clear block after this speaker turn concludes
            speaker.isNextSpeechBlocked = false;

            if (_crowdReaction != null)
            {
                float recoveryBonus = _gameManager.OverallManager != null
                    ? _gameManager.OverallManager.GetCrowdBooRecoveryBonus(speakerId)
                    : 0f;
                _crowdReaction.ApplySpeechEvaluation(eval, recoveryBonus);
            }

            _gameManager.RecordPublicEvent(
                "AI_EVALUATION",
                $"{PlayerIdentity.GetDisplayName(speakerId)} konuşması değerlendirildi: Etkililik {eval.speech_effectiveness:F1}/100, Tepki {eval.crowd_reaction}, Bağış +{_lastDonationEarned:N0}.",
                city != null ? city.cityName : string.Empty);
        }

        private void FinishAiEvaluationPhase()
        {
            if (_sabotageController != null && _sabotageController.SaboteurCaughtBySecurity)
            {
                _phaseTimeRemaining = 20f;
                _gameManager.SetRoundAndPhase(_gameManager.RoundNumber, TurnPhase.REVOLVER_DUEL);
                _gameManager.RecordPublicEvent(
                    "REVOLVER_DUEL",
                    "Yakalanan sabotajcı 5 şüphelinin arasında! Konuşmacı Revolver ile tahmin yapabilir.",
                    CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            }
            else
            {
                CompleteCurrentRoundAndAdvance();
            }
        }

        private void CompleteCurrentRoundAndAdvance()
        {
            _gameManager.MarkCurrentRallyCompleted();

            if (_gameManager.RoundNumber < 6)
            {
                BeginRound(_gameManager.RoundNumber + 1);
            }
            else
            {
                TransitionToElectionNight();
            }
        }

        private void TransitionToElectionNight()
        {
            if (_electionController != null)
            {
                _electionController.BeginElectionNight();
            }
            else
            {
                _gameManager.ResolveMatchVictory();
            }
        }

        // --- Microphone Speech-to-Text Pipeline ---
        public void ToggleMicrophoneCapture()
        {
            if (_isMicRecording)
                StopMicrophoneCapture(triggerTranscribe: true);
            else
                StartMicrophoneCapture();
        }

        public void StartMicrophoneCapture()
        {
            if (_isMicRecording || _isTranscribingMic) return;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                _isMicRecording = false;
                _micStatus = "Mikrofon cihazı bulunamadı — metin konuşma girişi aktif.";
                return;
            }

            try
            {
                _micDevice = Microphone.devices[0];
                _micClip = Microphone.Start(_micDevice, false, 60, 16000);
                _micRecordStartTime = Time.realtimeSinceStartup;
                _micRmsLevel = 0f;
                _micPeakLevel = 0f;
                _isMicRecording = true;
                _micStatus = $"🎙️ Kayıt başlatıldı ({_micDevice})...";
            }
            catch (Exception ex)
            {
                _isMicRecording = false;
                _micStatus = $"Mikrofon başlatılamadı ({ex.Message}); metin girişi kullanın.";
            }
        }

        public void StopMicrophoneCapture(bool triggerTranscribe = true)
        {
            if (!_isMicRecording) return;

            int recordedPos = 0;
            try
            {
                if (!string.IsNullOrEmpty(_micDevice))
                {
                    recordedPos = Microphone.GetPosition(_micDevice);
                    Microphone.End(_micDevice);
                }
                else if (Microphone.devices != null && Microphone.devices.Length > 0)
                {
                    recordedPos = Microphone.GetPosition(Microphone.devices[0]);
                    Microphone.End(Microphone.devices[0]);
                }
            }
            catch
            {
                // Ignore audio hardware cleanup issues in headless environments
            }

            float durationSec = Mathf.Max(0.2f, Time.realtimeSinceStartup - _micRecordStartTime);
            string wavBase64 = string.Empty;
            if (triggerTranscribe && _micClip != null)
            {
                wavBase64 = EncodeClipToWavBase64(_micClip, recordedPos, durationSec, out float clipRms, out float clipPeak);
                if (clipRms > _micRmsLevel) _micRmsLevel = clipRms;
                if (clipPeak > _micPeakLevel) _micPeakLevel = clipPeak;
            }

            if (_micClip != null)
            {
                Destroy(_micClip);
                _micClip = null;
            }
            _isMicRecording = false;

            if (!triggerTranscribe)
            {
                _micStatus = "Mikrofon durduruldu.";
                return;
            }

            DispatchSpeechTranscription(durationSec, _micRmsLevel, wavBase64);
        }

        private void SampleLiveMicrophoneLevel()
        {
            if (_micClip == null || string.IsNullOrEmpty(_micDevice)) return;

            try
            {
                int pos = Microphone.GetPosition(_micDevice);
                int window = Mathf.Min(512, _micClip.samples);
                if (pos > 0 && window > 0)
                {
                    int channels = Mathf.Max(1, _micClip.channels);
                    int startSample = (pos - window + _micClip.samples) % _micClip.samples;
                    float[] buf = new float[window * channels];
                    _micClip.GetData(buf, startSample);

                    double sumSq = 0.0;
                    float peak = 0f;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        float s = buf[i];
                        float abs = Mathf.Abs(s);
                        if (abs > peak) peak = abs;
                        sumSq += s * s;
                    }
                    _micRmsLevel = Mathf.Sqrt((float)(sumSq / buf.Length));
                    if (peak > _micPeakLevel) _micPeakLevel = peak;
                }
            }
            catch
            {
                // Keep previous level if hardware position query fails
            }

            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _micRecordStartTime);
            int bars = Mathf.Clamp(Mathf.RoundToInt(_micRmsLevel * 40f), 0, 10);
            string meter = new string('|', bars) + new string('.', 10 - bars);
            _micStatus = $"🎙️ Kayıt ({elapsed:F1}s) [{meter}] Seviye:{_micRmsLevel:F2} (Pik:{_micPeakLevel:F2})";
        }

        private void DispatchSpeechTranscription(float durationSec, float rmsLevel, string wavBase64)
        {
            CityState city = _gameManager != null && _gameManager.CityManager != null
                ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity)
                : null;
            string speakerId = _gameManager != null
                ? _gameManager.GetSpeakerForRound(_gameManager.RoundNumber)
                : PlayerIdentity.PlayerAId;
            string topIdeology = city != null && city.ideologyDistribution != null
                ? IdeologyCodec.ToKey(city.ideologyDistribution.GetDominantIdeology())
                : "democrat";

            SpeechTranscribeRequest req = new SpeechTranscribeRequest
            {
                match_id = _gameManager != null ? _gameManager.MatchId : "pm-match-01",
                player_id = speakerId,
                round_number = _gameManager != null ? _gameManager.RoundNumber : 1,
                city_name = city != null ? city.cityName : CityCatalog.ToKey(CityId.Baskent),
                top_ideology = topIdeology,
                duration_seconds = durationSec,
                rms_level = rmsLevel,
                audio_wav_base64 = wavBase64 ?? string.Empty,
                language = "tr"
            };

            if (_gatewayClient != null)
            {
                _isTranscribingMic = true;
                _micStatus = $"🎙️ Ses metne çevriliyor ({durationSec:F1}s, {req.city_name})...";
                _gatewayClient.TranscribeSpeech(req, (transcript, isFallback) =>
                {
                    _isTranscribingMic = false;
                    ApplyTranscribedSpeech(transcript, isFallback);
                });
            }
            else
            {
                ApplyTranscribedSpeech(AiGatewayClient.BuildDeterministicTranscribeFallback(req), true);
            }
        }

        private void ApplyTranscribedSpeech(string transcript, bool isFallback)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                _micStatus = "🎙️ Ses metne çevrilemedi; metin girişini kullanın.";
                return;
            }

            string cleaned = transcript.Trim();
            _pendingMicTranscript = cleaned;
            _hasUnconsumedMicTranscript = true;
            SetLiveSpeakerTranscript(string.IsNullOrWhiteSpace(_speakerTranscript)
                ? cleaned
                : $"{_speakerTranscript.TrimEnd()} {cleaned}");
            _micStatus = isFallback
                ? "🎙️ Ses transkripti eklendi (Offline şehir uyumlu mod)."
                : "🎙️ Ses başarıyla metne çevrildi!";
        }

        private static string EncodeClipToWavBase64(
            AudioClip clip,
            int recordedSamplePos,
            float durationSec,
            out float rms,
            out float peak)
        {
            rms = 0f;
            peak = 0f;
            if (clip == null || clip.samples <= 0) return string.Empty;

            int sampleRate = Mathf.Max(8000, clip.frequency);
            int channels = Mathf.Max(1, clip.channels);
            int targetSamples = recordedSamplePos > 0
                ? Mathf.Min(recordedSamplePos, clip.samples)
                : Mathf.Min(clip.samples, Mathf.RoundToInt(durationSec * sampleRate));
            int maxSamples = sampleRate * 20; // Cap at 20s (~640KB WAV) so payload stays compact
            int monoCount = Mathf.Clamp(targetSamples, 1, Mathf.Min(clip.samples, maxSamples));

            float[] raw = new float[monoCount * channels];
            clip.GetData(raw, 0);

            double sumSq = 0.0;
            using (MemoryStream ms = new MemoryStream(44 + monoCount * 2))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                int dataBytes = monoCount * 2;
                bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)1); // Mono
                bw.Write(sampleRate);
                bw.Write(sampleRate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                bw.Write(dataBytes);

                for (int i = 0; i < monoCount; i++)
                {
                    float mono = 0f;
                    int baseIdx = i * channels;
                    for (int c = 0; c < channels; c++)
                        mono += raw[baseIdx + c];
                    mono /= channels;

                    float abs = Mathf.Abs(mono);
                    if (abs > peak) peak = abs;
                    sumSq += mono * mono;

                    short pcm = (short)Mathf.RoundToInt(Mathf.Clamp(mono, -1f, 1f) * 32767f);
                    bw.Write(pcm);
                }

                rms = Mathf.Sqrt((float)(sumSq / monoCount));
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }
}
