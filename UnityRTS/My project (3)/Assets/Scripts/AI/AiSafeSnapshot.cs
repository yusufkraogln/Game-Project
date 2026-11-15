using System;
using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.AI
{
    [Serializable]
    public class VisibleCampaignPresenceDto
    {
        public int own_unit_count;
        public int visible_opponent_unit_count;
        public bool contested;
    }

    [Serializable]
    public class CitySnapshotDto
    {
        public string city_name = string.Empty;
        public int population;
        public IdeologyScoresDto ideology_distribution = new IdeologyScoresDto();
        public bool rally_done;
        public VisibleCampaignPresenceDto visible_campaign_presence = new VisibleCampaignPresenceDto();
        public string winner = "NONE";
    }

    [Serializable]
    public class OwnUnitSnapshotDto
    {
        public string unit_type = string.Empty;
        public int count;
        public string city_name = string.Empty;
        public string zone = string.Empty;
        public float avg_health;
    }

    [Serializable]
    public class VisibleOpponentActivityDto
    {
        public string activity_type = string.Empty;
        public string city_name = string.Empty;
        public string unit_type = string.Empty;
        public int count;
        public string zone = string.Empty;
        public string speech_excerpt = string.Empty;
        public int seconds_ago;
    }

    [Serializable]
    public class RecentEventSnapshotDto
    {
        public string event_type = string.Empty;
        public string summary = string.Empty;
        public string city_name = string.Empty;
        public int seconds_ago;
    }

    /// <summary>
    /// Exact JsonUtility-compatible DTO matching ai/agent/schemas/safe_snapshot.schema.json.
    /// Zero Dictionary fields. Never contains opponent_budget, opponent_overall, or hidden_support_rates.
    /// </summary>
    [Serializable]
    public class AiSafeSnapshotDto
    {
        public string match_id = string.Empty;
        public string player_id = string.Empty;
        public int round_number;
        public string phase = "CITY_VOTE";
        public string current_role = "NONE";
        public float party_budget;
        public IdeologyScoresDto player_overall = new IdeologyScoresDto();
        public List<CitySnapshotDto> cities = new List<CitySnapshotDto>();
        public List<OwnUnitSnapshotDto> own_units = new List<OwnUnitSnapshotDto>();
        public List<VisibleOpponentActivityDto> visible_opponent_activity = new List<VisibleOpponentActivityDto>();
        public List<RecentEventSnapshotDto> recent_events = new List<RecentEventSnapshotDto>();
        public List<string> unlocked_bonuses = new List<string>();
    }

    /// <summary>
    /// Enforces Fog-of-War isolation in C# before any data crosses to the AI Gateway or MCP tools.
    /// </summary>
    public static class VisibilityService
    {
        public static AiSafeSnapshotDto FilterFor(string localPlayerId, PoliticalMayhemGameManager gameManager)
        {
            string viewerId = PlayerIdentity.Normalize(localPlayerId);
            AiSafeSnapshotDto snapshot = new AiSafeSnapshotDto
            {
                match_id = gameManager != null && !string.IsNullOrEmpty(gameManager.MatchId) ? gameManager.MatchId : "pm-match-01",
                player_id = viewerId,
                round_number = gameManager != null ? Mathf.Clamp(gameManager.RoundNumber, 0, 12) : 0,
                phase = gameManager != null ? gameManager.CurrentPhase.ToString() : TurnPhase.CITY_VOTE.ToString(),
                current_role = gameManager != null ? gameManager.GetPlayerRole(viewerId).ToString() : PlayerRole.NONE.ToString()
            };

            if (gameManager == null)
                return snapshot;

            // Only authenticated local player's own budget, overall, and unlocked tech bonuses are read.
            PlayerState self = gameManager.GetPlayerState(viewerId);
            snapshot.party_budget = Mathf.Max(0f, self.partyBudget);
            snapshot.player_overall = self.overall != null ? self.overall.Clone() : new IdeologyScoresDto();
            snapshot.unlocked_bonuses = self.unlockedBonuses != null
                ? new List<string>(self.unlockedBonuses)
                : new List<string>();

            // Aggregate own units & fog-of-war filtered opponent units
            IReadOnlyList<CampaignUnit> allUnits = CampaignUnit.ActiveUnits;
            List<OwnUnitGroupAccumulator> ownGroups = new List<OwnUnitGroupAccumulator>();
            List<VisibleOpponentGroupAccumulator> visibleEnemyGroups = new List<VisibleOpponentGroupAccumulator>();

            for (int i = 0; i < allUnits.Count; i++)
            {
                CampaignUnit unit = allUnits[i];
                if (unit == null || !unit.IsAlive) continue;

                if (unit.OwnerPlayerId == viewerId)
                {
                    AddToOwnGroup(ownGroups, unit);
                }
                else if (gameManager.IsOpponentUnitVisibleTo(viewerId, unit))
                {
                    AddToVisibleEnemyGroup(visibleEnemyGroups, unit);
                }
            }

            for (int i = 0; i < ownGroups.Count; i++)
            {
                OwnUnitGroupAccumulator g = ownGroups[i];
                snapshot.own_units.Add(new OwnUnitSnapshotDto
                {
                    unit_type = g.unitType,
                    count = g.count,
                    city_name = g.cityName,
                    zone = g.zone,
                    avg_health = g.count > 0 ? Mathf.Round((g.totalHealth / g.count) * 10f) / 10f : 0f
                });
            }

            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < visibleEnemyGroups.Count; i++)
            {
                VisibleOpponentGroupAccumulator g = visibleEnemyGroups[i];
                snapshot.visible_opponent_activity.Add(new VisibleOpponentActivityDto
                {
                    activity_type = "UNIT_SPOTTED",
                    city_name = g.cityName,
                    unit_type = g.unitType,
                    count = g.count,
                    zone = g.zone,
                    speech_excerpt = string.Empty,
                    seconds_ago = 0
                });
            }

            // Include public speech excerpts observed by viewer
            IReadOnlyList<VisibleOpponentActivityRecord> activities = gameManager.OpponentActivities;
            for (int i = activities.Count - 1; i >= 0 && snapshot.visible_opponent_activity.Count < 16; i--)
            {
                VisibleOpponentActivityRecord act = activities[i];
                if (act.observerPlayerId != viewerId) continue;

                snapshot.visible_opponent_activity.Add(new VisibleOpponentActivityDto
                {
                    activity_type = Truncate(act.activityType, 64),
                    city_name = Truncate(act.cityName, 80),
                    unit_type = Truncate(act.unitType, 80),
                    count = Mathf.Max(0, act.count),
                    zone = Truncate(act.zone, 80),
                    speech_excerpt = Truncate(act.speechExcerpt, 240),
                    seconds_ago = Mathf.Max(0, Mathf.RoundToInt(now - act.timestampSeconds))
                });
            }

            // Populate public city metadata + fog-of-war filtered unit presence
            CityManager cityMgr = gameManager.CityManager;
            if (cityMgr != null)
            {
                IReadOnlyList<CityState> cities = cityMgr.Cities;
                for (int i = 0; i < cities.Count && snapshot.cities.Count < 6; i++)
                {
                    CityState city = cities[i];
                    int ownCount = CountUnitsInCity(ownGroups, city.cityName);
                    int visibleEnemyCount = CountVisibleEnemiesInCity(visibleEnemyGroups, city.cityName);

                    snapshot.cities.Add(new CitySnapshotDto
                    {
                        city_name = city.cityName,
                        population = Mathf.Max(1, city.population),
                        ideology_distribution = city.ideologyDistribution != null
                            ? city.ideologyDistribution.Clone()
                            : new IdeologyScoresDto(20f, 20f, 20f, 20f, 20f),
                        rally_done = city.rallyDone,
                        visible_campaign_presence = new VisibleCampaignPresenceDto
                        {
                            own_unit_count = ownCount,
                            visible_opponent_unit_count = visibleEnemyCount,
                            contested = ownCount > 0 && visibleEnemyCount > 0
                        },
                        winner = cityMgr.GetWinnerPerspectiveString(city, viewerId)
                    });
                }
            }

            // Public recent events
            IReadOnlyList<MatchEventRecord> events = gameManager.PublicEvents;
            for (int i = events.Count - 1; i >= 0 && snapshot.recent_events.Count < 12; i--)
            {
                MatchEventRecord ev = events[i];
                snapshot.recent_events.Add(new RecentEventSnapshotDto
                {
                    event_type = Truncate(ev.eventType, 64),
                    summary = Truncate(ev.summary, 240),
                    city_name = Truncate(ev.cityName, 80),
                    seconds_ago = Mathf.Max(0, Mathf.RoundToInt(now - ev.timestampSeconds))
                });
            }

            return snapshot;
        }

        public static string SerializeToJson(AiSafeSnapshotDto snapshot, bool prettyPrint = true)
        {
            return JsonUtility.ToJson(snapshot ?? new AiSafeSnapshotDto(), prettyPrint);
        }

        private class OwnUnitGroupAccumulator
        {
            public string unitType;
            public string cityName;
            public string zone;
            public int count;
            public float totalHealth;
        }

        private class VisibleOpponentGroupAccumulator
        {
            public string unitType;
            public string cityName;
            public string zone;
            public int count;
        }

        private static void AddToOwnGroup(List<OwnUnitGroupAccumulator> list, CampaignUnit unit)
        {
            string typeKey = unit.CanonicalTypeKey;
            string cityKey = CityCatalog.ToKey(unit.CurrentCity);
            string zoneKey = string.IsNullOrWhiteSpace(unit.CurrentZone)
                ? CityCatalog.GetPrimaryZoneName(unit.CurrentCity)
                : Truncate(unit.CurrentZone, 80);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].unitType == typeKey && list[i].cityName == cityKey && list[i].zone == zoneKey)
                {
                    list[i].count++;
                    list[i].totalHealth += Mathf.Max(0f, unit.CurrentHealth);
                    return;
                }
            }

            list.Add(new OwnUnitGroupAccumulator
            {
                unitType = typeKey,
                cityName = cityKey,
                zone = zoneKey,
                count = 1,
                totalHealth = Mathf.Max(0f, unit.CurrentHealth)
            });
        }

        private static void AddToVisibleEnemyGroup(List<VisibleOpponentGroupAccumulator> list, CampaignUnit unit)
        {
            string typeKey = unit.CanonicalTypeKey;
            string cityKey = CityCatalog.ToKey(unit.CurrentCity);
            string zoneKey = string.IsNullOrWhiteSpace(unit.CurrentZone)
                ? CityCatalog.GetPrimaryZoneName(unit.CurrentCity)
                : Truncate(unit.CurrentZone, 80);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].unitType == typeKey && list[i].cityName == cityKey && list[i].zone == zoneKey)
                {
                    list[i].count++;
                    return;
                }
            }

            list.Add(new VisibleOpponentGroupAccumulator
            {
                unitType = typeKey,
                cityName = cityKey,
                zone = zoneKey,
                count = 1
            });
        }

        private static int CountUnitsInCity(List<OwnUnitGroupAccumulator> groups, string cityName)
        {
            int total = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].cityName == cityName) total += groups[i].count;
            }
            return total;
        }

        private static int CountVisibleEnemiesInCity(List<VisibleOpponentGroupAccumulator> groups, string cityName)
        {
            int total = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].cityName == cityName) total += groups[i].count;
            }
            return total;
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen);
        }
    }
}
