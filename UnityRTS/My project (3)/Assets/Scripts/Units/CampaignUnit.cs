using System;
using System.Collections.Generic;
using PoliticalMayhem.Combat;
using PoliticalMayhem.Core;
using PoliticalMayhem.Rally;
using UnityEngine;

namespace PoliticalMayhem.Units
{
    /// <summary>
    /// Real-time campaign RTS unit (Kampanya Gönüllüsü, Propagandacı, Koruma, Miting Konvoyu).
    /// Handles stats, ideological tech passive modifiers, movement, separation, and combat ticks.
    /// </summary>
    public class CampaignUnit : MonoBehaviour
    {
        public static readonly List<CampaignUnit> ActiveUnits = new List<CampaignUnit>();

        public static event Action<CampaignUnit, CampaignUnit> OnUnitDefeated;
        public static event Action<CampaignUnit, CampaignUnit, float> OnUnitDamaged;

        [Header("Authoritative Identity")]
        [SerializeField] private int _unitId;
        [SerializeField] private string _ownerPlayerId = PlayerIdentity.PlayerAId;
        [SerializeField] private UnitType _unitType = UnitType.CampaignVolunteer;
        [SerializeField] private CityId _currentCity = CityId.Baskent;
        [SerializeField] private string _currentZone = "RallySquare";

        [Header("Combat & RTS Stats")]
        [SerializeField] private float _maxHealth = 80f;
        [SerializeField] private float _currentHealth = 80f;
        [SerializeField] private float _attackDamage = 8f;
        [SerializeField] private float _armor = 1f;
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _attackRange = 2.2f;
        [SerializeField] private float _attackCooldown = 1f;
        [SerializeField] private float _campaignPower = 6f;
        [SerializeField] private float _visionRadius = 15f;

        public static readonly Vector2 PlayfieldXBounds = new Vector2(-71f, 71f);
        public static readonly Vector2 PlayfieldZBounds = new Vector2(-46f, 78f);
        public const float CapitolExclusionRadius = 3.6f;

        [Header("Visuals")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private GameObject _selectionRing;

        private Vector3 _moveTarget;
        private bool _hasMoveOrder;
        private CampaignUnit _explicitTarget;
        private float _cooldownTimer;
        private float _activityTickTimer;
        private bool _isSelected;
        private bool _isVisuallyVisible = true;
        private CityManager _cityManager;

        private Renderer[] _allRenderers;
        private Renderer[] _modelRenderers;
        private Transform _visualPulseRoot;
        private bool _usesImportedModel;
        private Texture2D _modelBaseTexture;
        private Texture2D _modelEmissionTexture;
        private GameObject _healthBarRoot;
        private Transform _healthBarFill;
        private Renderer _healthBarFillRenderer;
        private Vector3 _bodyBaseLocalPos;
        private Quaternion _bodyBaseLocalRot = Quaternion.identity;
        private Vector3 _bodyBaseLocalScale = Vector3.one;
        private bool _bodyBaseCached;
        private float _attackPulseTimer;
        private float _hitRecoilTimer;
        private float _walkCyclePhase;
        private bool _movedThisFrame;
        private const float AttackPulseDuration = 0.22f;
        private const float HitRecoilDuration = 0.16f;

        public int UnitId => _unitId;
        public string OwnerPlayerId => _ownerPlayerId;
        public UnitType UnitType => _unitType;
        public string CanonicalTypeKey => UnitCatalog.ToKey(_unitType);
        public CityId CurrentCity => _currentCity;
        public string CurrentZone => _currentZone;
        public float MaxHealth => _maxHealth;
        public float CurrentHealth => _currentHealth;
        public float AttackDamage => _attackDamage;
        public float Armor => _armor;
        public float MoveSpeed => _moveSpeed;
        public float AttackRange => _attackRange;
        public float VisionRadius => _visionRadius;
        public float CampaignPower => _campaignPower;
        public bool IsAlive => _currentHealth > 0f && gameObject.activeInHierarchy;
        public bool IsSelected => _isSelected;
        public bool IsVisuallyVisible => _isVisuallyVisible;
        public bool HasMoveOrder => _hasMoveOrder;
        public Vector3 MoveTarget => _moveTarget;

        private void OnEnable()
        {
            if (!ActiveUnits.Contains(this))
                ActiveUnits.Add(this);
        }

        private void OnDisable()
        {
            ActiveUnits.Remove(this);
        }

        public void Initialize(
            int unitId,
            string ownerPlayerId,
            UnitType unitType,
            CityId spawnCity,
            string spawnZone,
            OverallManager overallManager,
            CityManager cityManager)
        {
            _unitId = unitId;
            _ownerPlayerId = PlayerIdentity.Normalize(ownerPlayerId);
            _unitType = unitType;
            _currentCity = spawnCity;
            _currentZone = string.IsNullOrWhiteSpace(spawnZone) ? CityCatalog.GetPrimaryZoneName(spawnCity) : spawnZone;
            _cityManager = cityManager;

            UnitStatDefinition baseStats = UnitCatalog.GetBaseStats(unitType);
            _maxHealth = baseStats.maxHealth;
            _attackDamage = baseStats.attackDamage;
            _armor = baseStats.armor;
            _moveSpeed = baseStats.moveSpeed;
            _attackRange = baseStats.attackRange;
            _attackCooldown = baseStats.attackCooldown;
            _campaignPower = baseStats.campaignPower;
            _visionRadius = unitType == UnitType.RallyConvoy ? 22f : 15f;

            // Apply unlocked passive ideological technology bonuses (ai/rag/knowledge/technologies.json)
            if (overallManager != null)
            {
                if (unitType == UnitType.CampaignVolunteer)
                    _maxHealth *= overallManager.GetVolunteerHealthMultiplier(_ownerPlayerId);
                else if (unitType == UnitType.Propagandist)
                    _attackRange *= overallManager.GetPropagandistRangeMultiplier(_ownerPlayerId);
                else if (unitType == UnitType.Bodyguard)
                    _armor *= overallManager.GetBodyguardArmorMultiplier(_ownerPlayerId);
            }

            _currentHealth = _maxHealth;
            transform.position = SanitizePlayfieldPosition(RemapLegacySpawnPosition(transform.position, spawnCity));
            _moveTarget = transform.position;
            _hasMoveOrder = false;
            _explicitTarget = null;
            _cooldownTimer = 0f;
            _attackPulseTimer = 0f;

            ApplyTeamColor();
            SetSelected(false);
            RefreshHealthBarVisual();
        }

        public void ConfigureImportedModelVisuals(Transform visualRoot, Texture2D baseTexture, Texture2D emissionTexture = null)
        {
            _usesImportedModel = visualRoot != null;
            _visualPulseRoot = visualRoot;
            _modelBaseTexture = baseTexture;
            _modelEmissionTexture = emissionTexture;
            _modelRenderers = visualRoot != null ? visualRoot.GetComponentsInChildren<Renderer>(true) : null;
        }

        public void BindVisuals(Renderer bodyRenderer, GameObject selectionRing)
        {
            _bodyRenderer = bodyRenderer;
            _selectionRing = selectionRing;

            if (_visualPulseRoot == null)
                _visualPulseRoot = transform.Find("Body") ?? (_bodyRenderer != null ? _bodyRenderer.transform : null);

            if (_visualPulseRoot != null)
            {
                _bodyBaseLocalPos = _visualPulseRoot.localPosition;
                _bodyBaseLocalRot = _visualPulseRoot.localRotation;
                _bodyBaseLocalScale = _visualPulseRoot.localScale;
                _bodyBaseCached = true;

                if (!_usesImportedModel && _visualPulseRoot.GetComponent<Renderer>() == null && _visualPulseRoot.childCount > 0)
                {
                    _usesImportedModel = true;
                    _modelRenderers = _visualPulseRoot.GetComponentsInChildren<Renderer>(true);
                }
            }

            Transform hbRoot = transform.Find("HealthBarRoot");
            if (hbRoot != null)
            {
                _healthBarRoot = hbRoot.gameObject;
                _healthBarFill = hbRoot.Find("HealthBarFill");
                _healthBarFillRenderer = _healthBarFill != null ? _healthBarFill.GetComponent<Renderer>() : null;
            }

            _allRenderers = GetComponentsInChildren<Renderer>(true);
            ApplyTeamColor();
            SetSelected(_isSelected);
            RefreshHealthBarVisual();
        }

        public void SetVisualVisibility(bool visible)
        {
            if (_allRenderers == null || _allRenderers.Length == 0)
                _allRenderers = GetComponentsInChildren<Renderer>(true);

            if (_isVisuallyVisible != visible)
            {
                _isVisuallyVisible = visible;
                for (int i = 0; i < _allRenderers.Length; i++)
                {
                    if (_allRenderers[i] != null)
                        _allRenderers[i].enabled = visible;
                }
            }

            if (_selectionRing != null)
                _selectionRing.SetActive(visible && _isSelected);
            RefreshHealthBarVisual();
        }

        public void ApplyTeamColor()
        {
            Color teamAccent = _ownerPlayerId == PlayerIdentity.PlayerBId
                ? new Color(0.88f, 0.42f, 0.32f)
                : new Color(0.32f, 0.62f, 0.92f);

            if (_usesImportedModel && _visualPulseRoot != null)
            {
                if (_modelRenderers == null || _modelRenderers.Length == 0)
                    _modelRenderers = _visualPulseRoot.GetComponentsInChildren<Renderer>(true);

                // Very gentle team tint on textured character/vehicle materials so skin & clothing look natural
                Color subtleTint = Color.Lerp(Color.white, teamAccent, 0.12f);
                subtleTint.a = 1f;

                for (int i = 0; i < _modelRenderers.Length; i++)
                {
                    Renderer r = _modelRenderers[i];
                    if (r == null) continue;

                    Texture2D tex = _modelBaseTexture;
                    if (tex == null && r.sharedMaterial != null)
                        tex = r.sharedMaterial.mainTexture as Texture2D;

                    Material teamMat = MaterialHelper.GetOrCreateSharedTexturedMaterial(
                        tex,
                        subtleTint,
                        0.20f,
                        $"{CanonicalTypeKey}_{_ownerPlayerId}_Mat",
                        _modelEmissionTexture);
                    int slotCount = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                    if (slotCount == 1)
                    {
                        r.sharedMaterial = teamMat;
                    }
                    else
                    {
                        Material[] mats = new Material[slotCount];
                        for (int j = 0; j < slotCount; j++) mats[j] = teamMat;
                        r.sharedMaterials = mats;
                    }
                }
            }
            else
            {
                if (_bodyRenderer == null)
                {
                    Transform bodyTf = transform.Find("Body");
                    _bodyRenderer = bodyTf != null ? bodyTf.GetComponent<Renderer>() : GetComponentInChildren<Renderer>();
                }

                // High-contrast neutral grey unit body on near-black map, subtly cool/warm per player
                Color baseGrey = _ownerPlayerId == PlayerIdentity.PlayerBId
                    ? new Color(0.76f, 0.70f, 0.69f)
                    : new Color(0.72f, 0.77f, 0.84f);

                if (_unitType == UnitType.Bodyguard) baseGrey = Color.Lerp(baseGrey, new Color(0.50f, 0.54f, 0.58f), 0.35f);
                else if (_unitType == UnitType.Propagandist) baseGrey = Color.Lerp(baseGrey, new Color(0.86f, 0.88f, 0.91f), 0.25f);
                else if (_unitType == UnitType.RallyConvoy) baseGrey = Color.Lerp(baseGrey, new Color(0.64f, 0.68f, 0.73f), 0.25f);
                baseGrey.a = 1f;

                if (_bodyRenderer != null)
                {
                    MaterialHelper.ApplyColor(_bodyRenderer, baseGrey, 0.18f);
                }
            }

            // Calm, non-clashing team distinction marker (steel-blue Player A vs muted-coral Player B)
            Transform accentTf = transform.Find("TeamAccent");
            if (accentTf != null)
            {
                Renderer accentRend = accentTf.GetComponent<Renderer>();
                if (accentRend != null)
                {
                    MaterialHelper.ApplyColor(accentRend, teamAccent, 0.22f);
                }
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            if (_selectionRing != null)
                _selectionRing.SetActive(_isVisuallyVisible && selected);
            RefreshHealthBarVisual();
        }

        public void IssueMoveOrder(Vector3 destination, CityId targetCity, string targetZone)
        {
            Vector3 clamped = SanitizePlayfieldPosition(new Vector3(destination.x, transform.position.y, destination.z));
            _moveTarget = clamped;
            _hasMoveOrder = true;
            _explicitTarget = null;
            if (!string.IsNullOrWhiteSpace(targetZone))
                _currentZone = targetZone;
        }

        public void IssueAttackOrder(CampaignUnit target)
        {
            if (target == null || target.OwnerPlayerId == _ownerPlayerId) return;
            _explicitTarget = target;
            _hasMoveOrder = false;
        }

        private void Update()
        {
            if (!IsAlive) return;

            _cooldownTimer = Mathf.Max(0f, _cooldownTimer - Time.deltaTime);
            _movedThisFrame = false;

            UpdateLocationAndCampaignPresence();
            TickCombatAndMovement();
            UpdateLocomotionAndCombatVisuals();
        }

        private void LateUpdate()
        {
            if (_healthBarRoot != null && _healthBarRoot.activeSelf && Camera.main != null)
            {
                _healthBarRoot.transform.rotation = Camera.main.transform.rotation;
            }
        }

        private void UpdateLocationAndCampaignPresence()
        {
            if (_cityManager == null)
                _cityManager = FindAnyObjectByType<CityManager>();

            if (_cityManager != null)
            {
                _currentCity = _cityManager.GetClosestCity(transform.position);

                PoliticalMayhemGameManager gm = PoliticalMayhemGameManager.Instance;
                RallyController rc = RallyController.Instance;
                bool timersPaused = rc != null && rc.PauseTimers;
                bool activeRallyActivityPhase = gm != null && !gm.MatchCompleted &&
                    (gm.CurrentPhase == TurnPhase.PREPARATION ||
                     gm.CurrentPhase == TurnPhase.RALLY_SPEECH ||
                     gm.CurrentPhase == TurnPhase.AI_EVALUATION);

                if (timersPaused || !activeRallyActivityPhase)
                {
                    _activityTickTimer = 0f;
                    return;
                }

                _activityTickTimer += Time.deltaTime;
                if (_activityTickTimer >= 1.0f)
                {
                    _activityTickTimer = 0f;
                    CityState city = _cityManager.GetCity(_currentCity);
                    if (city != null && Vector3.Distance(transform.position, _cityManager.GetCityWorldPosition(_currentCity)) <= 16f)
                    {
                        // Active campaign teams gradually build local city campaign activity (15% Election weight)
                        city.AddCampaignActivity(_ownerPlayerId, _campaignPower * 0.12f);
                    }
                }
            }
        }

        private void TickCombatAndMovement()
        {
            // Validate explicit attack target
            if (_explicitTarget != null && !_explicitTarget.IsAlive)
                _explicitTarget = null;

            CampaignUnit combatTarget = _explicitTarget;
            if (combatTarget == null && !_hasMoveOrder)
            {
                // Auto-acquire nearby opposing units within aggro envelope
                combatTarget = CombatResolver.FindBestEnemyInRange(this, ActiveUnits, _attackRange + 3.5f);
            }

            if (combatTarget != null)
            {
                float dist = Vector3.Distance(transform.position, combatTarget.transform.position);
                if (dist <= _attackRange)
                {
                    FaceTowards(combatTarget.transform.position);
                    if (_cooldownTimer <= 0f)
                    {
                        PerformAttack(combatTarget);
                    }
                    return;
                }
                else if (_explicitTarget != null)
                {
                    MoveTowards(combatTarget.transform.position);
                    return;
                }
            }

            if (_hasMoveOrder)
            {
                float dist = Vector3.Distance(
                    new Vector3(transform.position.x, 0f, transform.position.z),
                    new Vector3(_moveTarget.x, 0f, _moveTarget.z));

                if (dist <= 0.35f)
                {
                    _hasMoveOrder = false;
                }
                else
                {
                    MoveTowards(_moveTarget);
                }
            }
        }

        private void MoveTowards(Vector3 worldDestination)
        {
            Vector3 flatDest = SanitizePlayfieldPosition(new Vector3(worldDestination.x, transform.position.y, worldDestination.z));
            Vector3 dir = flatDest - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();

            // Lightweight local separation + capitol obstacle avoidance so units skirt around buildings on the island
            Vector3 separation = ComputeSeparationVector();
            Vector3 capitolAvoidance = ComputeCapitolAvoidanceVector(dir);
            Vector3 combined = dir + separation * 0.55f + capitolAvoidance * 1.35f;
            combined.y = 0f;
            if (combined.sqrMagnitude < 0.001f)
                combined = dir;

            Vector3 velocity = combined.normalized * _moveSpeed;
            Vector3 nextPos = SanitizePlayfieldPosition(transform.position + velocity * Time.deltaTime);
            if ((nextPos - transform.position).sqrMagnitude > 0.000001f)
                _movedThisFrame = true;
            transform.position = nextPos;
            FaceTowards(nextPos + velocity);
        }

        private static Vector3 RemapLegacySpawnPosition(Vector3 pos, CityId spawnCity)
        {
            Vector3 currentCenter = CityCatalog.GetWorldPosition(spawnCity);
            if (Vector3.Distance(new Vector3(pos.x, 0f, pos.z), currentCenter) <= 13.8f)
                return pos;

            Vector3 legacyCenter;
            switch (spawnCity)
            {
                case CityId.Baskent: legacyCenter = new Vector3(0f, 0f, 0f); break;
                case CityId.Limankent: legacyCenter = new Vector3(-28f, 0f, -14f); break;
                case CityId.Demirkent: legacyCenter = new Vector3(28f, 0f, -12f); break;
                case CityId.Yesilova: legacyCenter = new Vector3(-24f, 0f, 22f); break;
                case CityId.Universitekent: legacyCenter = new Vector3(24f, 0f, 22f); break;
                case CityId.Dagsehir: legacyCenter = new Vector3(0f, 0f, 36f); break;
                default: return pos;
            }

            Vector3 offset = pos - legacyCenter;
            if (new Vector2(offset.x, offset.z).magnitude <= 14.5f)
            {
                return new Vector3(currentCenter.x + offset.x, pos.y, currentCenter.z + offset.z);
            }
            return pos;
        }

        public static Vector3 SanitizePlayfieldPosition(Vector3 pos)
        {
            float x = Mathf.Clamp(pos.x, PlayfieldXBounds.x, PlayfieldXBounds.y);
            float z = Mathf.Clamp(pos.z, PlayfieldZBounds.x, PlayfieldZBounds.y);

            for (int i = 0; i < CityCatalog.All.Length; i++)
            {
                Vector3 center = CityCatalog.GetWorldPosition(CityCatalog.All[i]);
                float dx = x - center.x;
                float dz = z - center.z;
                float sqrDist = dx * dx + dz * dz;
                if (sqrDist < CapitolExclusionRadius * CapitolExclusionRadius)
                {
                    if (sqrDist < 0.0001f)
                    {
                        x = center.x;
                        z = center.z - CapitolExclusionRadius;
                    }
                    else
                    {
                        float dist = Mathf.Sqrt(sqrDist);
                        float scale = CapitolExclusionRadius / dist;
                        x = center.x + dx * scale;
                        z = center.z + dz * scale;
                    }
                }
            }

            x = Mathf.Clamp(x, PlayfieldXBounds.x, PlayfieldXBounds.y);
            z = Mathf.Clamp(z, PlayfieldZBounds.x, PlayfieldZBounds.y);
            return new Vector3(x, pos.y, z);
        }

        private Vector3 ComputeCapitolAvoidanceVector(Vector3 desiredDir)
        {
            Vector3 steer = Vector3.zero;
            float softRadius = CapitolExclusionRadius + 1.15f;

            for (int i = 0; i < CityCatalog.All.Length; i++)
            {
                Vector3 center = CityCatalog.GetWorldPosition(CityCatalog.All[i]);
                Vector3 away = transform.position - center;
                away.y = 0f;
                float dist = away.magnitude;
                if (dist < softRadius)
                {
                    Vector3 radial = dist > 0.001f ? away / dist : Vector3.back;
                    Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                    if (Vector3.Dot(tangent, desiredDir) < 0f)
                        tangent = -tangent;
                    float weight = (softRadius - dist) / softRadius;
                    steer += (radial * 0.7f + tangent * 0.9f) * weight;
                }
            }

            return steer;
        }

        private Vector3 ComputeSeparationVector()
        {
            Vector3 push = Vector3.zero;
            int count = 0;
            for (int i = 0; i < ActiveUnits.Count; i++)
            {
                CampaignUnit other = ActiveUnits[i];
                if (other == null || other == this || !other.IsAlive) continue;
                Vector3 diff = transform.position - other.transform.position;
                diff.y = 0f;
                float sqrDist = diff.sqrMagnitude;
                if (sqrDist > 0.0001f && sqrDist < 2.25f)
                {
                    push += diff.normalized / Mathf.Sqrt(sqrDist);
                    count++;
                }
            }
            return count > 0 ? push / count : Vector3.zero;
        }

        private void FaceTowards(Vector3 targetPos)
        {
            Vector3 look = targetPos - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(look),
                    12f * Time.deltaTime);
            }
        }

        private void PerformAttack(CampaignUnit target)
        {
            _cooldownTimer = _attackCooldown;
            _attackPulseTimer = AttackPulseDuration;

            bool isRanged = _unitType == UnitType.Propagandist || _unitType == UnitType.RallyConvoy;
            if (isRanged && target != null && _isVisuallyVisible)
            {
                SpawnRangedCombatPulse(target.transform.position + Vector3.up * 0.75f);
            }

            if (_isVisuallyVisible)
                GameAudioManager.EnsureInstance().PlayCombatHit(isRanged);

            float dmg = CombatResolver.ComputeEffectiveDamage(this, target);
            target.ReceiveDamage(dmg, this);
        }

        private void SpawnRangedCombatPulse(Vector3 targetWorldPos)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "BroadcastPulseOrb";
            Collider col = orb.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Vector3 startPos = transform.position + Vector3.up * (_unitType == UnitType.RallyConvoy ? 1.25f : 0.85f);
            orb.transform.position = startPos;
            orb.transform.localScale = Vector3.one * 0.24f;
            Color teamColor = _ownerPlayerId == PlayerIdentity.PlayerBId
                ? new Color(1.0f, 0.36f, 0.28f)
                : new Color(0.30f, 0.78f, 1.0f);
            MaterialHelper.ApplyColor(orb.GetComponent<Renderer>(), teamColor, 0.45f);

            TransientProjectileEffect fx = orb.AddComponent<TransientProjectileEffect>();
            fx.Launch(startPos, targetWorldPos, 0.22f);
        }

        private void UpdateLocomotionAndCombatVisuals()
        {
            Transform pulseTf = _visualPulseRoot != null
                ? _visualPulseRoot
                : (_bodyRenderer != null ? _bodyRenderer.transform : null);
            if (pulseTf == null || !_bodyBaseCached) return;

            Vector3 posOffset = Vector3.zero;
            Vector3 rotEuler = Vector3.zero;
            Vector3 scaleMul = Vector3.one;

            // 1. Walking stride / vehicle driving animation (or subtle idle breathing)
            if (_movedThisFrame)
            {
                float speedFreq = _unitType == UnitType.RallyConvoy ? 15f : 11.5f;
                _walkCyclePhase += Time.deltaTime * speedFreq;
                if (_unitType == UnitType.RallyConvoy)
                {
                    posOffset.y += Mathf.Abs(Mathf.Sin(_walkCyclePhase)) * 0.035f;
                    rotEuler.x += -2.5f;
                    rotEuler.z += Mathf.Sin(_walkCyclePhase * 0.6f) * 2.2f;
                }
                else
                {
                    posOffset.y += Mathf.Abs(Mathf.Sin(_walkCyclePhase)) * 0.11f;
                    rotEuler.x += 7.5f;
                    rotEuler.z += Mathf.Sin(_walkCyclePhase) * 9.0f;
                    rotEuler.y += Mathf.Cos(_walkCyclePhase) * 5.5f;
                }
            }
            else
            {
                float breath = Mathf.Sin(Time.time * 2.4f + _unitId * 0.9f) * 0.015f;
                scaleMul = new Vector3(1f - breath * 0.5f, 1f + breath, 1f - breath * 0.5f);
            }

            // 2. Attack lunge / broadcast recoil
            if (_attackPulseTimer > 0f)
            {
                _attackPulseTimer = Mathf.Max(0f, _attackPulseTimer - Time.deltaTime);
                float normalized = _attackPulseTimer / AttackPulseDuration;
                float wave = Mathf.Sin(normalized * Mathf.PI);
                posOffset += Vector3.forward * (0.34f * wave);
                rotEuler.x += (_unitType == UnitType.RallyConvoy ? -5f : 16f) * wave;
                scaleMul *= (1f + 0.14f * wave);
            }

            // 3. Damage hit recoil & squash
            if (_hitRecoilTimer > 0f)
            {
                _hitRecoilTimer = Mathf.Max(0f, _hitRecoilTimer - Time.deltaTime);
                float norm = _hitRecoilTimer / HitRecoilDuration;
                float wave = Mathf.Sin(norm * Mathf.PI);
                posOffset += Vector3.back * (0.20f * wave);
                rotEuler.x += -18f * wave;
                scaleMul = new Vector3(scaleMul.x * (1f + 0.16f * wave), scaleMul.y * (1f - 0.12f * wave), scaleMul.z * (1f + 0.16f * wave));
            }

            pulseTf.localPosition = _bodyBaseLocalPos + posOffset;
            pulseTf.localRotation = _bodyBaseLocalRot * Quaternion.Euler(rotEuler);
            pulseTf.localScale = Vector3.Scale(_bodyBaseLocalScale, scaleMul);
        }

        private void RefreshHealthBarVisual()
        {
            if (_healthBarRoot == null) return;

            bool show = _isVisuallyVisible && IsAlive && (_isSelected || _currentHealth < _maxHealth - 0.01f);
            _healthBarRoot.SetActive(show);
            if (!show) return;

            float ratio = _maxHealth > 0f ? Mathf.Clamp01(_currentHealth / _maxHealth) : 0f;
            if (_healthBarFill != null)
            {
                const float fullWidth = 1.08f;
                float fillWidth = Mathf.Max(0.02f, fullWidth * ratio);
                _healthBarFill.localScale = new Vector3(fillWidth, 0.10f, 0.11f);
                _healthBarFill.localPosition = new Vector3(-(fullWidth - fillWidth) * 0.5f, 0f, -0.02f);
            }

            if (_healthBarFillRenderer != null)
            {
                Color barColor = ratio > 0.6f
                    ? new Color(0.22f, 0.78f, 0.42f)
                    : (ratio > 0.3f ? new Color(0.95f, 0.72f, 0.18f) : new Color(0.90f, 0.24f, 0.24f));
                MaterialHelper.ApplyColor(_healthBarFillRenderer, barColor, 0.25f);
            }
        }

        public void ReceiveDamage(float damage, CampaignUnit attacker)
        {
            if (!IsAlive) return;
            _currentHealth = Mathf.Max(0f, _currentHealth - Mathf.Max(0f, damage));
            _hitRecoilTimer = HitRecoilDuration;
            RefreshHealthBarVisual();
            OnUnitDamaged?.Invoke(this, attacker, damage);

            if (_currentHealth <= 0f)
            {
                if (_isVisuallyVisible)
                    GameAudioManager.EnsureInstance().PlayUnitDefeated();
                RefreshHealthBarVisual();
                OnUnitDefeated?.Invoke(this, attacker);
                gameObject.SetActive(false);
                Destroy(gameObject, 0.05f);
            }
        }
    }

    /// <summary>
    /// Lightweight visual projectile orb for ranged Propagandist/Convoy attacks.
    /// </summary>
    public class TransientProjectileEffect : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _end;
        private float _duration = 0.22f;
        private float _elapsed;

        public void Launch(Vector3 startPos, Vector3 endPos, float duration)
        {
            _start = startPos;
            _end = endPos;
            _duration = Mathf.Max(0.08f, duration);
            _elapsed = 0f;
            transform.position = _start;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            Vector3 pos = Vector3.Lerp(_start, _end, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * 0.45f;
            transform.position = pos;
            transform.localScale = Vector3.one * Mathf.Lerp(0.24f, 0.42f, t);
            if (t >= 1f)
                Destroy(gameObject);
        }
    }
}
