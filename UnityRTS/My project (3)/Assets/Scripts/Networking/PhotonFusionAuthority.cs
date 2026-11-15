using System;
using System.Collections.Generic;
using PoliticalMayhem.AI;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using PoliticalMayhem.Rally;
using PoliticalMayhem.Units;
using UnityEngine;
#if PHOTON_FUSION
using Fusion;
using Fusion.Sockets;
#endif

namespace PoliticalMayhem.Networking
{
    /// <summary>
    /// Photon Fusion 2 Host-Mode Authority Layer.
    /// Compiles cleanly out of the box using Local Loopback Host Authority when PHOTON_FUSION is not defined,
    /// and activates full Photon Fusion 2 NetworkRunner + RPCs + [Networked] state sync when Fusion 2 SDK is installed.
    /// Every manual UI command and every AI-approved ActionPlan passes through the exact same Host validation path.
    /// </summary>
#if PHOTON_FUSION
    public class PhotonFusionAuthority : NetworkBehaviour, INetworkRunnerCallbacks
#else
    public class PhotonFusionAuthority : MonoBehaviour
#endif
    {
        public static PhotonFusionAuthority Instance { get; private set; }

        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private UnitController _unitController;
        [SerializeField] private RallyController _rallyController;
        [SerializeField] private ElectionNightController _electionController;

        [Header("Multiplayer Mode")]
        [SerializeField] private string _sessionRoomName = "PoliticalMayhemRoom01";
        [SerializeField] private bool _isHostAuthority = true;
        [SerializeField] private string _networkStatus = "Local Host Authority Simulation (Fusion 2 Ready)";

#if PHOTON_FUSION
        private NetworkRunner _runner;

        [Networked] public int NetRoundNumber { get; set; }
        [Networked] public int NetPhaseInt { get; set; }
        [Networked] public int NetMatchSeed { get; set; }
        [Networked] public float NetPlayerABudget { get; set; }
        [Networked] public float NetPlayerBBudget { get; set; }
#endif

        public bool IsHostAuthority => _isHostAuthority;
        public string NetworkStatus => _networkStatus;
        public string SessionRoomName => _sessionRoomName;
        public bool IsFusionSdkEnabled
        {
            get
            {
#if PHOTON_FUSION
                return true;
#else
                return false;
#endif
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResolveReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ResolveReferences()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_unitController == null)
                _unitController = FindAnyObjectByType<UnitController>();
            if (_rallyController == null)
                _rallyController = FindAnyObjectByType<RallyController>();
            if (_electionController == null)
                _electionController = FindAnyObjectByType<ElectionNightController>();
        }

        // --- Session Lifecycle ---
        public async void StartHost(string roomName = null)
        {
            if (!string.IsNullOrWhiteSpace(roomName))
                _sessionRoomName = roomName;

#if PHOTON_FUSION
            await StartFusionSession(GameMode.Host);
#else
            _isHostAuthority = true;
            _networkStatus = $"Local Host Authority Active (Room: {_sessionRoomName})";
            if (_gameManager != null)
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerAId);
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

        public async void StartClient(string roomName = null)
        {
            if (!string.IsNullOrWhiteSpace(roomName))
                _sessionRoomName = roomName;

#if PHOTON_FUSION
            await StartFusionSession(GameMode.Client);
#else
            // In local loopback simulation, switching to Client simulates Player B perspective
            // while Host authority remains on the authoritative loop.
            _isHostAuthority = true;
            _networkStatus = $"Local Loopback Client View (Player B) -> Host Authority ({_sessionRoomName})";
            if (_gameManager != null)
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerBId);
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

        // --- Unified Authoritative Gameplay Requests (Manual UI + AI Approved ActionPlan) ---

        public bool RequestDeployUnit(string playerId, UnitType unitType, CityId cityId, string targetZone, int count, out string resultMessage)
        {
            ResolveReferences();
            AiActionItem item = new AiActionItem
            {
                action_type = "DEPLOY_UNIT",
                unit_type = UnitCatalog.ToKey(unitType),
                city_name = CityCatalog.ToKey(cityId),
                target_zone = string.IsNullOrWhiteSpace(targetZone) ? CityCatalog.GetPrimaryZoneName(cityId) : targetZone,
                count = count,
                reason = "Player/AI deployment request"
            };

            ValidationResult validation = GameActionValidator.ValidateAction(item, playerId, _gameManager, logResult: true);
            if (!validation.isValid)
            {
                resultMessage = validation.reason;
                return false;
            }

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestDeployUnit(PlayerIdentity.Normalize(playerId), (int)unitType, (int)cityId, item.target_zone, count);
                resultMessage = "Deployment RPC sent to Host.";
                return true;
            }
#endif
            AuthoritativeDeployUnit(PlayerIdentity.Normalize(playerId), unitType, cityId, item.target_zone, count);
            resultMessage = $"Deployed {count}x {UnitCatalog.GetDisplayNameTr(unitType)} at {CityCatalog.GetDisplayName(cityId)}.";
            return true;
        }

        public void RequestMoveGroup(string playerId, List<int> unitIds, Vector3 worldPos, CityId targetCity, string targetZone)
        {
            ResolveReferences();
            if (unitIds == null || unitIds.Count == 0) return;

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestMoveGroup(PlayerIdentity.Normalize(playerId), unitIds.ToArray(), worldPos, (int)targetCity, targetZone ?? "");
                return;
            }
#endif
            AuthoritativeMoveGroup(PlayerIdentity.Normalize(playerId), unitIds, worldPos, targetCity, targetZone);
        }

        public void RequestAttackOrder(string playerId, List<int> attackerIds, int targetUnitId)
        {
            ResolveReferences();
            if (attackerIds == null || attackerIds.Count == 0) return;

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestAttackOrder(PlayerIdentity.Normalize(playerId), attackerIds.ToArray(), targetUnitId);
                return;
            }
#endif
            if (_unitController != null)
                _unitController.ExecuteAttackOrder(playerId, attackerIds, targetUnitId);
        }

        public bool RequestVoteRallyCity(string playerId, CityId cityId, out string resultMessage)
        {
            ResolveReferences();
            AiActionItem item = new AiActionItem
            {
                action_type = "VOTE_RALLY_CITY",
                city_name = CityCatalog.ToKey(cityId),
                reason = "Rally city nomination vote"
            };

            ValidationResult validation = GameActionValidator.ValidateAction(item, playerId, _gameManager, logResult: true);
            if (!validation.isValid)
            {
                resultMessage = validation.reason;
                return false;
            }

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestVoteRallyCity(PlayerIdentity.Normalize(playerId), (int)cityId);
                resultMessage = "City vote RPC sent to Host.";
                return true;
            }
#endif
            bool ok = _rallyController != null && _rallyController.SubmitCityVote(playerId, cityId);
            resultMessage = ok ? $"Voted for {CityCatalog.GetDisplayName(cityId)}." : "Vote failed.";
            return ok;
        }

        public bool RequestChooseSpeechStrategy(string playerId, IdeologyType ideology, CityId? cityId, out string resultMessage)
        {
            ResolveReferences();
            PlayerState player = _gameManager != null ? _gameManager.GetPlayerState(playerId) : null;
            if (player == null)
            {
                resultMessage = "Player state unavailable.";
                return false;
            }

            string key = IdeologyCodec.ToKey(ideology);
#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestChooseSpeechStrategy(PlayerIdentity.Normalize(playerId), key);
                resultMessage = $"Speech strategy RPC ({key}) sent to Host.";
                return true;
            }
#endif
            player.selectedSpeechStrategy = key;
            _gameManager.RecordPublicEvent(
                "SPEECH_STRATEGY",
                $"{PlayerIdentity.GetDisplayName(playerId)} konuşma odağını {IdeologyCodec.GetDisplayNameTr(ideology)} olarak belirledi.",
                cityId.HasValue ? CityCatalog.ToKey(cityId.Value) : string.Empty);
            resultMessage = $"Speech strategy updated to {key}.";
            return true;
        }

        public bool RequestAllocateElectionBribe(string playerId, CityId cityId, float amount, out string resultMessage)
        {
            ResolveReferences();
            AiActionItem item = new AiActionItem
            {
                action_type = "ALLOCATE_ELECTION_BRIBE",
                city_name = CityCatalog.ToKey(cityId),
                bribe_amount = amount,
                reason = "Election Night bribe allocation"
            };

            ValidationResult validation = GameActionValidator.ValidateAction(item, playerId, _gameManager, logResult: true);
            if (!validation.isValid)
            {
                resultMessage = validation.reason;
                return false;
            }

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestAllocateElectionBribe(PlayerIdentity.Normalize(playerId), (int)cityId, amount);
                resultMessage = "Bribe allocation RPC sent to Host.";
                return true;
            }
#endif
            if (_electionController == null)
            {
                resultMessage = "ElectionNightController missing.";
                return false;
            }

            return _electionController.SubmitBribeAllocation(playerId, cityId, amount, out resultMessage);
        }

        /// <summary>
        /// Executes an explicitly human-approved AI ActionPlan through the exact same Host validation & authority path.
        /// </summary>
        public bool RequestExecuteApprovedActionPlan(string playerId, ActionPlan plan, out string resultMessage)
        {
            ResolveReferences();
            string normalized = PlayerIdentity.Normalize(playerId);

            ValidationResult validation = GameActionValidator.ValidatePlan(plan, normalized, _gameManager, logResult: true);
            if (!validation.isValid)
            {
                resultMessage = $"Host Authority rejected plan: {validation.reason}";
                return false;
            }

#if PHOTON_FUSION
            if (_runner != null && _runner.IsRunning && !Object.HasStateAuthority)
            {
                RpcRequestExecuteApprovedActionPlan(normalized, JsonUtility.ToJson(plan));
                resultMessage = "Approved ActionPlan RPC submitted to Photon Host Authority.";
                return true;
            }
#endif
            return ExecuteValidatedPlanOnAuthority(normalized, plan, out resultMessage);
        }

        private bool ExecuteValidatedPlanOnAuthority(string playerId, ActionPlan plan, out string resultMessage)
        {
            ValidationResult finalRevalidation = GameActionValidator.ValidatePlan(plan, playerId, _gameManager, logResult: true);
            if (!finalRevalidation.isValid)
            {
                resultMessage = $"Authority revalidation rejected plan: {finalRevalidation.reason}";
                return false;
            }

            int executedCount = 0;
            for (int i = 0; i < plan.actions.Count; i++)
            {
                AiActionItem a = plan.actions[i];
                switch (a.action_type)
                {
                    case "DEPLOY_UNIT":
                        if (UnitCatalog.TryParse(a.unit_type, out UnitType ut) &&
                            CityCatalog.TryParse(a.city_name, out CityId cid))
                        {
                            AuthoritativeDeployUnit(playerId, ut, cid, a.target_zone, a.count);
                            executedCount++;
                        }
                        break;

                    case "MOVE_GROUP":
                    case "MOVE_UNIT":
                        if (CityCatalog.TryParse(a.city_name, out CityId moveCity) && _unitController != null)
                        {
                            string zone = string.IsNullOrWhiteSpace(a.target_zone)
                                ? CityCatalog.GetPrimaryZoneName(moveCity)
                                : a.target_zone;
                            Vector3 dest = _gameManager != null && _gameManager.CityManager != null
                                ? _gameManager.CityManager.GetZoneWorldPosition(moveCity, zone)
                                : CityCatalog.GetWorldPosition(moveCity) + CityCatalog.GetZoneOffset(zone);

                            int moveCount = a.action_type == "MOVE_UNIT" && a.count <= 0 ? 1 : a.count;
                            int moved = _unitController.ExecuteMoveUnitsByTypeOrId(
                                playerId,
                                a.unit_id,
                                a.unit_type,
                                moveCount,
                                dest,
                                moveCity,
                                zone);
                            if (moved > 0)
                                executedCount++;
                        }
                        break;

                    case "VOTE_RALLY_CITY":
                        {
                            RallyController rallyCtrl = _rallyController != null ? _rallyController : RallyController.Instance;
                            if (CityCatalog.TryParse(a.city_name, out CityId voteCity) &&
                                rallyCtrl != null &&
                                _gameManager != null &&
                                _gameManager.CurrentPhase == TurnPhase.CITY_VOTE)
                            {
                                if (rallyCtrl.CastCityVote(playerId, voteCity))
                                    executedCount++;
                            }
                        }
                        break;

                    case "CHOOSE_SPEECH_STRATEGY":
                        if (IdeologyCodec.TryParse(a.ideology_focus, out IdeologyType focus))
                        {
                            CityId? stratCity = CityCatalog.TryParse(a.city_name, out CityId sc) ? sc : (CityId?)null;
                            if (RequestChooseSpeechStrategy(playerId, focus, stratCity, out _))
                                executedCount++;
                        }
                        break;

                    case "ALLOCATE_ELECTION_BRIBE":
                        {
                            ElectionNightController electionCtrl = _electionController != null ? _electionController : ElectionNightController.Instance;
                            if (CityCatalog.TryParse(a.city_name, out CityId bribeCity) && electionCtrl != null)
                            {
                                if (electionCtrl.SubmitBribeAllocation(playerId, bribeCity, a.bribe_amount, out _))
                                    executedCount++;
                            }
                        }
                        break;
                }
            }

            resultMessage = $"Host Authority executed {executedCount}/{plan.actions.Count} approved AI action(s).";
            return true;
        }

        private void AuthoritativeDeployUnit(string playerId, UnitType unitType, CityId cityId, string targetZone, int count)
        {
            if (_unitController != null)
            {
                _unitController.ExecuteDeployUnit(playerId, unitType, cityId, targetZone, count);
            }
        }

        private void AuthoritativeMoveGroup(string playerId, List<int> unitIds, Vector3 worldPos, CityId targetCity, string targetZone)
        {
            if (_unitController != null)
            {
                _unitController.ExecuteMoveGroup(playerId, unitIds, worldPos, targetCity, targetZone);
            }
        }

#if PHOTON_FUSION
        private async System.Threading.Tasks.Task StartFusionSession(GameMode mode)
        {
            if (_runner == null)
            {
                _runner = gameObject.GetComponent<NetworkRunner>();
                if (_runner == null) _runner = gameObject.AddComponent<NetworkRunner>();
            }
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            StartGameArgs args = new StartGameArgs
            {
                GameMode = mode,
                SessionName = _sessionRoomName,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            StartGameResult result = await _runner.StartGame(args);
            _isHostAuthority = result.Ok && (_runner.IsServer || _runner.IsSharedModeMasterClient);
            _networkStatus = result.Ok
                ? $"Photon Fusion 2 Connected ({mode} / Room: {_sessionRoomName})"
                : $"Photon Fusion 2 Error: {result.ShutdownReason}";
        }

        public override void FixedUpdateNetwork()
        {
            if (Object != null && Object.HasStateAuthority && _gameManager != null)
            {
                NetRoundNumber = _gameManager.RoundNumber;
                NetPhaseInt = (int)_gameManager.CurrentPhase;
                NetMatchSeed = _gameManager.MatchSeed;
                NetPlayerABudget = _gameManager.PlayerA.partyBudget;
                NetPlayerBBudget = _gameManager.PlayerB.partyBudget;
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestDeployUnit(string playerId, int unitTypeInt, int cityIdInt, string targetZone, int count)
        {
            RequestDeployUnit(playerId, (UnitType)unitTypeInt, (CityId)cityIdInt, targetZone, count, out _);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestMoveGroup(string playerId, int[] unitIds, Vector3 worldPos, int targetCityInt, string targetZone)
        {
            AuthoritativeMoveGroup(playerId, new List<int>(unitIds), worldPos, (CityId)targetCityInt, targetZone);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestAttackOrder(string playerId, int[] attackerIds, int targetUnitId)
        {
            if (_unitController != null)
                _unitController.ExecuteAttackOrder(playerId, new List<int>(attackerIds), targetUnitId);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestVoteRallyCity(string playerId, int cityIdInt)
        {
            if (_rallyController != null)
                _rallyController.SubmitCityVote(playerId, (CityId)cityIdInt);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestChooseSpeechStrategy(string playerId, string ideologyKey)
        {
            if (IdeologyCodec.TryParse(ideologyKey, out IdeologyType ideology))
                RequestChooseSpeechStrategy(playerId, ideology, null, out _);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestAllocateElectionBribe(string playerId, int cityIdInt, float amount)
        {
            RequestAllocateElectionBribe(playerId, (CityId)cityIdInt, amount, out _);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RpcRequestExecuteApprovedActionPlan(string playerId, string planJson)
        {
            ActionPlan plan = JsonUtility.FromJson<ActionPlan>(planJson);
            ExecuteValidatedPlanOnAuthority(playerId, plan, out _);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            _networkStatus = $"Fusion Player Joined: {player.PlayerId}";
            if (_gameManager != null && !runner.IsServer && runner.LocalPlayer == player)
                _gameManager.SetLocalPlayerPerspective(PlayerIdentity.PlayerBId);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { _networkStatus = $"Fusion Shutdown: {shutdownReason}"; }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
#endif
    }
}
