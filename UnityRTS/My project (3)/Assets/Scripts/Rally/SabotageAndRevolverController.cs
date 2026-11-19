using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.Rally
{
    /// <summary>
    /// Handles Verbal Sabotage (penalty-free), Physical Sabotage (brawl + 5-person Security Squad arrest),
    /// and the comedic 5-suspect Revolver Duel mechanic.
    /// </summary>
    public class SabotageAndRevolverController : MonoBehaviour
    {
        public static readonly string[] DefaultSuspectPersonas =
        {
            "Şüpheli #1: Bıyıklı Sahte Muhabir",
            "Şüpheli #2: Megafonlu Gizli Anketör",
            "Şüpheli #3: Kılık Değiştirmiş Çaycı",
            "Şüpheli #4: Gürültücü Sahte Taraftar",
            "Şüpheli #5: Pelerinli Broşürcü"
        };

        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private CrowdReactionController _crowdReaction;

        private string _verbalSabotageText = string.Empty;
        private bool _physicalBrawlTriggered;
        private bool _saboteurCaughtBySecurity;
        private float _physicalDisruptionRatio;
        private int _guiltySuspectIndex = -1;
        private bool _revolverResolved;
        private string _lastOutcomeMessage = string.Empty;

        public string VerbalSabotageText => _verbalSabotageText;
        public bool PhysicalBrawlTriggered => _physicalBrawlTriggered;
        public bool SaboteurCaughtBySecurity => _saboteurCaughtBySecurity;
        public float PhysicalDisruptionRatio => _physicalDisruptionRatio;
        public int GuiltySuspectIndex => _guiltySuspectIndex;
        public bool RevolverResolved => _revolverResolved;
        public string LastOutcomeMessage => _lastOutcomeMessage;

        private void Awake()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_crowdReaction == null)
                _crowdReaction = FindAnyObjectByType<CrowdReactionController>();
        }

        public void ResetForNewRally()
        {
            _verbalSabotageText = string.Empty;
            _physicalBrawlTriggered = false;
            _saboteurCaughtBySecurity = false;
            _physicalDisruptionRatio = 0f;
            _guiltySuspectIndex = -1;
            _revolverResolved = false;
            _lastOutcomeMessage = string.Empty;
        }

        /// <summary>
        /// Verbal Sabotage carries ZERO arrest/revolver penalty (OurGame.md Section 6).
        /// </summary>
        public bool SubmitVerbalSabotage(string saboteurPlayerId, string counterArgument)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.RALLY_SPEECH)
                return false;

            string expectedSaboteur = _gameManager.GetSaboteurForRound(_gameManager.RoundNumber);
            if (PlayerIdentity.Normalize(saboteurPlayerId) != expectedSaboteur)
                return false;

            _verbalSabotageText = counterArgument ?? string.Empty;
            _gameManager.RecordPublicEvent(
                "VERBAL_SABOTAGE",
                $"{PlayerIdentity.GetDisplayName(expectedSaboteur)} kalabalık içinden sözlü karşı argüman başlattı (cezasız).",
                CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            return true;
        }

        /// <summary>
        /// Physical Sabotage triggers a cartoonish crowd brawl that damages rally yield,
        /// while the Speaker's 5-person Security Squad attempts to catch the infiltrator.
        /// </summary>
        public bool TriggerPhysicalSabotage(string saboteurPlayerId)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.RALLY_SPEECH)
                return false;

            if (_physicalBrawlTriggered)
                return false;

            string expectedSaboteur = _gameManager.GetSaboteurForRound(_gameManager.RoundNumber);
            string expectedSpeaker = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (PlayerIdentity.Normalize(saboteurPlayerId) != expectedSaboteur)
                return false;

            _physicalBrawlTriggered = true;

            // Friendly Bodyguards (Koruma) at the rally city mitigate physical disruption & boost Security Squad arrest odds
            int speakerBodyguards = CountPlayerBodyguardsAtCity(expectedSpeaker, _gameManager.ActiveRallyCity);
            float protectionDiscount = Mathf.Min(0.18f, speakerBodyguards * 0.06f);
            _physicalDisruptionRatio = Mathf.Clamp(0.40f - protectionDiscount, 0.15f, 0.45f);

            // 5-person Security Squad arrest check (65% base + 10% per Bodyguard in rally city, max 95%)
            float arrestChance = Mathf.Clamp(0.65f + speakerBodyguards * 0.10f, 0.65f, 0.95f);
            float roll = (_gameManager.PickRandomSuspectIndex(100)) / 100f;
            _saboteurCaughtBySecurity = roll < arrestChance;

            if (_saboteurCaughtBySecurity)
            {
                _guiltySuspectIndex = _gameManager.PickRandomSuspectIndex(5);
                _lastOutcomeMessage = "Fiziksel kavga çıktı! 5 kişilik Güvenlik Ekibi sabotajcıyı yakalayıp 5 şüpheli arasına koydu!";
            }
            else
            {
                _lastOutcomeMessage = "Fiziksel kavga mitingi sarstı, ancak sabotajcı kalabalıkta izini kaybettirdi!";
            }

            if (_crowdReaction != null)
                _crowdReaction.ApplyPhysicalBrawlReaction();

            if (RallyStageVisualController.Instance != null)
                RallyStageVisualController.Instance.TriggerPhysicalSabotageBrawl(_saboteurCaughtBySecurity);

            GameAudioManager.EnsureInstance().PlayBrawl();

            _gameManager.RecordPublicEvent(
                "PHYSICAL_SABOTAGE",
                _lastOutcomeMessage,
                CityCatalog.ToKey(_gameManager.ActiveRallyCity));

            return true;
        }

        private static int CountPlayerBodyguardsAtCity(string playerId, CityId cityId)
        {
            int count = 0;
            IReadOnlyList<CampaignUnit> units = CampaignUnit.ActiveUnits;
            for (int i = 0; i < units.Count; i++)
            {
                CampaignUnit u = units[i];
                if (u != null && u.IsAlive && u.OwnerPlayerId == playerId &&
                    u.UnitType == UnitType.Bodyguard && u.CurrentCity == cityId)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Comedic non-graphic Revolver Duel resolution among 5 suspects.
        /// Hit Saboteur -> opponent's next speaking turn is blocked!
        /// Miss (hit innocent suspect) -> Speaker suffers severe Overall & city reputation penalty!
        /// </summary>
        public bool ResolveRevolverShot(string speakerPlayerId, int suspectIndex)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.REVOLVER_DUEL || _revolverResolved)
                return false;

            string expectedSpeaker = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (PlayerIdentity.Normalize(speakerPlayerId) != expectedSpeaker)
                return false;

            if (suspectIndex < 0 || suspectIndex >= 5)
                return false;

            _revolverResolved = true;
            string opponentId = PlayerIdentity.GetOpponentId(expectedSpeaker);
            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;

            bool hitGuilty = suspectIndex == _guiltySuspectIndex;
            if (RallyStageVisualController.Instance != null)
                RallyStageVisualController.Instance.PlayRevolverOutcome(suspectIndex, hitGuilty);
            GameAudioManager.EnsureInstance().PlayRevolverShot(hitGuilty);

            if (hitGuilty)
            {
                PlayerState opponent = _gameManager.GetPlayerState(opponentId);
                opponent.isNextSpeechBlocked = true;
                _lastOutcomeMessage = $"TAM İSABET! ({DefaultSuspectPersonas[suspectIndex]}) — Rakibin bir sonraki konuşma hakkı bloke edildi!";
                _gameManager.RecordPublicEvent("REVOLVER_HIT", _lastOutcomeMessage, CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            }
            else
            {
                if (_gameManager.OverallManager != null)
                    _gameManager.OverallManager.ApplyReputationPenalty(expectedSpeaker, 12f);

                if (city != null)
                {
                    if (PlayerIdentity.ToSlot(expectedSpeaker) == PlayerSlot.PlayerA)
                        city.playerARallySpeechScore = Mathf.Max(0f, city.playerARallySpeechScore - 20f);
                    else
                        city.playerBRallySpeechScore = Mathf.Max(0f, city.playerBRallySpeechScore - 20f);
                }

                _lastOutcomeMessage = $"YANLIŞ ŞÜPHELİ! ({DefaultSuspectPersonas[suspectIndex]} masum çıktı!) Konuşmacı ağır siyasi itibar kaybı yaşadı (-12 Overall).";
                _gameManager.RecordPublicEvent("REVOLVER_MISS", _lastOutcomeMessage, CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            }

            return true;
        }

        public bool HoldFire(string speakerPlayerId)
        {
            if (_gameManager == null || _gameManager.CurrentPhase != TurnPhase.REVOLVER_DUEL || _revolverResolved)
                return false;

            string expectedSpeaker = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (PlayerIdentity.Normalize(speakerPlayerId) != expectedSpeaker)
                return false;

            _revolverResolved = true;
            _lastOutcomeMessage = "Konuşmacı tetiğe basmadı; düello cezasız kapandı.";
            _gameManager.RecordPublicEvent("REVOLVER_HOLD", _lastOutcomeMessage, CityCatalog.ToKey(_gameManager.ActiveRallyCity));
            return true;
        }
    }
}
