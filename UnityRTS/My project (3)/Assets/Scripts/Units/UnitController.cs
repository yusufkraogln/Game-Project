using System;
using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Networking;
using UnityEngine;

namespace PoliticalMayhem.Units
{
    /// <summary>
    /// Handles RTS unit selection (single click + drag marquee), right-click formation movement,
    /// attack targeting, visual primitive creation, and authoritative unit spawning/execution.
    /// </summary>
    public class UnitController : MonoBehaviour
    {
        public const int MaxTotalActiveUnitsPerPlayer = 24;

        [Header("Scene Wiring")]
        [SerializeField] private PoliticalMayhemGameManager _gameManager;
        [SerializeField] private Camera _rtsCamera;
        [SerializeField] private Transform _unitsRoot;

        [Header("Selection Box Style")]
        [SerializeField] private Color _marqueeFill = new Color(0.25f, 0.85f, 0.45f, 0.16f);
        [SerializeField] private Color _marqueeBorder = new Color(0.25f, 0.95f, 0.45f, 0.90f);

        private readonly List<CampaignUnit> _selectedUnits = new List<CampaignUnit>();
        private int _nextUnitId = 1;
        private bool _isDragging;
        private Vector2 _dragStartScreen;
        private Vector2 _dragCurrentScreen;
        private Texture2D _whiteTex;

        /// <summary>
        /// Optional delegate set by PoliticalMayhemHUD to prevent clicks over IMGUI panels
        /// from triggering 3D RTS selection/move commands.
        /// </summary>
        public Func<Vector2, bool> IsScreenPointOverHud;

        public IReadOnlyList<CampaignUnit> SelectedUnits => _selectedUnits;

        private void Awake()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_rtsCamera == null)
                _rtsCamera = Camera.main;

            EnsureUnitsRoot();
        }

        private void OnEnable()
        {
            if (_gameManager == null)
                _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();

            if (_gameManager != null)
                _gameManager.OnMatchInitialized += HandleMatchInitialized;
        }

        private void OnDisable()
        {
            if (_gameManager != null)
                _gameManager.OnMatchInitialized -= HandleMatchInitialized;
        }

        private void Start()
        {
            if (_gameManager != null && CampaignUnit.ActiveUnits.Count == 0)
            {
                SpawnInitialArmies();
            }
        }

        private void HandleMatchInitialized()
        {
            ClearAllUnits();
            SpawnInitialArmies();
        }

        private void EnsureUnitsRoot()
        {
            if (_unitsRoot != null) return;
            GameObject existing = GameObject.Find("UnitsRoot");
            if (existing == null)
                existing = new GameObject("UnitsRoot");
            _unitsRoot = existing.transform;
        }

        public void ClearAllUnits()
        {
            _selectedUnits.Clear();
            _nextUnitId = 1;
            List<CampaignUnit> snapshot = new List<CampaignUnit>(CampaignUnit.ActiveUnits);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] != null)
                {
                    snapshot[i].gameObject.SetActive(false);
                    Destroy(snapshot[i].gameObject);
                }
            }
        }

        /// <summary>
        /// Gives both players identical initial campaign squads without spending Party Budget.
        /// Places a hidden forward team in Limankent/Demirkent to demonstrate Fog-of-War isolation.
        /// </summary>
        public void SpawnInitialArmies()
        {
            if (_gameManager == null) return;

            // Player A equal starter army (4 units)
            SpawnUnitInternal(PlayerIdentity.PlayerAId, UnitType.CampaignVolunteer, CityId.Baskent, "WestGate", new Vector3(-8f, 0f, -2f));
            SpawnUnitInternal(PlayerIdentity.PlayerAId, UnitType.Propagandist, CityId.Baskent, "WestGate", new Vector3(-9.5f, 0f, -3.5f));
            SpawnUnitInternal(PlayerIdentity.PlayerAId, UnitType.Bodyguard, CityId.Baskent, "WestGate", new Vector3(-6.5f, 0f, -1f));
            SpawnUnitInternal(PlayerIdentity.PlayerAId, UnitType.CampaignVolunteer, CityId.Limankent, "HarborGate", new Vector3(-28f, 0f, -11f));

            // Player B equal starter army (4 units) — Demirkent unit is outside Player A's vision (Fog of War!)
            SpawnUnitInternal(PlayerIdentity.PlayerBId, UnitType.CampaignVolunteer, CityId.Baskent, "EastGate", new Vector3(11f, 0f, 2f));
            SpawnUnitInternal(PlayerIdentity.PlayerBId, UnitType.Propagandist, CityId.Baskent, "EastGate", new Vector3(12.5f, 0f, 3.5f));
            SpawnUnitInternal(PlayerIdentity.PlayerBId, UnitType.Bodyguard, CityId.Baskent, "EastGate", new Vector3(9.5f, 0f, 1f));
            SpawnUnitInternal(PlayerIdentity.PlayerBId, UnitType.CampaignVolunteer, CityId.Demirkent, "IndustrialDistrict", new Vector3(28f, 0f, -9f));
        }

        private void Update()
        {
            if (_rtsCamera == null) _rtsCamera = Camera.main;
            if (_rtsCamera == null || _gameManager == null) return;

            RefreshFogOfWarVisibility();

            // Prune destroyed, non-owned, or FoW-hidden units from selection
            for (int i = _selectedUnits.Count - 1; i >= 0; i--)
            {
                CampaignUnit sel = _selectedUnits[i];
                if (sel == null || !sel.IsAlive ||
                    sel.OwnerPlayerId != _gameManager.LocalPlayerId ||
                    !sel.IsVisuallyVisible)
                {
                    if (sel != null) sel.SetSelected(false);
                    _selectedUnits.RemoveAt(i);
                }
            }

            Vector3 mousePos = InputCompat.MousePosition;
            bool blockedByHud = IsScreenPointOverHud != null && IsScreenPointOverHud(new Vector2(mousePos.x, mousePos.y));

            if (InputCompat.GetMouseButtonDown(0) && !blockedByHud)
            {
                _isDragging = true;
                _dragStartScreen = mousePos;
                _dragCurrentScreen = mousePos;
            }

            if (_isDragging && InputCompat.GetMouseButton(0))
            {
                _dragCurrentScreen = mousePos;
            }

            if (_isDragging && InputCompat.GetMouseButtonUp(0))
            {
                _isDragging = false;
                _dragCurrentScreen = mousePos;
                float dragDist = Vector2.Distance(_dragStartScreen, _dragCurrentScreen);
                if (dragDist < 10f)
                    HandleSingleLeftClick(_dragCurrentScreen);
                else
                    HandleMarqueeSelect(_dragStartScreen, _dragCurrentScreen);
            }

            if (InputCompat.GetMouseButtonDown(1) && !blockedByHud && _selectedUnits.Count > 0)
            {
                HandleRightClickCommand(mousePos);
            }
        }

        private void HandleSingleLeftClick(Vector2 screenPos)
        {
            bool additive = InputCompat.IsShiftHeld();
            if (!additive) ClearSelection();

            Ray ray = _rtsCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 250f))
            {
                CampaignUnit unit = hit.collider.GetComponentInParent<CampaignUnit>();
                if (unit != null && unit.IsAlive && unit.OwnerPlayerId == _gameManager.LocalPlayerId)
                {
                    SelectUnit(unit, true);
                }
            }
        }

        private void HandleMarqueeSelect(Vector2 start, Vector2 end)
        {
            if (!InputCompat.IsShiftHeld()) ClearSelection();

            float minX = Mathf.Min(start.x, end.x);
            float maxX = Mathf.Max(start.x, end.x);
            float minY = Mathf.Min(start.y, end.y);
            float maxY = Mathf.Max(start.y, end.y);

            string localId = _gameManager.LocalPlayerId;
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit unit = all[i];
                if (unit == null || !unit.IsAlive || unit.OwnerPlayerId != localId) continue;

                Vector3 screen = _rtsCamera.WorldToScreenPoint(unit.transform.position);
                if (screen.z < 0f) continue;

                if (screen.x >= minX && screen.x <= maxX && screen.y >= minY && screen.y <= maxY)
                {
                    SelectUnit(unit, true);
                }
            }
        }

        private void HandleRightClickCommand(Vector3 screenPos)
        {
            Ray ray = _rtsCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f)) return;

            string localId = _gameManager.LocalPlayerId;
            List<int> selectedIds = GetSelectedUnitIds();
            if (selectedIds.Count == 0) return;

            CampaignUnit hitUnit = hit.collider.GetComponentInParent<CampaignUnit>();
            if (hitUnit != null && hitUnit.IsAlive && hitUnit.OwnerPlayerId != localId && hitUnit.IsVisuallyVisible)
            {
                if (PhotonFusionAuthority.Instance != null)
                {
                    PhotonFusionAuthority.Instance.RequestAttackOrder(localId, selectedIds, hitUnit.UnitId);
                }
                else
                {
                    ExecuteAttackOrder(localId, selectedIds, hitUnit.UnitId);
                }
                return;
            }

            Vector3 targetPoint = new Vector3(hit.point.x, 0f, hit.point.z);
            CityId nearestCity = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetClosestCity(targetPoint)
                : CityId.Baskent;
            string zone = CityCatalog.GetPrimaryZoneName(nearestCity);

            if (PhotonFusionAuthority.Instance != null)
            {
                PhotonFusionAuthority.Instance.RequestMoveGroup(localId, selectedIds, targetPoint, nearestCity, zone);
            }
            else
            {
                ExecuteMoveGroup(localId, selectedIds, targetPoint, nearestCity, zone);
            }
        }

        public void SelectUnit(CampaignUnit unit, bool addToSelection)
        {
            if (unit == null) return;
            if (!addToSelection) ClearSelection();
            if (!_selectedUnits.Contains(unit))
            {
                _selectedUnits.Add(unit);
                unit.SetSelected(true);
            }
        }

        public void SelectAllOwnedUnits(string playerId)
        {
            ClearSelection();
            string normalized = PlayerIdentity.Normalize(playerId);
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].IsAlive && all[i].OwnerPlayerId == normalized)
                {
                    SelectUnit(all[i], true);
                }
            }
        }

        public void ClearSelection()
        {
            for (int i = 0; i < _selectedUnits.Count; i++)
            {
                if (_selectedUnits[i] != null)
                    _selectedUnits[i].SetSelected(false);
            }
            _selectedUnits.Clear();
        }

        public List<int> GetSelectedUnitIds()
        {
            List<int> ids = new List<int>(_selectedUnits.Count);
            for (int i = 0; i < _selectedUnits.Count; i++)
            {
                if (_selectedUnits[i] != null && _selectedUnits[i].IsAlive)
                    ids.Add(_selectedUnits[i].UnitId);
            }
            return ids;
        }

        public static int CountActiveUnitsForPlayer(string playerId)
        {
            string normalized = PlayerIdentity.Normalize(playerId);
            int count = 0;
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit u = all[i];
                if (u != null && u.IsAlive && u.OwnerPlayerId == normalized)
                    count++;
            }
            return count;
        }

        private void RefreshFogOfWarVisibility()
        {
            if (_gameManager == null) return;
            string localId = _gameManager.LocalPlayerId;
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit unit = all[i];
                if (unit == null || !unit.IsAlive) continue;

                bool visible = unit.OwnerPlayerId == localId || _gameManager.IsOpponentUnitVisibleTo(localId, unit);
                unit.SetVisualVisibility(visible);
            }
        }

        /// <summary>
        /// Authoritative Host execution for unit deployment (0 party_budget spent; uses round capacity & total cap).
        /// </summary>
        public int ExecuteDeployUnit(string playerId, UnitType unitType, CityId cityId, string targetZone, int count)
        {
            if (_gameManager == null || count <= 0) return 0;
            string normalized = PlayerIdentity.Normalize(playerId);
            int remainingTotal = Mathf.Max(0, MaxTotalActiveUnitsPerPlayer - CountActiveUnitsForPlayer(normalized));
            int toSpawn = Mathf.Min(count, remainingTotal);
            if (toSpawn <= 0) return 0;

            PlayerState player = _gameManager.GetPlayerState(normalized);

            Vector3 basePos = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetZoneWorldPosition(cityId, targetZone)
                : CityCatalog.GetWorldPosition(cityId) + CityCatalog.GetZoneOffset(targetZone);

            // Offset slightly by player side so opposing spawns don't overlap identically
            Vector3 sideBias = normalized == PlayerIdentity.PlayerBId
                ? new Vector3(3.0f, 0f, 2.0f)
                : new Vector3(-3.0f, 0f, -2.0f);

            int spawned = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                Vector3 formationOffset = ComputeFormationOffset(i, toSpawn, 1.8f);
                Vector3 spawnPos = CampaignUnit.SanitizePlayfieldPosition(basePos + sideBias + formationOffset);
                SpawnUnitInternal(normalized, unitType, cityId, targetZone, spawnPos);
                spawned++;
            }

            player.IncrementDeployedThisRound(unitType, spawned);
            _gameManager.RecordPublicEvent(
                "UNIT_DEPLOYED",
                $"{PlayerIdentity.GetDisplayName(normalized)} {CityCatalog.GetDisplayName(cityId)} bölgesine {spawned}x {UnitCatalog.GetDisplayNameTr(unitType)} gönderdi.",
                CityCatalog.ToKey(cityId));

            return spawned;
        }

        /// <summary>
        /// Authoritative Host execution for moving explicit unit IDs in a neat grid formation.
        /// </summary>
        public int ExecuteMoveGroup(string playerId, List<int> unitIds, Vector3 worldCenter, CityId targetCity, string targetZone)
        {
            if (unitIds == null || unitIds.Count == 0) return 0;
            string normalized = PlayerIdentity.Normalize(playerId);

            List<CampaignUnit> validUnits = new List<CampaignUnit>();
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit u = all[i];
                if (u != null && u.IsAlive && u.OwnerPlayerId == normalized && unitIds.Contains(u.UnitId))
                    validUnits.Add(u);
            }

            for (int i = 0; i < validUnits.Count; i++)
            {
                Vector3 slot = worldCenter + ComputeFormationOffset(i, validUnits.Count, 1.9f);
                validUnits[i].IssueMoveOrder(slot, targetCity, targetZone);
            }

            return validUnits.Count;
        }

        /// <summary>
        /// Authoritative Host execution for AI/HUD group movement by unit type + count to a target city/zone.
        /// </summary>
        public int ExecuteMoveGroupByCriteria(
            string playerId,
            UnitType? filterType,
            int count,
            CityId targetCity,
            string targetZone)
        {
            if (_gameManager == null || count <= 0) return 0;
            string normalized = PlayerIdentity.Normalize(playerId);

            Vector3 dest = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetZoneWorldPosition(targetCity, targetZone)
                : CityCatalog.GetWorldPosition(targetCity) + CityCatalog.GetZoneOffset(targetZone);

            List<int> ids = CollectMovableUnitIds(normalized, unitId: 0, filterType, count, dest);
            if (ids.Count == 0) return 0;

            int moved = ExecuteMoveGroup(normalized, ids, dest, targetCity, targetZone);
            if (moved > 0)
            {
                _gameManager.RecordPublicEvent(
                    "UNIT_MOVE",
                    $"{PlayerIdentity.GetDisplayName(normalized)} {moved} birimi {CityCatalog.GetDisplayName(targetCity)} ({targetZone}) konumuna sevk etti.",
                    CityCatalog.ToKey(targetCity));
            }
            return moved;
        }

        /// <summary>
        /// Moves a specific owned unit (when unitId > 0) or up to count matching units (all when count <= 0)
        /// by unit_type (or any owned unit if unitTypeStr is empty), prioritizing units not already at targetPos.
        /// </summary>
        public int ExecuteMoveUnitsByTypeOrId(
            string playerId,
            int unitId,
            string unitTypeStr,
            int count,
            Vector3 targetPos,
            CityId? targetCityOverride = null,
            string targetZoneOverride = null)
        {
            if (_gameManager == null) return 0;
            string normalized = PlayerIdentity.Normalize(playerId);
            Vector3 clampedTarget = CampaignUnit.SanitizePlayfieldPosition(targetPos);

            UnitType? filterType = null;
            if (!string.IsNullOrWhiteSpace(unitTypeStr) && UnitCatalog.TryParse(unitTypeStr, out UnitType parsedType))
                filterType = parsedType;

            List<int> ids = CollectMovableUnitIds(normalized, unitId, filterType, count, clampedTarget);
            if (ids.Count == 0) return 0;

            CityId targetCity = targetCityOverride ?? (_gameManager.CityManager != null
                ? _gameManager.CityManager.GetClosestCity(clampedTarget)
                : CityId.Baskent);
            string targetZone = string.IsNullOrWhiteSpace(targetZoneOverride)
                ? CityCatalog.GetPrimaryZoneName(targetCity)
                : targetZoneOverride;

            int moved = ExecuteMoveGroup(normalized, ids, clampedTarget, targetCity, targetZone);
            if (moved > 0)
            {
                _gameManager.RecordPublicEvent(
                    "UNIT_MOVE",
                    $"{PlayerIdentity.GetDisplayName(normalized)} {moved} birimi {CityCatalog.GetDisplayName(targetCity)} ({targetZone}) konumuna sevk etti.",
                    CityCatalog.ToKey(targetCity));
            }
            return moved;
        }

        private static List<int> CollectMovableUnitIds(
            string normalizedPlayerId,
            int unitId,
            UnitType? filterType,
            int count,
            Vector3 targetPos)
        {
            List<int> ids = new List<int>();
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;

            if (unitId > 0)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    CampaignUnit u = all[i];
                    if (u != null && u.IsAlive && u.OwnerPlayerId == normalizedPlayerId && u.UnitId == unitId)
                    {
                        ids.Add(u.UnitId);
                        return ids;
                    }
                }
            }

            List<CampaignUnit> awayFromTarget = new List<CampaignUnit>();
            List<CampaignUnit> alreadyNearTarget = new List<CampaignUnit>();
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit u = all[i];
                if (u == null || !u.IsAlive || u.OwnerPlayerId != normalizedPlayerId) continue;
                if (filterType.HasValue && u.UnitType != filterType.Value) continue;

                float dx = u.transform.position.x - targetPos.x;
                float dz = u.transform.position.z - targetPos.z;
                if (dx * dx + dz * dz > 4.0f)
                    awayFromTarget.Add(u);
                else
                    alreadyNearTarget.Add(u);
            }

            List<CampaignUnit> ordered = new List<CampaignUnit>(awayFromTarget.Count + alreadyNearTarget.Count);
            ordered.AddRange(awayFromTarget);
            ordered.AddRange(alreadyNearTarget);

            int toMove = count > 0 ? Mathf.Min(count, ordered.Count) : ordered.Count;
            for (int i = 0; i < toMove; i++)
            {
                ids.Add(ordered[i].UnitId);
            }
            return ids;
        }

        public void ExecuteAttackOrder(string playerId, List<int> attackerIds, int targetUnitId)
        {
            if (attackerIds == null || attackerIds.Count == 0) return;
            string normalized = PlayerIdentity.Normalize(playerId);

            CampaignUnit target = FindUnitById(targetUnitId);
            if (target == null || !target.IsAlive || target.OwnerPlayerId == normalized) return;

            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                CampaignUnit u = all[i];
                if (u != null && u.IsAlive && u.OwnerPlayerId == normalized && attackerIds.Contains(u.UnitId))
                {
                    u.IssueAttackOrder(target);
                }
            }
        }

        public CampaignUnit FindUnitById(int unitId)
        {
            IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].UnitId == unitId)
                    return all[i];
            }
            return null;
        }

        public static Vector3 ComputeFormationOffset(int index, int totalCount, float spacing)
        {
            if (totalCount <= 1) return Vector3.zero;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(totalCount));
            int row = index / cols;
            int col = index % cols;
            float offsetX = (col - (cols - 1) * 0.5f) * spacing;
            float offsetZ = (row - ((totalCount - 1) / cols) * 0.5f) * spacing;
            return new Vector3(offsetX, 0f, offsetZ);
        }

        private CampaignUnit SpawnUnitInternal(
            string ownerPlayerId,
            UnitType unitType,
            CityId cityId,
            string zone,
            Vector3 worldPos)
        {
            EnsureUnitsRoot();
            int id = _nextUnitId++;

            GameObject go = CreateUnitVisualObject(unitType, id, ownerPlayerId, worldPos, out bool usedImportedModel, out Texture2D baseTex, out Texture2D emissionTex);
            go.transform.SetParent(_unitsRoot, true);

            CampaignUnit unit = go.GetComponent<CampaignUnit>();
            if (unit == null) unit = go.AddComponent<CampaignUnit>();

            Transform bodyTf = go.transform.Find("Body");
            if (usedImportedModel && bodyTf != null)
            {
                unit.ConfigureImportedModelVisuals(bodyTf, baseTex, emissionTex);
            }

            Renderer bodyRenderer = (bodyTf != null ? bodyTf.GetComponentInChildren<Renderer>() : null)
                ?? go.GetComponentInChildren<Renderer>();
            Transform ringTf = go.transform.Find("SelectionRing");
            GameObject ringGo = ringTf != null ? ringTf.gameObject : null;
            unit.BindVisuals(bodyRenderer, ringGo);

            unit.Initialize(
                id,
                ownerPlayerId,
                unitType,
                cityId,
                zone,
                _gameManager != null ? _gameManager.OverallManager : null,
                _gameManager != null ? _gameManager.CityManager : null);

            if (_gameManager != null)
            {
                bool initialVisible = unit.OwnerPlayerId == _gameManager.LocalPlayerId ||
                                      _gameManager.IsOpponentUnitVisibleTo(_gameManager.LocalPlayerId, unit);
                unit.SetVisualVisibility(initialVisible);
            }

            return unit;
        }

        private static void GetUnitModelSpec(
            UnitType unitType,
            out string modelPath,
            out string texturePath,
            out string emissionPath,
            out float targetSize,
            out bool normalizeByHeight)
        {
            switch (unitType)
            {
                case UnitType.RallyConvoy:
                    modelPath = "Models/Units/Vehicles/van";
                    texturePath = "Models/Units/Vehicles/Textures/colormap";
                    emissionPath = null;
                    targetSize = 2.45f;
                    normalizeByHeight = false;
                    return;

                case UnitType.Bodyguard:
                    modelPath = "Models/Units/Characters/blocky_03_knight";
                    texturePath = "Models/Units/Characters/blocky_palette";
                    emissionPath = "Models/Units/Characters/blocky_palette_emissive";
                    targetSize = 2.15f;
                    normalizeByHeight = true;
                    return;

                case UnitType.Propagandist:
                    modelPath = "Models/Units/Characters/blocky_19_princess";
                    texturePath = "Models/Units/Characters/blocky_palette";
                    emissionPath = "Models/Units/Characters/blocky_palette_emissive";
                    targetSize = 2.05f;
                    normalizeByHeight = true;
                    return;

                default:
                    modelPath = "Models/Units/Characters/blocky_01_hero";
                    texturePath = "Models/Units/Characters/blocky_palette";
                    emissionPath = "Models/Units/Characters/blocky_palette_emissive";
                    targetSize = 1.85f;
                    normalizeByHeight = true;
                    return;
            }
        }

        private static GameObject CreateUnitVisualObject(
            UnitType unitType,
            int id,
            string ownerPlayerId,
            Vector3 worldPos,
            out bool usedImportedModel,
            out Texture2D baseTex,
            out Texture2D emissionTex)
        {
            Vector3 safePos = CampaignUnit.SanitizePlayfieldPosition(new Vector3(worldPos.x, 0f, worldPos.z));
            GameObject root = new GameObject($"Unit_{id}_{ownerPlayerId}_{UnitCatalog.ToKey(unitType)}");
            root.transform.position = safePos;

            CapsuleCollider hitCollider = root.AddComponent<CapsuleCollider>();
            hitCollider.center = new Vector3(0f, 1.0f, 0f);
            hitCollider.radius = unitType == UnitType.RallyConvoy ? 1.2f : 0.65f;
            hitCollider.height = 2.1f;

            GetUnitModelSpec(
                unitType,
                out string modelPath,
                out string texPath,
                out string emissionPath,
                out float targetSize,
                out bool normalizeByHeight);

            baseTex = !string.IsNullOrEmpty(texPath) ? Resources.Load<Texture2D>(texPath) : null;
            emissionTex = !string.IsNullOrEmpty(emissionPath) ? Resources.Load<Texture2D>(emissionPath) : null;

            GameObject modelWrapper = ModelVisualHelper.InstantiateNormalizedModel(
                modelPath,
                texPath,
                "Body",
                root.transform,
                Vector3.zero,
                0f,
                targetSize,
                normalizeByHeight,
                0f,
                Color.white,
                0.20f,
                stripColliders: true,
                emissionResourcePath: emissionPath);

            usedImportedModel = modelWrapper != null;

            if (usedImportedModel)
            {
                if (unitType == UnitType.RallyConvoy)
                {
                    CreateVisualPart(
                        PrimitiveType.Cube,
                        "TeamAccent",
                        root.transform,
                        new Vector3(0f, 1.38f, -0.15f),
                        new Vector3(0.92f, 0.16f, 0.42f));
                }
                else
                {
                    CreateVisualPart(
                        PrimitiveType.Cylinder,
                        "TeamAccent",
                        root.transform,
                        new Vector3(0f, 0.11f, 0f),
                        new Vector3(1.12f, 0.045f, 1.12f));
                }
            }
            else
            {
                Color lightSilver = new Color(0.84f, 0.87f, 0.90f);
                Color darkSlate = new Color(0.20f, 0.23f, 0.28f);

                if (unitType == UnitType.RallyConvoy)
                {
                    // Fallback wide rectangular convoy block + top team strip
                    CreateVisualPart(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.65f, 0f), new Vector3(1.45f, 0.95f, 2.35f));
                    CreateVisualPart(PrimitiveType.Cube, "TeamAccent", root.transform, new Vector3(0f, 1.22f, 0f), new Vector3(1.15f, 0.22f, 1.85f));
                    CreateVisualPart(PrimitiveType.Cube, "FrontStrip", root.transform, new Vector3(0f, 0.72f, 1.15f), new Vector3(1.10f, 0.30f, 0.14f), darkSlate);
                }
                else if (unitType == UnitType.Bodyguard)
                {
                    // Fallback sturdy cube unit + front marker plate
                    CreateVisualPart(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.90f, 0f), new Vector3(0.90f, 1.30f, 0.72f));
                    CreateVisualPart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.78f, 0f), new Vector3(0.48f, 0.48f, 0.48f), lightSilver);
                    CreateVisualPart(PrimitiveType.Cube, "TeamAccent", root.transform, new Vector3(0.52f, 0.92f, 0.12f), new Vector3(0.16f, 0.95f, 0.60f));
                }
                else if (unitType == UnitType.Propagandist)
                {
                    // Fallback tall cylinder unit + shoulder/base collar
                    CreateVisualPart(PrimitiveType.Cylinder, "Body", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.72f, 0.85f, 0.72f));
                    CreateVisualPart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.88f, 0f), new Vector3(0.46f, 0.46f, 0.46f), lightSilver);
                    CreateVisualPart(PrimitiveType.Cylinder, "TeamAccent", root.transform, new Vector3(0f, 0.16f, 0f), new Vector3(1.08f, 0.08f, 1.08f));
                }
                else
                {
                    // Fallback clean capsule volunteer + top banner cube
                    CreateVisualPart(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.65f, 0.75f, 0.65f));
                    CreateVisualPart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.76f, 0f), new Vector3(0.44f, 0.44f, 0.44f), lightSilver);
                    CreateVisualPart(PrimitiveType.Cube, "TeamAccent", root.transform, new Vector3(0f, 2.22f, 0f), new Vector3(0.52f, 0.22f, 0.20f));
                }
            }

            // Crisp cyan-white selection ring on dark ground
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            float ringScale = unitType == UnitType.RallyConvoy ? 2.65f : 1.55f;
            ring.transform.localScale = new Vector3(ringScale, 0.04f, ringScale);
            MaterialHelper.ApplyColor(ring.GetComponent<Renderer>(), new Color(0.55f, 0.90f, 1.0f), 0.35f);
            ring.SetActive(false);

            // 3D floating health bar (shown when selected or damaged, hidden under Fog-of-War)
            GameObject healthBarRoot = new GameObject("HealthBarRoot");
            healthBarRoot.transform.SetParent(root.transform, false);
            float hbHeight = unitType == UnitType.RallyConvoy ? 2.15f : 2.65f;
            healthBarRoot.transform.localPosition = new Vector3(0f, hbHeight, 0f);

            CreateVisualPart(
                PrimitiveType.Cube,
                "HealthBarBg",
                healthBarRoot.transform,
                Vector3.zero,
                new Vector3(1.16f, 0.14f, 0.10f),
                new Color(0.10f, 0.13f, 0.18f));
            CreateVisualPart(
                PrimitiveType.Cube,
                "HealthBarFill",
                healthBarRoot.transform,
                new Vector3(0f, 0f, -0.02f),
                new Vector3(1.08f, 0.10f, 0.11f),
                new Color(0.22f, 0.78f, 0.42f));
            healthBarRoot.SetActive(false);

            return root;
        }

        private static GameObject CreateVisualPart(
            PrimitiveType primitive,
            string partName,
            Transform parent,
            Vector3 localPos,
            Vector3 localScale,
            Color? overrideColor = null)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;
            if (overrideColor.HasValue)
            {
                MaterialHelper.ApplyColor(part.GetComponent<Renderer>(), overrideColor.Value, 0.3f);
            }
            return part;
        }

        private void OnGUI()
        {
            if (!_isDragging) return;
            if (_whiteTex == null)
            {
                _whiteTex = new Texture2D(1, 1);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply();
            }

            // Convert bottom-left screen coords to top-left GUI coords
            float x1 = _dragStartScreen.x;
            float y1 = Screen.height - _dragStartScreen.y;
            float x2 = _dragCurrentScreen.x;
            float y2 = Screen.height - _dragCurrentScreen.y;

            Rect rect = new Rect(Mathf.Min(x1, x2), Mathf.Min(y1, y2), Mathf.Abs(x2 - x1), Mathf.Abs(y2 - y1));

            Color prev = GUI.color;
            GUI.color = _marqueeFill;
            GUI.DrawTexture(rect, _whiteTex);
            GUI.color = _marqueeBorder;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), _whiteTex);
            GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), _whiteTex);
            GUI.color = prev;
        }
    }
}
