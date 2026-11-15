using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.Combat
{
    /// <summary>
    /// Deterministic unit-counter matrix and combat resolution helpers aligned with
    /// ai/rag/knowledge/units.json and OurGame.md.
    /// </summary>
    public static class CombatResolver
    {
        public static float GetCounterDamageMultiplier(UnitType attacker, UnitType defender)
        {
            // Bodyguard (Koruma) hard-counters Campaign Volunteer and Saboteur
            if (attacker == UnitType.Bodyguard &&
                (defender == UnitType.CampaignVolunteer || defender == UnitType.SaboteurAgent))
            {
                return 1.60f;
            }

            // Propagandist (Propagandacı) disrupts slow Bodyguard morale at range
            if (attacker == UnitType.Propagandist && defender == UnitType.Bodyguard)
            {
                return 1.50f;
            }

            // Campaign Volunteer (Kampanya Gönüllüsü) overwhelms fragile Propagandist in close quarters
            if (attacker == UnitType.CampaignVolunteer && defender == UnitType.Propagandist)
            {
                return 1.50f;
            }

            // Security Squad (Güvenlik Ekibi) suppresses Saboteurs
            if (attacker == UnitType.SecuritySquad && defender == UnitType.SaboteurAgent)
            {
                return 1.75f;
            }

            // Rally Convoy kites heavy Bodyguards
            if (attacker == UnitType.RallyConvoy && defender == UnitType.Bodyguard)
            {
                return 1.35f;
            }

            // Saboteur ambush bonus against unescorted Propagandists
            if (attacker == UnitType.SaboteurAgent && defender == UnitType.Propagandist)
            {
                return 1.45f;
            }

            return 1.0f;
        }

        public static float ComputeEffectiveDamage(CampaignUnit attacker, CampaignUnit defender)
        {
            if (attacker == null || defender == null) return 0f;
            float multiplier = GetCounterDamageMultiplier(attacker.UnitType, defender.UnitType);
            float raw = attacker.AttackDamage * multiplier;
            float mitigated = raw - (defender.Armor * 0.65f);
            return Mathf.Max(2.0f, Mathf.Round(mitigated * 10f) / 10f);
        }

        public static CampaignUnit FindBestEnemyInRange(
            CampaignUnit attacker,
            IReadOnlyList<CampaignUnit> allUnits,
            float searchRadius)
        {
            if (attacker == null || allUnits == null) return null;

            CampaignUnit best = null;
            float bestScore = float.MinValue;
            Vector3 myPos = attacker.transform.position;

            for (int i = 0; i < allUnits.Count; i++)
            {
                CampaignUnit candidate = allUnits[i];
                if (candidate == null || !candidate.IsAlive) continue;
                if (candidate.OwnerPlayerId == attacker.OwnerPlayerId) continue;

                float dist = Vector3.Distance(myPos, candidate.transform.position);
                if (dist > searchRadius) continue;

                float counterBonus = GetCounterDamageMultiplier(attacker.UnitType, candidate.UnitType);
                float score = counterBonus * 10f - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        public static string DescribeCounterAdvantage(UnitType attacker, UnitType defender)
        {
            float mult = GetCounterDamageMultiplier(attacker, defender);
            if (mult > 1.05f)
            {
                return $"{UnitCatalog.GetDisplayNameTr(attacker)} -> {UnitCatalog.GetDisplayNameTr(defender)} ({mult:0.00}x counter)";
            }
            return "Standard engagement (1.00x)";
        }
    }
}
