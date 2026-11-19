using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Five ideological voter blocs and Overall dimensions defined in OurGame.md.
    /// </summary>
    public enum IdeologyType
    {
        Nationalist = 0,
        Liberal = 1,
        Democrat = 2,
        Communist = 3,
        Imperialist = 4
    }

    /// <summary>
    /// Six fixed strategic cities in Political Mayhem.
    /// </summary>
    public enum CityId
    {
        Baskent = 0,
        Limankent = 1,
        Demirkent = 2,
        Yesilova = 3,
        Universitekent = 4,
        Dagsehir = 5
    }

    /// <summary>
    /// Canonical unit types aligned with ai/rag/knowledge/units.json.
    /// </summary>
    public enum UnitType
    {
        CampaignVolunteer = 0,
        Propagandist = 1,
        Bodyguard = 2,
        RallyConvoy = 3,
        SecuritySquad = 4,
        SaboteurAgent = 5
    }

    /// <summary>
    /// Authoritative turn phases matching ai/agent/schemas/safe_snapshot.schema.json.
    /// </summary>
    public enum TurnPhase
    {
        CITY_VOTE = 0,
        PREPARATION = 1,
        RALLY_SPEECH = 2,
        AI_EVALUATION = 3,
        REVOLVER_DUEL = 4,
        ELECTION_FINAL_SPEECH = 5,
        ELECTION_BRIBE = 6,
        ELECTION_RESULT = 7,
        BALCONY_SPEECH = 8
    }

    /// <summary>
    /// Current round role matching safe_snapshot.schema.json current_role enum.
    /// </summary>
    public enum PlayerRole
    {
        NONE = 0,
        SPEAKER = 1,
        SABOTEUR = 2,
        ELECTION_CANDIDATE = 3
    }

    /// <summary>
    /// Crowd reaction states matching speech_eval.schema.json crowd_reaction enum.
    /// </summary>
    public enum CrowdReactionType
    {
        MIXED = 0,
        CHEER = 1,
        BOO = 2,
        PROTEST = 3,
        THROW_OBJECTS = 4
    }

    public enum PlayerSlot
    {
        None = 0,
        PlayerA = 1,
        PlayerB = 2
    }

    public static class PlayerIdentity
    {
        public const string PlayerAId = "player_a";
        public const string PlayerBId = "player_b";

        public static string Normalize(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return PlayerAId;
            string trimmed = playerId.Trim().ToLowerInvariant();
            if (trimmed == PlayerBId || trimmed == "playerb" || trimmed == "b" || trimmed == "oyuncu_b")
                return PlayerBId;
            return PlayerAId;
        }

        public static string GetOpponentId(string playerId)
        {
            return Normalize(playerId) == PlayerAId ? PlayerBId : PlayerAId;
        }

        public static PlayerSlot ToSlot(string playerId)
        {
            return Normalize(playerId) == PlayerBId ? PlayerSlot.PlayerB : PlayerSlot.PlayerA;
        }

        public static string FromSlot(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.PlayerA: return PlayerAId;
                case PlayerSlot.PlayerB: return PlayerBId;
                default: return string.Empty;
            }
        }

        public static string GetDisplayName(string playerId)
        {
            return Normalize(playerId) == PlayerBId ? "Oyuncu B" : "Oyuncu A";
        }
    }

    public static class IdeologyCodec
    {
        public static readonly IdeologyType[] All =
        {
            IdeologyType.Nationalist,
            IdeologyType.Liberal,
            IdeologyType.Democrat,
            IdeologyType.Communist,
            IdeologyType.Imperialist
        };

        public static string ToKey(IdeologyType ideology)
        {
            switch (ideology)
            {
                case IdeologyType.Nationalist: return "nationalist";
                case IdeologyType.Liberal: return "liberal";
                case IdeologyType.Democrat: return "democrat";
                case IdeologyType.Communist: return "communist";
                case IdeologyType.Imperialist: return "imperialist";
                default: return "nationalist";
            }
        }

        public static string GetDisplayNameTr(IdeologyType ideology)
        {
            switch (ideology)
            {
                case IdeologyType.Nationalist: return "Milliyetçilik";
                case IdeologyType.Liberal: return "Liberalizm";
                case IdeologyType.Democrat: return "Demokratlık";
                case IdeologyType.Communist: return "Komünizm";
                case IdeologyType.Imperialist: return "Emperyalizm";
                default: return ideology.ToString();
            }
        }

        public static bool TryParse(string raw, out IdeologyType ideology)
        {
            ideology = IdeologyType.Nationalist;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "nationalist":
                case "milliyetci":
                case "milliyetçilik":
                case "milliyetcilik":
                    ideology = IdeologyType.Nationalist;
                    return true;
                case "liberal":
                case "liberalizm":
                case "liberaller":
                    ideology = IdeologyType.Liberal;
                    return true;
                case "democrat":
                case "demokrat":
                case "demokratlik":
                case "demokratlık":
                case "demokratlar":
                    ideology = IdeologyType.Democrat;
                    return true;
                case "communist":
                case "komunist":
                case "komünist":
                case "komunizm":
                case "komünizm":
                    ideology = IdeologyType.Communist;
                    return true;
                case "imperialist":
                case "emperyalist":
                case "emperyalizm":
                    ideology = IdeologyType.Imperialist;
                    return true;
                default:
                    return false;
            }
        }
    }

    public static class CityCatalog
    {
        public static readonly CityId[] All =
        {
            CityId.Baskent,
            CityId.Limankent,
            CityId.Demirkent,
            CityId.Yesilova,
            CityId.Universitekent,
            CityId.Dagsehir
        };

        public static string ToKey(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return "Baskent";
                case CityId.Limankent: return "Limankent";
                case CityId.Demirkent: return "Demirkent";
                case CityId.Yesilova: return "Yesilova";
                case CityId.Universitekent: return "Universitekent";
                case CityId.Dagsehir: return "Dagsehir";
                default: return "Baskent";
            }
        }

        public static string GetDisplayName(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return "Başkent";
                case CityId.Limankent: return "Limankent";
                case CityId.Demirkent: return "Demirkent";
                case CityId.Yesilova: return "Yeşilova";
                case CityId.Universitekent: return "Üniversitekent";
                case CityId.Dagsehir: return "Dağşehir";
                default: return cityId.ToString();
            }
        }

        public static int GetPopulation(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return 120000;
                case CityId.Limankent: return 90000;
                case CityId.Demirkent: return 110000;
                case CityId.Yesilova: return 80000;
                case CityId.Universitekent: return 70000;
                case CityId.Dagsehir: return 60000;
                default: return 80000;
            }
        }

        public static Vector3 GetWorldPosition(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return new Vector3(0f, 0f, 14f);
                case CityId.Limankent: return new Vector3(-48f, 0f, -26f);
                case CityId.Demirkent: return new Vector3(48f, 0f, -6f);
                case CityId.Yesilova: return new Vector3(-44f, 0f, 34f);
                case CityId.Universitekent: return new Vector3(42f, 0f, 38f);
                case CityId.Dagsehir: return new Vector3(0f, 0f, 56f);
                default: return new Vector3(0f, 0f, 14f);
            }
        }

        public static Color GetNeutralColor(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return new Color(0.68f, 0.72f, 0.78f);
                case CityId.Limankent: return new Color(0.64f, 0.69f, 0.75f);
                case CityId.Demirkent: return new Color(0.60f, 0.65f, 0.71f);
                case CityId.Yesilova: return new Color(0.62f, 0.67f, 0.73f);
                case CityId.Universitekent: return new Color(0.66f, 0.70f, 0.76f);
                case CityId.Dagsehir: return new Color(0.56f, 0.61f, 0.68f);
                default: return new Color(0.62f, 0.66f, 0.72f);
            }
        }

        public static string GetPrimaryZoneName(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent: return "RallySquare";
                case CityId.Limankent: return "HarborGate";
                case CityId.Demirkent: return "IndustrialDistrict";
                case CityId.Yesilova: return "MarketSquare";
                case CityId.Universitekent: return "CampusPlaza";
                case CityId.Dagsehir: return "HighlandSquare";
                default: return "RallySquare";
            }
        }

        public static Vector3 GetZoneOffset(string targetZone)
        {
            if (string.IsNullOrWhiteSpace(targetZone)) return new Vector3(0f, 0f, -5.8f);
            switch (targetZone.Trim().ToLowerInvariant())
            {
                case "rallysquare":
                case "marketsquare":
                case "campusplaza":
                case "highlandsquare":
                case "stagefront":
                case "southgate":
                    return new Vector3(0f, 0f, -5.8f);
                case "harborgate":
                case "harborsquare":
                case "westgate":
                    return new Vector3(-5.0f, 0f, -5.4f);
                case "industrialdistrict":
                case "industrialsquare":
                case "eastgate":
                    return new Vector3(5.0f, 0f, -5.4f);
                case "northgate":
                    return new Vector3(0f, 0f, -7.2f);
                default:
                    return new Vector3(0f, 0f, -5.8f);
            }
        }

        public static bool TryParse(string raw, out CityId cityId)
        {
            cityId = CityId.Baskent;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string key = raw.Trim()
                .Replace("ş", "s").Replace("Ş", "s")
                .Replace("ı", "i").Replace("İ", "i")
                .Replace("ü", "u").Replace("Ü", "u")
                .Replace("ğ", "g").Replace("Ğ", "g")
                .Replace("ö", "o").Replace("Ö", "o")
                .Replace("ç", "c").Replace("Ç", "c")
                .ToLowerInvariant();

            switch (key)
            {
                case "baskent":
                    cityId = CityId.Baskent;
                    return true;
                case "limankent":
                    cityId = CityId.Limankent;
                    return true;
                case "demirkent":
                    cityId = CityId.Demirkent;
                    return true;
                case "yesilova":
                    cityId = CityId.Yesilova;
                    return true;
                case "universitekent":
                    cityId = CityId.Universitekent;
                    return true;
                case "dagsehir":
                    cityId = CityId.Dagsehir;
                    return true;
                default:
                    return false;
            }
        }
    }

    [Serializable]
    public struct UnitStatDefinition
    {
        public UnitType unitType;
        public string canonicalKey;
        public string displayNameTr;
        public int deploymentLimitPerRound;
        public float maxHealth;
        public float attackDamage;
        public float armor;
        public float moveSpeed;
        public float attackRange;
        public float attackCooldown;
        public float campaignPower;
    }

    public static class UnitCatalog
    {
        public static readonly UnitType[] DeployableTypes =
        {
            UnitType.CampaignVolunteer,
            UnitType.Propagandist,
            UnitType.Bodyguard,
            UnitType.RallyConvoy
        };

        public static string ToKey(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.CampaignVolunteer: return "campaign_volunteer";
                case UnitType.Propagandist: return "propagandist";
                case UnitType.Bodyguard: return "bodyguard";
                case UnitType.RallyConvoy: return "rally_convoy";
                case UnitType.SecuritySquad: return "security_squad";
                case UnitType.SaboteurAgent: return "saboteur_agent";
                default: return "campaign_volunteer";
            }
        }

        public static string GetDisplayNameTr(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.CampaignVolunteer: return "Kampanya Gönüllüsü";
                case UnitType.Propagandist: return "Propagandacı";
                case UnitType.Bodyguard: return "Koruma";
                case UnitType.RallyConvoy: return "Miting Konvoyu";
                case UnitType.SecuritySquad: return "Güvenlik Ekibi";
                case UnitType.SaboteurAgent: return "Sabotajcı";
                default: return unitType.ToString();
            }
        }

        public static bool IsDeployableByPlayer(UnitType unitType)
        {
            return unitType == UnitType.CampaignVolunteer
                || unitType == UnitType.Propagandist
                || unitType == UnitType.Bodyguard
                || unitType == UnitType.RallyConvoy;
        }

        public static bool TryParse(string raw, out UnitType unitType)
        {
            unitType = UnitType.CampaignVolunteer;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string key = raw.Trim()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace("ş", "s").Replace("Ş", "s")
                .Replace("ı", "i").Replace("İ", "i")
                .Replace("ü", "u").Replace("Ü", "u")
                .Replace("ğ", "g").Replace("Ğ", "g")
                .Replace("ö", "o").Replace("Ö", "o")
                .Replace("ç", "c").Replace("Ç", "c")
                .ToLowerInvariant();

            switch (key)
            {
                case "campaign_volunteer":
                case "campaignvolunteer":
                case "volunteer":
                case "kampanya_gonullusu":
                    unitType = UnitType.CampaignVolunteer;
                    return true;
                case "propagandist":
                case "propagandaci":
                    unitType = UnitType.Propagandist;
                    return true;
                case "bodyguard":
                case "koruma":
                    unitType = UnitType.Bodyguard;
                    return true;
                case "rally_convoy":
                case "rallyconvoy":
                case "miting_konvoyu":
                    unitType = UnitType.RallyConvoy;
                    return true;
                case "security_squad":
                case "securitysquad":
                case "guvenlik_ekibi":
                    unitType = UnitType.SecuritySquad;
                    return true;
                case "saboteur_agent":
                case "saboteur":
                case "sabotajci":
                    unitType = UnitType.SaboteurAgent;
                    return true;
                default:
                    return false;
            }
        }

        public static UnitStatDefinition GetBaseStats(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.CampaignVolunteer:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.CampaignVolunteer,
                        canonicalKey = "campaign_volunteer",
                        displayNameTr = "Kampanya Gönüllüsü",
                        deploymentLimitPerRound = 6,
                        maxHealth = 80f,
                        attackDamage = 8f,
                        armor = 1f,
                        moveSpeed = 5.0f,
                        attackRange = 2.2f,
                        attackCooldown = 1.0f,
                        campaignPower = 6.0f
                    };
                case UnitType.Propagandist:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.Propagandist,
                        canonicalKey = "propagandist",
                        displayNameTr = "Propagandacı",
                        deploymentLimitPerRound = 4,
                        maxHealth = 70f,
                        attackDamage = 14f,
                        armor = 0f,
                        moveSpeed = 4.2f,
                        attackRange = 6.5f,
                        attackCooldown = 1.3f,
                        campaignPower = 9.0f
                    };
                case UnitType.Bodyguard:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.Bodyguard,
                        canonicalKey = "bodyguard",
                        displayNameTr = "Koruma",
                        deploymentLimitPerRound = 3,
                        maxHealth = 150f,
                        attackDamage = 16f,
                        armor = 5f,
                        moveSpeed = 3.6f,
                        attackRange = 2.0f,
                        attackCooldown = 1.15f,
                        campaignPower = 3.5f
                    };
                case UnitType.RallyConvoy:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.RallyConvoy,
                        canonicalKey = "rally_convoy",
                        displayNameTr = "Miting Konvoyu",
                        deploymentLimitPerRound = 2,
                        maxHealth = 180f,
                        attackDamage = 12f,
                        armor = 3f,
                        moveSpeed = 6.2f,
                        attackRange = 3.5f,
                        attackCooldown = 1.4f,
                        campaignPower = 8.0f
                    };
                case UnitType.SecuritySquad:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.SecuritySquad,
                        canonicalKey = "security_squad",
                        displayNameTr = "Güvenlik Ekibi",
                        deploymentLimitPerRound = 1,
                        maxHealth = 120f,
                        attackDamage = 15f,
                        armor = 4f,
                        moveSpeed = 4.5f,
                        attackRange = 2.4f,
                        attackCooldown = 0.9f,
                        campaignPower = 0f
                    };
                case UnitType.SaboteurAgent:
                default:
                    return new UnitStatDefinition
                    {
                        unitType = UnitType.SaboteurAgent,
                        canonicalKey = "saboteur_agent",
                        displayNameTr = "Sabotajcı",
                        deploymentLimitPerRound = 1,
                        maxHealth = 95f,
                        attackDamage = 20f,
                        armor = 2f,
                        moveSpeed = 5.4f,
                        attackRange = 2.2f,
                        attackCooldown = 1.0f,
                        campaignPower = 0f
                    };
            }
        }
    }

    public static class CrowdReactionCodec
    {
        public static CrowdReactionType Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CrowdReactionType.MIXED;
            switch (raw.Trim().ToUpperInvariant())
            {
                case "CHEER": return CrowdReactionType.CHEER;
                case "BOO": return CrowdReactionType.BOO;
                case "PROTEST": return CrowdReactionType.PROTEST;
                case "THROW_OBJECTS": return CrowdReactionType.THROW_OBJECTS;
                default: return CrowdReactionType.MIXED;
            }
        }

        public static string ToKey(CrowdReactionType reaction)
        {
            return reaction.ToString();
        }
    }

    /// <summary>
    /// Explicit JsonUtility-safe 5-ideology score vector (no Dictionary fields).
    /// </summary>
    [Serializable]
    public class IdeologyScoresDto
    {
        public float nationalist;
        public float liberal;
        public float democrat;
        public float communist;
        public float imperialist;

        public IdeologyScoresDto() { }

        public IdeologyScoresDto(float nat, float lib, float dem, float com, float imp)
        {
            nationalist = nat;
            liberal = lib;
            democrat = dem;
            communist = com;
            imperialist = imp;
        }

        public float Get(IdeologyType type)
        {
            switch (type)
            {
                case IdeologyType.Nationalist: return nationalist;
                case IdeologyType.Liberal: return liberal;
                case IdeologyType.Democrat: return democrat;
                case IdeologyType.Communist: return communist;
                case IdeologyType.Imperialist: return imperialist;
                default: return 0f;
            }
        }

        public void Set(IdeologyType type, float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            switch (type)
            {
                case IdeologyType.Nationalist: nationalist = clamped; break;
                case IdeologyType.Liberal: liberal = clamped; break;
                case IdeologyType.Democrat: democrat = clamped; break;
                case IdeologyType.Communist: communist = clamped; break;
                case IdeologyType.Imperialist: imperialist = clamped; break;
            }
        }

        public IdeologyScoresDto Clone()
        {
            return new IdeologyScoresDto(nationalist, liberal, democrat, communist, imperialist);
        }

        public void NormalizeTo100()
        {
            float total = Mathf.Max(0.0001f, nationalist + liberal + democrat + communist + imperialist);
            nationalist = Mathf.Round((nationalist / total) * 1000f) / 10f;
            liberal = Mathf.Round((liberal / total) * 1000f) / 10f;
            democrat = Mathf.Round((democrat / total) * 1000f) / 10f;
            communist = Mathf.Round((communist / total) * 1000f) / 10f;
            imperialist = Mathf.Max(0f, Mathf.Round((100f - nationalist - liberal - democrat - communist) * 10f) / 10f);
        }

        public IdeologyType GetDominantIdeology()
        {
            IdeologyType best = IdeologyType.Nationalist;
            float bestVal = nationalist;
            foreach (IdeologyType t in IdeologyCodec.All)
            {
                float v = Get(t);
                if (v > bestVal)
                {
                    bestVal = v;
                    best = t;
                }
            }
            return best;
        }

        public string ToShortSummary()
        {
            return $"Milliyetçi:{nationalist:F0}% Lib:{liberal:F0}% Dem:{democrat:F0}% Kom:{communist:F0}% Emp:{imperialist:F0}%";
        }
    }

    /// <summary>
    /// Matches ai/agent/schemas/speech_eval.schema.json.
    /// </summary>
    [Serializable]
    public class SpeechEvalResult
    {
        public float speech_effectiveness;
        public IdeologyScoresDto ideology_scores = new IdeologyScoresDto();
        public float persuasion_impact;
        public float sabotage_effectiveness;
        public string crowd_reaction = "MIXED";
        public float argument_quality;
        public float consistency_score;
        public string feedback_summary = string.Empty;
    }

    /// <summary>
    /// Matches ai/agent/schemas/action_plan.schema.json action item.
    /// </summary>
    [Serializable]
    public class AiActionItem
    {
        public string action_type = string.Empty;
        public int unit_id;
        public string unit_type = string.Empty;
        public string city_name = string.Empty;
        public string target_zone = string.Empty;
        public int count;
        public float bribe_amount;
        public string ideology_focus = string.Empty;
        public string reason = string.Empty;
    }

    /// <summary>
    /// Matches ai/agent/schemas/action_plan.schema.json root object.
    /// </summary>
    [Serializable]
    public class ActionPlan
    {
        public string summary = string.Empty;
        public float confidence;
        public List<AiActionItem> actions = new List<AiActionItem>();
    }

    [Serializable]
    public class CityState
    {
        public CityId cityId;
        public string cityName;
        public string displayName;
        public int population;
        public IdeologyScoresDto ideologyDistribution = new IdeologyScoresDto();
        public bool rallyDone;
        public int rallyRoundNumber;
        public PlayerSlot rallySpeakerSlot = PlayerSlot.None;
        public PlayerSlot winnerSlot = PlayerSlot.None;

        // Server-only authoritative election metrics (never leaked into AiSafeSnapshot!)
        public float playerACampaignActivity;
        public float playerBCampaignActivity;
        public float playerARallySpeechScore;
        public float playerBRallySpeechScore;
        public float playerASabotageImpact;
        public float playerBSabotageImpact;
        public float playerAFinalSpeechScore;
        public float playerBFinalSpeechScore;
        public float playerABribeAllocated;
        public float playerBBribeAllocated;

        // Populated only after ELECTION_RESULT for this city
        public bool electionResolved;
        public float finalShareA;
        public float finalShareB;
        public int finalVotesA;
        public int finalVotesB;

        public float GetCampaignActivity(string playerId)
        {
            return PlayerIdentity.ToSlot(playerId) == PlayerSlot.PlayerB ? playerBCampaignActivity : playerACampaignActivity;
        }

        public void AddCampaignActivity(string playerId, float delta)
        {
            if (PlayerIdentity.ToSlot(playerId) == PlayerSlot.PlayerB)
                playerBCampaignActivity = Mathf.Clamp(playerBCampaignActivity + delta, 0f, 100f);
            else
                playerACampaignActivity = Mathf.Clamp(playerACampaignActivity + delta, 0f, 100f);
        }
    }

    [Serializable]
    public class PlayerState
    {
        public string playerId;
        public string displayName;
        public float partyBudget;
        public float totalDonationsEarned;
        public IdeologyScoresDto overall = new IdeologyScoresDto(20f, 20f, 20f, 20f, 20f);
        public List<string> unlockedBonuses = new List<string>();
        public string selectedSpeechStrategy = "democrat";
        public string cityVoteChoice = string.Empty;
        public bool isNextSpeechBlocked;
        public int speechesDelivered;
        public int citiesWon;
        public int totalPopularVotes;

        // Per-round unit deployment counters (no Dictionary for Unity serialization safety)
        public int deployedVolunteerThisRound;
        public int deployedPropagandistThisRound;
        public int deployedBodyguardThisRound;
        public int deployedConvoyThisRound;

        public void ResetRoundDeployments()
        {
            deployedVolunteerThisRound = 0;
            deployedPropagandistThisRound = 0;
            deployedBodyguardThisRound = 0;
            deployedConvoyThisRound = 0;
        }

        public int GetDeployedThisRound(UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.CampaignVolunteer: return deployedVolunteerThisRound;
                case UnitType.Propagandist: return deployedPropagandistThisRound;
                case UnitType.Bodyguard: return deployedBodyguardThisRound;
                case UnitType.RallyConvoy: return deployedConvoyThisRound;
                default: return 0;
            }
        }

        public void IncrementDeployedThisRound(UnitType unitType, int count)
        {
            switch (unitType)
            {
                case UnitType.CampaignVolunteer: deployedVolunteerThisRound += count; break;
                case UnitType.Propagandist: deployedPropagandistThisRound += count; break;
                case UnitType.Bodyguard: deployedBodyguardThisRound += count; break;
                case UnitType.RallyConvoy: deployedConvoyThisRound += count; break;
            }
        }
    }

    [Serializable]
    public class MatchEventRecord
    {
        public string eventType = string.Empty;
        public string summary = string.Empty;
        public string cityName = string.Empty;
        public float timestampSeconds;
    }

    [Serializable]
    public class VisibleOpponentActivityRecord
    {
        public string activityType = string.Empty;
        public string cityName = string.Empty;
        public string unitType = string.Empty;
        public int count;
        public string zone = string.Empty;
        public string speechExcerpt = string.Empty;
        public float timestampSeconds;
        public string observerPlayerId = string.Empty;
    }

    [Serializable]
    public class ValidationResult
    {
        public bool isValid;
        public string reason = string.Empty;
        public string failedActionType = string.Empty;

        public static ValidationResult Ok(string message = "Valid")
        {
            return new ValidationResult { isValid = true, reason = message };
        }

        public static ValidationResult Fail(string reason, string actionType = "")
        {
            return new ValidationResult { isValid = false, reason = reason, failedActionType = actionType };
        }
    }

    [Serializable]
    public class ElectionCityResult
    {
        public string cityName;
        public string displayName;
        public int population;
        public float shareA;
        public float shareB;
        public int votesA;
        public int votesB;
        public float bribeA;
        public float bribeB;
        public float bribeShiftApplied;
        public string winnerPlayerId;
        public string headline;
    }
}
