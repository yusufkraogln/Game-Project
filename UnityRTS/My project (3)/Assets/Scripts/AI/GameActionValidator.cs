using System;
using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.AI
{
    [Serializable]
    public class ValidationLogEntry
    {
        public string timestamp;
        public string playerId;
        public string actionType;
        public bool accepted;
        public string message;
    }

    /// <summary>
    /// Deterministic C# rules validator running on BOTH client preview and Host authority.
    /// Ensures AI ActionPlans and manual requests strictly obey schema bounds, round limits,
    /// unit ownership, city no-repeat rally rules, and Election Night bribery constraints.
    /// </summary>
    public static class GameActionValidator
    {
        public static readonly HashSet<string> AllowedActionTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "DEPLOY_UNIT",
            "MOVE_GROUP",
            "MOVE_UNIT",
            "VOTE_RALLY_CITY",
            "CHOOSE_SPEECH_STRATEGY",
            "ALLOCATE_ELECTION_BRIBE"
        };

        private static readonly List<ValidationLogEntry> _auditLog = new List<ValidationLogEntry>();
        public static IReadOnlyList<ValidationLogEntry> AuditLog => _auditLog;

        public static void ClearAuditLog()
        {
            _auditLog.Clear();
        }

        private static void RecordLog(string playerId, string actionType, bool accepted, string message)
        {
            _auditLog.Add(new ValidationLogEntry
            {
                timestamp = DateTime.Now.ToString("HH:mm:ss"),
                playerId = playerId,
                actionType = string.IsNullOrEmpty(actionType) ? "PLAN" : actionType,
                accepted = accepted,
                message = message
            });
            if (_auditLog.Count > 50)
                _auditLog.RemoveAt(0);

            if (!accepted)
            {
                Debug.LogWarning($"[GameActionValidator] REJECTED ({playerId} / {actionType}): {message}");
            }
        }

        public static ValidationResult ValidatePlan(
            ActionPlan plan,
            string playerId,
            PoliticalMayhemGameManager gameManager,
            bool logResult = true)
        {
            string normalizedPlayer = PlayerIdentity.Normalize(playerId);

            if (plan == null)
            {
                var res = ValidationResult.Fail("ActionPlan is null.", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            if (string.IsNullOrWhiteSpace(plan.summary) || plan.summary.Length > 400)
            {
                var res = ValidationResult.Fail("ActionPlan summary must be 1..400 characters.", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            if (float.IsNaN(plan.confidence) || plan.confidence < 0f || plan.confidence > 1f)
            {
                var res = ValidationResult.Fail("ActionPlan confidence must be within [0, 1].", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            if (plan.actions == null)
            {
                var res = ValidationResult.Fail("ActionPlan actions list is null.", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            if (plan.actions.Count > 8)
            {
                var res = ValidationResult.Fail($"ActionPlan exceeds max 8 actions ({plan.actions.Count}).", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            if (gameManager == null)
            {
                var res = ValidationResult.Fail("GameManager reference unavailable for authoritative validation.", "PLAN");
                if (logResult) RecordLog(normalizedPlayer, "PLAN", false, res.reason);
                return res;
            }

            // Track cumulative budget & unit deployment across multiple actions in the same plan
            float remainingBudget = gameManager.GetPlayerState(normalizedPlayer).partyBudget;
            int extraDeployedVolunteer = 0;
            int extraDeployedPropagandist = 0;
            int extraDeployedBodyguard = 0;
            int extraDeployedConvoy = 0;
            int extraTotalUnits = 0;

            for (int i = 0; i < plan.actions.Count; i++)
            {
                AiActionItem action = plan.actions[i];
                ValidationResult itemResult = ValidateActionInternal(
                    action,
                    normalizedPlayer,
                    gameManager,
                    ref remainingBudget,
                    ref extraDeployedVolunteer,
                    ref extraDeployedPropagandist,
                    ref extraDeployedBodyguard,
                    ref extraDeployedConvoy,
                    ref extraTotalUnits);

                if (!itemResult.isValid)
                {
                    if (logResult)
                    {
                        RecordLog(
                            normalizedPlayer,
                            action != null ? action.action_type : "NULL",
                            false,
                            $"Action[{i}] failed: {itemResult.reason}");
                    }
                    return itemResult;
                }
            }

            if (logResult)
            {
                RecordLog(normalizedPlayer, "PLAN", true, $"Plan validated cleanly ({plan.actions.Count} action(s)).");
            }
            return ValidationResult.Ok($"Validated {plan.actions.Count} action(s).");
        }

        public static ValidationResult ValidateAction(
            AiActionItem action,
            string playerId,
            PoliticalMayhemGameManager gameManager,
            bool logResult = true)
        {
            string normalizedPlayer = PlayerIdentity.Normalize(playerId);
            if (gameManager == null)
            {
                var fail = ValidationResult.Fail("GameManager missing.", action != null ? action.action_type : "UNKNOWN");
                if (logResult) RecordLog(normalizedPlayer, fail.failedActionType, false, fail.reason);
                return fail;
            }

            float budget = gameManager.GetPlayerState(normalizedPlayer).partyBudget;
            int v = 0, p = 0, b = 0, c = 0, total = 0;
            ValidationResult res = ValidateActionInternal(action, normalizedPlayer, gameManager, ref budget, ref v, ref p, ref b, ref c, ref total);
            if (logResult)
            {
                RecordLog(normalizedPlayer, action != null ? action.action_type : "NULL", res.isValid, res.reason);
            }
            return res;
        }

        private static ValidationResult ValidateActionInternal(
            AiActionItem action,
            string playerId,
            PoliticalMayhemGameManager gameManager,
            ref float remainingBudget,
            ref int extraVolunteer,
            ref int extraPropagandist,
            ref int extraBodyguard,
            ref int extraConvoy,
            ref int extraTotalUnits)
        {
            if (action == null)
                return ValidationResult.Fail("Action item is null.", "NULL");

            if (string.IsNullOrWhiteSpace(action.action_type) || !AllowedActionTypes.Contains(action.action_type))
                return ValidationResult.Fail($"Unknown or unauthorized action_type '{action.action_type}'.", action.action_type);

            if (string.IsNullOrWhiteSpace(action.reason) || action.reason.Length > 250)
                return ValidationResult.Fail("Action reason must be 1..250 characters.", action.action_type);

            switch (action.action_type)
            {
                case "DEPLOY_UNIT":
                    return ValidateDeployUnit(action, playerId, gameManager, ref extraVolunteer, ref extraPropagandist, ref extraBodyguard, ref extraConvoy, ref extraTotalUnits);
                case "MOVE_GROUP":
                case "MOVE_UNIT":
                    return ValidateMoveGroup(action, playerId);
                case "VOTE_RALLY_CITY":
                    return ValidateVoteRallyCity(action, gameManager);
                case "CHOOSE_SPEECH_STRATEGY":
                    return ValidateSpeechStrategy(action);
                case "ALLOCATE_ELECTION_BRIBE":
                    return ValidateElectionBribe(action, playerId, gameManager, ref remainingBudget);
                default:
                    return ValidationResult.Fail($"Unsupported action_type '{action.action_type}'.", action.action_type);
            }
        }

        private static ValidationResult ValidateDeployUnit(
            AiActionItem action,
            string playerId,
            PoliticalMayhemGameManager gameManager,
            ref int extraVolunteer,
            ref int extraPropagandist,
            ref int extraBodyguard,
            ref int extraConvoy,
            ref int extraTotalUnits)
        {
            if (!gameManager.IsRallyRoundPhase())
            {
                return ValidationResult.Fail(
                    $"Units can only be deployed during Rally Rounds (current phase: {gameManager.CurrentPhase}).",
                    action.action_type);
            }

            if (!UnitCatalog.TryParse(action.unit_type, out UnitType unitType) || !UnitCatalog.IsDeployableByPlayer(unitType))
            {
                return ValidationResult.Fail(
                    $"Invalid or non-deployable unit_type '{action.unit_type}'.",
                    action.action_type);
            }

            if (!CityCatalog.TryParse(action.city_name, out _))
            {
                return ValidationResult.Fail(
                    $"Unknown canonical city_name '{action.city_name}'.",
                    action.action_type);
            }

            if (action.count < 1 || action.count > 20)
            {
                return ValidationResult.Fail(
                    $"Invalid unit count {action.count}; must be integer in [1, 20].",
                    action.action_type);
            }

            int currentActive = UnitController.CountActiveUnitsForPlayer(playerId) + extraTotalUnits;
            if (currentActive + action.count > UnitController.MaxTotalActiveUnitsPerPlayer)
            {
                return ValidationResult.Fail(
                    $"Total active unit cap ({UnitController.MaxTotalActiveUnitsPerPlayer}) exceeded: active {currentActive}, requested {action.count}.",
                    action.action_type);
            }

            int alreadyAdded = 0;
            switch (unitType)
            {
                case UnitType.CampaignVolunteer: alreadyAdded = extraVolunteer; break;
                case UnitType.Propagandist: alreadyAdded = extraPropagandist; break;
                case UnitType.Bodyguard: alreadyAdded = extraBodyguard; break;
                case UnitType.RallyConvoy: alreadyAdded = extraConvoy; break;
            }

            int remainingCap = gameManager.GetRemainingDeploymentCapacity(playerId, unitType) - alreadyAdded;
            if (action.count > remainingCap)
            {
                return ValidationResult.Fail(
                    $"Round deployment capacity exceeded for {UnitCatalog.ToKey(unitType)}: requested {action.count}, remaining {Mathf.Max(0, remainingCap)}.",
                    action.action_type);
            }

            switch (unitType)
            {
                case UnitType.CampaignVolunteer: extraVolunteer += action.count; break;
                case UnitType.Propagandist: extraPropagandist += action.count; break;
                case UnitType.Bodyguard: extraBodyguard += action.count; break;
                case UnitType.RallyConvoy: extraConvoy += action.count; break;
            }
            extraTotalUnits += action.count;

            return ValidationResult.Ok("DEPLOY_UNIT valid.");
        }

        private static ValidationResult ValidateMoveGroup(AiActionItem action, string playerId)
        {
            if (!CityCatalog.TryParse(action.city_name, out _))
            {
                return ValidationResult.Fail($"Unknown target city_name '{action.city_name}'.", action.action_type);
            }

            bool isSingleUnitMove = action.action_type == "MOVE_UNIT";
            int requestedCount = isSingleUnitMove && action.count <= 0 ? 1 : action.count;
            if (requestedCount < 1 || requestedCount > 20)
            {
                return ValidationResult.Fail($"Invalid {action.action_type} count {action.count}; must be integer in [1, 20].", action.action_type);
            }

            IReadOnlyList<CampaignUnit> units = CampaignUnit.ActiveUnits;
            if (action.unit_id > 0)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    CampaignUnit u = units[i];
                    if (u != null && u.IsAlive && u.OwnerPlayerId == playerId && u.UnitId == action.unit_id)
                        return ValidationResult.Ok($"{action.action_type} valid.");
                }
            }

            bool filterByType = !string.IsNullOrWhiteSpace(action.unit_type);
            UnitType parsedType = UnitType.CampaignVolunteer;
            if (filterByType && !UnitCatalog.TryParse(action.unit_type, out parsedType))
            {
                return ValidationResult.Fail($"Unknown unit_type '{action.unit_type}' for {action.action_type}.", action.action_type);
            }

            int ownedMatching = 0;
            for (int i = 0; i < units.Count; i++)
            {
                CampaignUnit u = units[i];
                if (u == null || !u.IsAlive || u.OwnerPlayerId != playerId) continue;
                if (!filterByType || u.UnitType == parsedType)
                    ownedMatching++;
            }

            if (ownedMatching < requestedCount)
            {
                return ValidationResult.Fail(
                    $"Insufficient owned units for {action.action_type}: requested {requestedCount}, owned {ownedMatching}.",
                    action.action_type);
            }

            return ValidationResult.Ok($"{action.action_type} valid.");
        }

        private static ValidationResult ValidateVoteRallyCity(AiActionItem action, PoliticalMayhemGameManager gameManager)
        {
            if (gameManager.CurrentPhase != TurnPhase.CITY_VOTE)
            {
                return ValidationResult.Fail(
                    $"VOTE_RALLY_CITY only allowed in CITY_VOTE phase (current: {gameManager.CurrentPhase}).",
                    action.action_type);
            }

            if (!CityCatalog.TryParse(action.city_name, out CityId cityId))
            {
                return ValidationResult.Fail($"Unknown city_name '{action.city_name}'.", action.action_type);
            }

            CityState city = gameManager.CityManager != null ? gameManager.CityManager.GetCity(cityId) : null;
            if (city == null)
            {
                return ValidationResult.Fail($"City '{action.city_name}' not initialized.", action.action_type);
            }

            if (city.rallyDone)
            {
                return ValidationResult.Fail(
                    $"City '{city.cityName}' already hosted a rally (no-repeat rule).",
                    action.action_type);
            }

            return ValidationResult.Ok("VOTE_RALLY_CITY valid.");
        }

        private static ValidationResult ValidateSpeechStrategy(AiActionItem action)
        {
            if (!IdeologyCodec.TryParse(action.ideology_focus, out _))
            {
                return ValidationResult.Fail(
                    $"Invalid ideology_focus '{action.ideology_focus}'. Must be one of: nationalist, liberal, democrat, communist, imperialist.",
                    action.action_type);
            }

            if (!string.IsNullOrWhiteSpace(action.city_name) && !CityCatalog.TryParse(action.city_name, out _))
            {
                return ValidationResult.Fail($"Unknown city_name '{action.city_name}'.", action.action_type);
            }

            return ValidationResult.Ok("CHOOSE_SPEECH_STRATEGY valid.");
        }

        private static ValidationResult ValidateElectionBribe(
            AiActionItem action,
            string playerId,
            PoliticalMayhemGameManager gameManager,
            ref float remainingBudget)
        {
            if (gameManager.CurrentPhase != TurnPhase.ELECTION_BRIBE)
            {
                return ValidationResult.Fail(
                    $"ALLOCATE_ELECTION_BRIBE strictly forbidden during {gameManager.CurrentPhase}. Party budget is locked until ELECTION_BRIBE.",
                    action.action_type);
            }

            if (!CityCatalog.TryParse(action.city_name, out CityId cityId))
            {
                return ValidationResult.Fail($"Unknown city_name '{action.city_name}'.", action.action_type);
            }

            ElectionNightController electionCtrl = ElectionNightController.Instance;
            if (electionCtrl != null)
            {
                if (cityId != electionCtrl.CurrentElectionCityId)
                {
                    return ValidationResult.Fail(
                        $"Bribe target '{action.city_name}' must match active election city '{CityCatalog.ToKey(electionCtrl.CurrentElectionCityId)}'.",
                        action.action_type);
                }

                if (electionCtrl.IsBribeLocked(playerId))
                {
                    return ValidationResult.Fail(
                        $"Election bribe already locked for {playerId} in {CityCatalog.GetDisplayName(cityId)}.",
                        action.action_type);
                }
            }

            if (float.IsNaN(action.bribe_amount) || float.IsInfinity(action.bribe_amount) || action.bribe_amount <= 0f)
            {
                return ValidationResult.Fail($"Invalid bribe_amount {action.bribe_amount}; must be > 0.", action.action_type);
            }

            if (action.bribe_amount > remainingBudget)
            {
                return ValidationResult.Fail(
                    $"Bribe exceeds party budget: requested {action.bribe_amount:N0}, available {remainingBudget:N0}.",
                    action.action_type);
            }

            remainingBudget -= action.bribe_amount;
            return ValidationResult.Ok("ALLOCATE_ELECTION_BRIBE valid.");
        }
    }
}
