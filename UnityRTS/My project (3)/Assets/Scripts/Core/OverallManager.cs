using System.Collections.Generic;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Tracks 5 independent ideological Overall ratings (0..100) per player
    /// using the exact OurGame.md update rule: New = 0.80 * Previous + 0.20 * Speech.
    /// Computes unlocked passive ideological threshold bonuses matching ai/rag/knowledge/technologies.json.
    /// </summary>
    public class OverallManager : MonoBehaviour
    {
        public const float PreviousWeight = 0.80f;
        public const float SpeechWeight = 0.20f;
        public const float DefaultUnlockThreshold = 40f;

        public const string TechNationalist = "tech_nationalist_fervor_v1";
        public const string TechLiberal = "tech_liberal_media_network_v1";
        public const string TechDemocrat = "tech_democrat_grassroots_v1";
        public const string TechCommunist = "tech_communist_union_mobilization_v1";
        public const string TechImperialist = "tech_imperialist_patronage_v1";

        [Header("Ideological Progression Threshold")]
        [SerializeField] private float _unlockThreshold = DefaultUnlockThreshold;

        private readonly Dictionary<string, PlayerState> _players = new Dictionary<string, PlayerState>();

        public float UnlockThreshold => _unlockThreshold;

        public void BindPlayers(PlayerState playerA, PlayerState playerB)
        {
            _players.Clear();
            _players[PlayerIdentity.PlayerAId] = playerA;
            _players[PlayerIdentity.PlayerBId] = playerB;
            RecomputeUnlockedBonuses(playerA);
            RecomputeUnlockedBonuses(playerB);
        }

        public IdeologyScoresDto GetOverall(string playerId)
        {
            string key = PlayerIdentity.Normalize(playerId);
            return _players.TryGetValue(key, out PlayerState p) ? p.overall : new IdeologyScoresDto();
        }

        public List<string> GetUnlockedBonuses(string playerId)
        {
            string key = PlayerIdentity.Normalize(playerId);
            return _players.TryGetValue(key, out PlayerState p)
                ? new List<string>(p.unlockedBonuses)
                : new List<string>();
        }

        public bool HasBonus(string playerId, string techId)
        {
            string key = PlayerIdentity.Normalize(playerId);
            return _players.TryGetValue(key, out PlayerState p) && p.unlockedBonuses.Contains(techId);
        }

        /// <summary>
        /// Updates all 5 Overall dimensions deterministically:
        /// NewOverall = 0.80 * PreviousOverall + 0.20 * SpeechIdeologyScore.
        /// </summary>
        public List<string> ApplySpeechEvaluation(string playerId, IdeologyScoresDto speechIdeologyScores)
        {
            string key = PlayerIdentity.Normalize(playerId);
            if (!_players.TryGetValue(key, out PlayerState player) || speechIdeologyScores == null)
                return new List<string>();

            foreach (IdeologyType ideology in IdeologyCodec.All)
            {
                float previous = player.overall.Get(ideology);
                float speechScore = Mathf.Clamp(speechIdeologyScores.Get(ideology), 0f, 100f);
                float updated = Mathf.Clamp(PreviousWeight * previous + SpeechWeight * speechScore, 0f, 100f);
                player.overall.Set(ideology, Mathf.Round(updated * 100f) / 100f);
            }

            player.speechesDelivered++;
            return RecomputeUnlockedBonuses(player);
        }

        /// <summary>
        /// Applies a political reputation penalty (e.g., shooting an innocent suspect in Revolver Duel).
        /// </summary>
        public void ApplyReputationPenalty(string playerId, float deltaPoints)
        {
            string key = PlayerIdentity.Normalize(playerId);
            if (!_players.TryGetValue(key, out PlayerState player)) return;

            foreach (IdeologyType ideology in IdeologyCodec.All)
            {
                float updated = Mathf.Clamp(player.overall.Get(ideology) - Mathf.Abs(deltaPoints), 0f, 100f);
                player.overall.Set(ideology, Mathf.Round(updated * 100f) / 100f);
            }

            RecomputeUnlockedBonuses(player);
        }

        public List<string> RecomputeUnlockedBonuses(PlayerState player)
        {
            List<string> newlyUnlocked = new List<string>();
            if (player == null) return newlyUnlocked;

            CheckUnlock(player, IdeologyType.Nationalist, TechNationalist, newlyUnlocked);
            CheckUnlock(player, IdeologyType.Liberal, TechLiberal, newlyUnlocked);
            CheckUnlock(player, IdeologyType.Democrat, TechDemocrat, newlyUnlocked);
            CheckUnlock(player, IdeologyType.Communist, TechCommunist, newlyUnlocked);
            CheckUnlock(player, IdeologyType.Imperialist, TechImperialist, newlyUnlocked);

            return newlyUnlocked;
        }

        private void CheckUnlock(PlayerState player, IdeologyType ideology, string techId, List<string> newlyUnlocked)
        {
            bool meetsThreshold = player.overall.Get(ideology) >= _unlockThreshold;
            if (meetsThreshold && !player.unlockedBonuses.Contains(techId))
            {
                player.unlockedBonuses.Add(techId);
                newlyUnlocked.Add(techId);
            }
        }

        /// <summary>
        /// Computes 0..100 alignment score between a player's Overall vector and a city's demographic distribution.
        /// </summary>
        public float ComputeOverallAlignment(string playerId, IdeologyScoresDto cityDistribution)
        {
            if (cityDistribution == null) return 25f;
            IdeologyScoresDto overall = GetOverall(playerId);

            float weightedAlignment = 0f;
            float totalWeight = 0f;
            foreach (IdeologyType ideology in IdeologyCodec.All)
            {
                float popPct = Mathf.Max(0f, cityDistribution.Get(ideology)) / 100f;
                weightedAlignment += popPct * overall.Get(ideology);
                totalWeight += popPct;
            }

            if (totalWeight <= 0.0001f) return 20f;
            return Mathf.Clamp(weightedAlignment / totalWeight, 0f, 100f);
        }

        // Gameplay modifiers unlocked by ideological progression thresholds (technologies.json)
        public int GetVolunteerRoundCapBonus(string playerId) => HasBonus(playerId, TechDemocrat) ? 1 : 0;
        public float GetVolunteerHealthMultiplier(string playerId) => HasBonus(playerId, TechCommunist) ? 1.15f : 1.0f;
        public float GetPropagandistRangeMultiplier(string playerId) => HasBonus(playerId, TechLiberal) ? 1.15f : 1.0f;
        public float GetBodyguardArmorMultiplier(string playerId) => HasBonus(playerId, TechNationalist) ? 1.10f : 1.0f;
        public float GetBribeEfficiencyMultiplier(string playerId) => HasBonus(playerId, TechImperialist) ? 1.20f : 1.0f;
        public float GetCheerSabotageResistance(string playerId) => HasBonus(playerId, TechNationalist) ? 0.15f : 0f;
        public float GetCrowdBooRecoveryBonus(string playerId) => HasBonus(playerId, TechDemocrat) ? 0.12f : 0f;
    }
}
