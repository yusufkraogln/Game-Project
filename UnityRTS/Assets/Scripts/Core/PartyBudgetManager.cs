using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Implements Political Mayhem's single-currency Party Budget & Donation Economy.
    /// Formula (OurGame.md Section 7):
    ///   Donation = GroupPopulation * DonationTendency * SpeechImpact * OverallAlignment * ScaleFactor
    /// Strictly enforces that party_budget CANNOT be spent during rally rounds (locked until ELECTION_BRIBE).
    /// </summary>
    public class PartyBudgetManager : MonoBehaviour
    {
        [Header("Donation Tuning")]
        [SerializeField] private float _baseScaleFactor = 0.18f;

        private readonly Dictionary<string, PlayerState> _players = new Dictionary<string, PlayerState>();

        public void BindPlayers(PlayerState playerA, PlayerState playerB)
        {
            _players.Clear();
            _players[PlayerIdentity.PlayerAId] = playerA;
            _players[PlayerIdentity.PlayerBId] = playerB;
        }

        public float GetBudget(string playerId)
        {
            string key = PlayerIdentity.Normalize(playerId);
            return _players.TryGetValue(key, out PlayerState p) ? p.partyBudget : 0f;
        }

        /// <summary>
        /// Party Budget is strictly locked during all rally rounds and can ONLY be spent during ELECTION_BRIBE.
        /// </summary>
        public bool CanSpendBudget(TurnPhase currentPhase)
        {
            return currentPhase == TurnPhase.ELECTION_BRIBE;
        }

        public float GetDonationTendency(IdeologyType ideology, string playerId, OverallManager overallManager)
        {
            float baseTendency;
            switch (ideology)
            {
                case IdeologyType.Nationalist: baseTendency = 0.85f; break;
                case IdeologyType.Liberal: baseTendency = 0.95f; break;
                case IdeologyType.Democrat: baseTendency = 0.80f; break;
                case IdeologyType.Communist: baseTendency = 0.70f; break;
                case IdeologyType.Imperialist: baseTendency = 1.05f; break;
                default: baseTendency = 0.80f; break;
            }

            if (overallManager != null)
            {
                if (ideology == IdeologyType.Nationalist && overallManager.HasBonus(playerId, OverallManager.TechNationalist))
                    baseTendency *= 1.12f;
                if (ideology == IdeologyType.Liberal && overallManager.HasBonus(playerId, OverallManager.TechLiberal))
                    baseTendency *= 1.18f;
                if (ideology == IdeologyType.Democrat && overallManager.HasBonus(playerId, OverallManager.TechDemocrat))
                    baseTendency *= 1.10f;
                if (ideology == IdeologyType.Communist && overallManager.HasBonus(playerId, OverallManager.TechCommunist))
                    baseTendency *= 1.15f;
                if (ideology == IdeologyType.Imperialist && overallManager.HasBonus(playerId, OverallManager.TechImperialist))
                    baseTendency *= 1.20f;
            }

            return baseTendency;
        }

        /// <summary>
        /// Deterministically calculates and awards rally donations across all 5 ideological voter groups.
        /// </summary>
        public float AwardRallyDonations(
            string speakerPlayerId,
            CityState city,
            SpeechEvalResult speechEval,
            OverallManager overallManager,
            float sabotagePenaltyRatio = 0f)
        {
            string key = PlayerIdentity.Normalize(speakerPlayerId);
            if (!_players.TryGetValue(key, out PlayerState player) || city == null || speechEval == null)
                return 0f;

            if (player.isNextSpeechBlocked)
                return 0f;

            float speechImpact = Mathf.Clamp01(
                (0.60f * speechEval.speech_effectiveness + 0.40f * speechEval.persuasion_impact) / 100f);
            float sabotageMultiplier = Mathf.Clamp(1.0f - Mathf.Clamp01(sabotagePenaltyRatio), 0.25f, 1.0f);
            speechImpact *= sabotageMultiplier;

            float totalDonation = 0f;
            IdeologyScoresDto overall = overallManager != null ? overallManager.GetOverall(key) : player.overall;

            foreach (IdeologyType ideology in IdeologyCodec.All)
            {
                float groupShare = Mathf.Clamp01(city.ideologyDistribution.Get(ideology) / 100f);
                float groupPopulation = city.population * groupShare;
                float donationTendency = GetDonationTendency(ideology, key, overallManager);

                float speakerIdeologyFactor = Mathf.Clamp01(overall.Get(ideology) / 100f);
                float speechIdeologyFactor = Mathf.Clamp01(speechEval.ideology_scores.Get(ideology) / 100f);
                float overallAlignment = 0.25f + 0.75f * (0.55f * speakerIdeologyFactor + 0.45f * speechIdeologyFactor);

                float groupDonation = groupPopulation
                    * donationTendency
                    * speechImpact
                    * overallAlignment
                    * _baseScaleFactor;

                totalDonation += groupDonation;
            }

            totalDonation = Mathf.Round(totalDonation);
            player.partyBudget += totalDonation;
            player.totalDonationsEarned += totalDonation;
            return totalDonation;
        }

        public bool TrySpendBribe(string playerId, float amount, TurnPhase currentPhase, out string errorReason)
        {
            errorReason = string.Empty;
            if (!CanSpendBudget(currentPhase))
            {
                errorReason = $"Parti bütçesi miting turlarında harcanamaz (faz: {currentPhase}). Yalnızca ELECTION_BRIBE fazında kullanılabilir.";
                return false;
            }

            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                errorReason = "Geçersiz rüşvet tutarı.";
                return false;
            }

            string key = PlayerIdentity.Normalize(playerId);
            if (!_players.TryGetValue(key, out PlayerState player))
            {
                errorReason = "Oyuncu bulunamadı.";
                return false;
            }

            if (amount > player.partyBudget)
            {
                errorReason = $"Yetersiz parti bütçesi: istenen {amount:N0}, mevcut {player.partyBudget:N0}.";
                return false;
            }

            player.partyBudget = Mathf.Max(0f, player.partyBudget - amount);
            return true;
        }
    }
}
