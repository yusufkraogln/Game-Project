using System.Collections.Generic;
using PoliticalMayhem.Core;
using UnityEngine;

namespace PoliticalMayhem.Rally
{
    /// <summary>
    /// Builds and animates the 3D political-comedy staging for:
    /// 1) Rally Podium, Speaker & 5-person Security Squad (RALLY_SPEECH / AI_EVALUATION)
    /// 2) Physical Sabotage crowd brawl dust cloud & Security Squad pursuit
    /// 3) Non-graphic comedic 5-Suspect Revolver Duel lineup (REVOLVER_DUEL)
    /// 4) Elevated Victory Balcony on Başkent Capitol (BALCONY_SPEECH)
    /// </summary>
    public class RallyStageVisualController : MonoBehaviour
    {
        public static RallyStageVisualController Instance { get; private set; }

        private const string UnitPropagandistPath = "Models/Units/Characters/unit_propagandist";
        private const string UnitBodyguardPath = "Models/Units/Characters/unit_bodyguard";
        private const string UnitVolunteerPath = "Models/Units/Characters/unit_volunteer";
        private const string UnitsPalettePath = "Models/Units/Characters/units_palette";
        private const string CrowdPalettePath = "Models/Crowd/crowd_palette";

        private static readonly string[] SuspectModelPaths =
        {
            "Models/Units/Characters/unit_propagandist",
            "Models/Crowd/crowd_male_1",
            "Models/Crowd/crowd_female_1",
            "Models/Crowd/crowd_male_2",
            "Models/Crowd/crowd_female_2"
        };

        private static readonly string[] SuspectPalettePaths =
        {
            UnitsPalettePath,
            CrowdPalettePath,
            CrowdPalettePath,
            CrowdPalettePath,
            CrowdPalettePath
        };

        private GameObject _stageRoot;
        private Transform _speakerTransform;
        private Vector3 _speakerBaseLocalPos;
        private readonly List<Transform> _securitySquad = new List<Transform>();
        private readonly List<Vector3> _securityBaseLocalPositions = new List<Vector3>();

        // Physical Sabotage Brawl visuals
        private GameObject _brawlRoot;
        private Transform _saboteurTransform;
        private readonly List<Transform> _brawlPuffs = new List<Transform>();
        private Vector3 _brawlLocalCenter;
        private bool _brawlActive;
        private bool _saboteurArrested;
        private float _brawlTimer;

        // 5-Suspect Revolver Duel visuals
        private GameObject _duelRoot;
        private Transform _duelSpeaker;
        private Transform _revolverPivot;
        private readonly List<Transform> _suspectTransforms = new List<Transform>();
        private readonly List<Vector3> _suspectBaseLocalPos = new List<Vector3>();
        private readonly List<Renderer> _suspectSpotlightRenderers = new List<Renderer>();
        private int _shotSuspectIndex = -1;
        private bool _shotWasHit;
        private float _shotAnimTimer;
        private GameObject _comicBangBanner;

        // Victory Balcony visuals
        private GameObject _balconyRoot;
        private Transform _balconyWinner;
        private Vector3 _balconyWinnerBaseLocalPos;
        private bool _balconyActive;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
        }

        private void OnDestroy()
        {
            ClearAllStageVisuals();
            if (Instance == this)
                Instance = null;
        }

        public void ClearAllStageVisuals()
        {
            ClearRallyStage();
            ClearRevolverDuel();
            ClearBalconyStage();
        }

        /// <summary>
        /// Spawns the 3D rally podium, speaker, banners, loudspeakers, and 5-person Security Squad
        /// in the open southern front plaza of the active rally city.
        /// </summary>
        public void ShowRallyStage(CityId cityId, CityManager cityManager, string speakerPlayerId)
        {
            ClearAllStageVisuals();

            Vector3 cityCenter = cityManager != null
                ? cityManager.GetCityWorldPosition(cityId)
                : CityCatalog.GetWorldPosition(cityId);

            Color teamColor = PlayerIdentity.ToSlot(speakerPlayerId) == PlayerSlot.PlayerB
                ? new Color(0.86f, 0.26f, 0.24f)
                : new Color(0.20f, 0.52f, 0.92f);
            Color goldTrim = new Color(0.95f, 0.78f, 0.24f);
            Color darkStage = new Color(0.22f, 0.24f, 0.29f);
            Color woodPodium = new Color(0.48f, 0.30f, 0.16f);

            _stageRoot = new GameObject("RallyStage3D");
            _stageRoot.transform.position = cityCenter;

            // Main Stage Platform sitting cleanly at Z = -3.95 (south of Capitol, north of Crowd)
            Vector3 stageCenter = new Vector3(0f, 0.08f, -3.95f);
            CreatePart(PrimitiveType.Cube, "StageDeck", _stageRoot.transform,
                stageCenter + new Vector3(0f, 0.16f, 0f), new Vector3(3.6f, 0.32f, 1.5f), darkStage, 0.18f);
            CreatePart(PrimitiveType.Cube, "StageCarpet", _stageRoot.transform,
                stageCenter + new Vector3(0f, 0.33f, 0f), new Vector3(3.4f, 0.03f, 1.32f), teamColor, 0.12f);
            CreatePart(PrimitiveType.Cube, "StageFrontSkirt", _stageRoot.transform,
                stageCenter + new Vector3(0f, 0.16f, -0.76f), new Vector3(3.5f, 0.24f, 0.05f), teamColor, 0.22f);
            CreatePart(PrimitiveType.Cube, "StageGoldTrim", _stageRoot.transform,
                stageCenter + new Vector3(0f, 0.31f, -0.77f), new Vector3(3.62f, 0.05f, 0.06f), goldTrim, 0.45f);

            // Speaker Podium (Kürsü) + Microphone
            Vector3 podiumBase = stageCenter + new Vector3(0f, 0.34f, -0.34f);
            CreatePart(PrimitiveType.Cube, "PodiumBody", _stageRoot.transform,
                podiumBase + new Vector3(0f, 0.38f, 0f), new Vector3(0.56f, 0.76f, 0.38f), woodPodium, 0.25f);
            CreatePart(PrimitiveType.Cube, "PodiumTop", _stageRoot.transform,
                podiumBase + new Vector3(0f, 0.78f, -0.02f), new Vector3(0.68f, 0.07f, 0.48f), woodPodium * 0.85f, 0.30f);
            CreatePart(PrimitiveType.Cube, "PodiumCrest", _stageRoot.transform,
                podiumBase + new Vector3(0f, 0.48f, -0.20f), new Vector3(0.28f, 0.28f, 0.03f), goldTrim, 0.45f);
            CreatePart(PrimitiveType.Cylinder, "MicStem", _stageRoot.transform,
                podiumBase + new Vector3(0.08f, 0.90f, 0.06f), new Vector3(0.025f, 0.12f, 0.025f), new Color(0.15f, 0.15f, 0.18f), 0.5f);
            CreatePart(PrimitiveType.Sphere, "MicHead", _stageRoot.transform,
                podiumBase + new Vector3(0.08f, 1.03f, 0.08f), Vector3.one * 0.07f, new Color(0.22f, 0.24f, 0.28f), 0.6f);

            // Twin Stage Loudspeakers & Party Flag Banners
            float[] sides = { -1f, 1f };
            for (int s = 0; s < sides.Length; s++)
            {
                float sx = sides[s];
                CreatePart(PrimitiveType.Cube, $"SpeakerBox_{s}", _stageRoot.transform,
                    stageCenter + new Vector3(sx * 1.48f, 0.64f, -0.22f), new Vector3(0.36f, 0.60f, 0.34f), new Color(0.14f, 0.16f, 0.20f), 0.25f);
                CreatePart(PrimitiveType.Cylinder, $"BannerPole_{s}", _stageRoot.transform,
                    stageCenter + new Vector3(sx * 1.66f, 1.30f, 0.56f), new Vector3(0.05f, 1.10f, 0.05f), goldTrim, 0.4f);
                CreatePart(PrimitiveType.Cube, $"PartyBanner_{s}", _stageRoot.transform,
                    stageCenter + new Vector3(sx * 1.66f, 1.82f, 0.52f), new Vector3(0.55f, 0.78f, 0.04f), teamColor, 0.18f);
            }

            // 3D Speaker Character standing behind the podium facing south (yaw = 180)
            Vector3 speakerLocal = stageCenter + new Vector3(0f, 0.34f, 0.18f);
            GameObject speakerGo = ModelVisualHelper.InstantiateNormalizedModel(
                UnitPropagandistPath,
                UnitsPalettePath,
                "StageSpeaker",
                _stageRoot.transform,
                speakerLocal,
                180f,
                1.24f,
                normalizeByHeight: true,
                groundLocalY: speakerLocal.y,
                tint: Color.Lerp(Color.white, teamColor, 0.12f),
                smoothness: 0.22f,
                stripColliders: true);
            if (speakerGo != null)
            {
                _speakerTransform = speakerGo.transform;
                _speakerBaseLocalPos = _speakerTransform.localPosition;
            }

            // 5-Person Security Squad (Güvenlik Ekibi) guarding the rally perimeter
            _securitySquad.Clear();
            _securityBaseLocalPositions.Clear();
            Vector3[] guardOffsets =
            {
                stageCenter + new Vector3(-2.35f, 0f, -0.45f),
                stageCenter + new Vector3(-2.05f, 0f, -1.25f),
                stageCenter + new Vector3(0.00f,  0f, -1.28f),
                stageCenter + new Vector3(2.05f,  0f, -1.25f),
                stageCenter + new Vector3(2.35f,  0f, -0.45f)
            };

            for (int i = 0; i < guardOffsets.Length; i++)
            {
                GameObject guard = ModelVisualHelper.InstantiateNormalizedModel(
                    UnitBodyguardPath,
                    UnitsPalettePath,
                    $"SecurityGuard_{i}",
                    _stageRoot.transform,
                    guardOffsets[i],
                    180f,
                    1.24f,
                    normalizeByHeight: true,
                    groundLocalY: 0.08f,
                    tint: Color.white,
                    smoothness: 0.22f,
                    stripColliders: true);
                if (guard != null)
                {
                    // Subtle yellow/amber security badge disc under feet
                    CreatePart(PrimitiveType.Cylinder, "SecurityBadgeRing", guard.transform,
                        new Vector3(0f, 0.018f, 0f), new Vector3(0.68f, 0.015f, 0.68f), new Color(0.95f, 0.72f, 0.16f), 0.3f);
                    _securitySquad.Add(guard.transform);
                    _securityBaseLocalPositions.Add(guard.transform.localPosition);
                }
            }
        }

        /// <summary>
        /// Triggers the 3D comedic crowd brawl & 5-person Security Squad pursuit animation
        /// when the Saboteur initiates Physical Sabotage.
        /// </summary>
        public void TriggerPhysicalSabotageBrawl(bool caughtBySecurity)
        {
            if (_stageRoot == null) return;

            ClearBrawlVisuals();
            _brawlActive = true;
            _saboteurArrested = caughtBySecurity;
            _brawlTimer = 0f;
            _brawlLocalCenter = new Vector3(0.4f, 0.08f, -6.55f);

            _brawlRoot = new GameObject("PhysicalBrawlFX");
            _brawlRoot.transform.SetParent(_stageRoot.transform, false);
            _brawlRoot.transform.localPosition = Vector3.zero;

            // Spawn Saboteur infiltrator inside the crowd
            GameObject saboteur = ModelVisualHelper.InstantiateNormalizedModel(
                UnitVolunteerPath,
                UnitsPalettePath,
                "SaboteurInfiltrator",
                _brawlRoot.transform,
                _brawlLocalCenter,
                0f,
                1.18f,
                normalizeByHeight: true,
                groundLocalY: 0.08f,
                tint: new Color(0.95f, 0.65f, 0.65f),
                smoothness: 0.20f,
                stripColliders: true);
            if (saboteur != null)
            {
                _saboteurTransform = saboteur.transform;
                CreatePart(PrimitiveType.Cylinder, "SaboteurAlertRing", saboteur.transform,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.95f, 0.02f, 0.95f), new Color(0.95f, 0.18f, 0.18f), 0.35f);
            }

            // Cartoonish brawl dust puffs & exclamation marker (non-graphic comedy)
            _brawlPuffs.Clear();
            Color[] puffColors =
            {
                new Color(0.92f, 0.90f, 0.84f),
                new Color(0.98f, 0.80f, 0.25f),
                new Color(0.94f, 0.42f, 0.22f)
            };
            for (int i = 0; i < 6; i++)
            {
                float a = (i / 6f) * Mathf.PI * 2f;
                Vector3 pos = _brawlLocalCenter + new Vector3(Mathf.Cos(a) * 0.65f, 0.55f + (i % 2) * 0.35f, Mathf.Sin(a) * 0.65f);
                GameObject puff = CreatePart(PrimitiveType.Sphere, $"BrawlPuff_{i}", _brawlRoot.transform,
                    pos, Vector3.one * (0.45f + (i % 3) * 0.12f), puffColors[i % puffColors.Length], 0.15f);
                _brawlPuffs.Add(puff.transform);
            }

            // Floating comic "ARBEDE!" / "!" sign above brawl
            GameObject labelGo = new GameObject("BrawlLabel");
            labelGo.transform.SetParent(_brawlRoot.transform, false);
            labelGo.transform.localPosition = _brawlLocalCenter + new Vector3(0f, 2.25f, 0f);
            labelGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = caughtBySecurity ? "KAVGA! GÜVENLİK YAKALADI!" : "KAVGA! SABOTAJCI KAÇTI!";
            tm.characterSize = 0.12f;
            tm.fontSize = 40;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(1.0f, 0.88f, 0.20f);
        }

        /// <summary>
        /// Spawns the 3D comedic 5-Suspect Revolver Duel lineup in front of the active rally city.
        /// Non-graphic political comedy style per OurGame.md Section 6.
        /// </summary>
        public void ShowRevolverDuelLineup(CityId cityId, CityManager cityManager, string speakerPlayerId)
        {
            ClearAllStageVisuals();

            Vector3 cityCenter = cityManager != null
                ? cityManager.GetCityWorldPosition(cityId)
                : CityCatalog.GetWorldPosition(cityId);

            Color teamColor = PlayerIdentity.ToSlot(speakerPlayerId) == PlayerSlot.PlayerB
                ? new Color(0.86f, 0.26f, 0.24f)
                : new Color(0.20f, 0.52f, 0.92f);

            _duelRoot = new GameObject("RevolverDuelLineup3D");
            _duelRoot.transform.position = cityCenter;
            _suspectTransforms.Clear();
            _suspectBaseLocalPos.Clear();
            _suspectSpotlightRenderers.Clear();
            _shotSuspectIndex = -1;
            _shotAnimTimer = 0f;

            // Dramatic lineup backdrop rug
            CreatePart(PrimitiveType.Cube, "LineupMat", _duelRoot.transform,
                new Vector3(0f, 0.10f, -4.35f), new Vector3(7.6f, 0.05f, 1.6f), new Color(0.18f, 0.20f, 0.26f), 0.15f);
            CreatePart(PrimitiveType.Cube, "LineupBackdrop", _duelRoot.transform,
                new Vector3(0f, 1.05f, -3.55f), new Vector3(7.4f, 1.95f, 0.12f), new Color(0.88f, 0.90f, 0.94f), 0.10f);

            // Height chart stripes on the comedy lineup wall
            for (int h = 0; h < 3; h++)
            {
                CreatePart(PrimitiveType.Cube, $"HeightStripe_{h}", _duelRoot.transform,
                    new Vector3(0f, 0.55f + h * 0.45f, -3.62f), new Vector3(7.2f, 0.03f, 0.02f), new Color(0.32f, 0.36f, 0.44f), 0.1f);
            }

            // Spawn 5 distinct suspects standing in a neat row at Z = -4.35 facing south (yaw = 180)
            Color[] suspectAccents =
            {
                new Color(0.25f, 0.55f, 0.88f), // #1 Sahte Muhabir
                new Color(0.95f, 0.65f, 0.18f), // #2 Megafonlu Anketör
                new Color(0.82f, 0.42f, 0.24f), // #3 Çaycı
                new Color(0.25f, 0.78f, 0.42f), // #4 Sahte Taraftar
                new Color(0.62f, 0.30f, 0.78f)  // #5 Pelerinli Broşürcü
            };

            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 1.45f;
                Vector3 localPos = new Vector3(x, 0.12f, -4.35f);

                GameObject spot = CreatePart(PrimitiveType.Cylinder, $"SuspectSpot_{i}", _duelRoot.transform,
                    new Vector3(x, 0.13f, -4.35f), new Vector3(0.92f, 0.02f, 0.92f), suspectAccents[i], 0.30f);
                _suspectSpotlightRenderers.Add(spot.GetComponent<Renderer>());

                GameObject suspect = ModelVisualHelper.InstantiateNormalizedModel(
                    SuspectModelPaths[i],
                    SuspectPalettePaths[i],
                    $"Suspect_{i + 1}",
                    _duelRoot.transform,
                    localPos,
                    180f,
                    1.16f,
                    normalizeByHeight: true,
                    groundLocalY: 0.12f,
                    tint: Color.white,
                    smoothness: 0.20f,
                    stripColliders: true);

                if (suspect != null)
                {
                    AttachSuspectComedyProp(suspect.transform, i, suspectAccents[i]);
                    _suspectTransforms.Add(suspect.transform);
                    _suspectBaseLocalPos.Add(suspect.transform.localPosition);
                }

                // Crisp dark-navy #1..#5 number contrasting cleanly against the white lineup wall
                GameObject numGo = new GameObject($"SuspectLabel_{i + 1}");
                numGo.transform.SetParent(_duelRoot.transform, false);
                numGo.transform.localPosition = new Vector3(x, 1.82f, -3.66f);
                numGo.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
                TextMesh tm = numGo.AddComponent<TextMesh>();
                tm.text = $"#{i + 1}";
                tm.characterSize = 0.11f;
                tm.fontSize = 48;
                tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(0.10f, 0.14f, 0.24f);
            }

            // Speaker standing opposite the 5 suspects at Z = -7.2 facing north (yaw = 0)
            GameObject speaker = ModelVisualHelper.InstantiateNormalizedModel(
                UnitPropagandistPath,
                UnitsPalettePath,
                "DuelSpeaker",
                _duelRoot.transform,
                new Vector3(0f, 0.08f, -7.20f),
                0f,
                1.24f,
                normalizeByHeight: true,
                groundLocalY: 0.08f,
                tint: Color.Lerp(Color.white, teamColor, 0.15f),
                smoothness: 0.22f,
                stripColliders: true);

            if (speaker != null)
            {
                _duelSpeaker = speaker.transform;
                CreatePart(PrimitiveType.Cylinder, "DuelSpeakerRing", speaker.transform,
                    new Vector3(0f, 0.02f, 0f), new Vector3(0.95f, 0.02f, 0.95f), teamColor, 0.35f);

                // Stylized cartoonish non-graphic Revolver prop held out front
                GameObject revRoot = new GameObject("ComicRevolverPivot");
                revRoot.transform.SetParent(speaker.transform, false);
                revRoot.transform.localPosition = new Vector3(0.24f, 0.88f, 0.28f);
                _revolverPivot = revRoot.transform;

                Color silver = new Color(0.80f, 0.84f, 0.90f);
                Color handleWood = new Color(0.45f, 0.26f, 0.12f);
                CreatePart(PrimitiveType.Cube, "Barrel", revRoot.transform, new Vector3(0f, 0.05f, 0.24f), new Vector3(0.06f, 0.07f, 0.36f), silver, 0.6f);
                CreatePart(PrimitiveType.Cylinder, "Cylinder", revRoot.transform, new Vector3(0f, 0.04f, 0.06f), new Vector3(0.09f, 0.06f, 0.09f), new Color(0.55f, 0.60f, 0.66f), 0.5f);
                CreatePart(PrimitiveType.Cube, "Grip", revRoot.transform, new Vector3(0f, -0.06f, -0.03f), new Vector3(0.06f, 0.16f, 0.09f), handleWood, 0.25f);
            }
        }

        /// <summary>
        /// Plays the non-graphic political-comedy revolver reveal ("BANG!" flag + spotlight outcome).
        /// </summary>
        public void PlayRevolverOutcome(int suspectIndex, bool hitGuilty)
        {
            if (_duelRoot == null || suspectIndex < 0 || suspectIndex >= _suspectTransforms.Count)
                return;

            _shotSuspectIndex = suspectIndex;
            _shotWasHit = hitGuilty;
            _shotAnimTimer = 0f;

            // Turn speaker toward chosen suspect
            if (_duelSpeaker != null && _suspectTransforms[suspectIndex] != null)
            {
                Vector3 dir = _suspectTransforms[suspectIndex].localPosition - _duelSpeaker.localPosition;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    _duelSpeaker.localRotation = Quaternion.LookRotation(dir);
            }

            // Highlight chosen suspect spotlight (green = caught, red = wrong innocent!)
            if (suspectIndex < _suspectSpotlightRenderers.Count && _suspectSpotlightRenderers[suspectIndex] != null)
            {
                MaterialHelper.ApplyColor(
                    _suspectSpotlightRenderers[suspectIndex],
                    hitGuilty ? new Color(0.20f, 0.92f, 0.38f) : new Color(0.92f, 0.20f, 0.20f),
                    0.4f);
            }

            // Cartoonish "PAT! / TAM İSABET!" or "EYVAH! MASUM!" comic pop-up flag (zero graphic violence)
            if (_comicBangBanner != null)
                Destroy(_comicBangBanner);

            _comicBangBanner = new GameObject("ComicOutcomeBanner");
            _comicBangBanner.transform.SetParent(_duelRoot.transform, false);
            _comicBangBanner.transform.localPosition = new Vector3(0f, 2.55f, -4.1f);
            _comicBangBanner.transform.rotation = Quaternion.Euler(28f, 0f, 0f);

            TextMesh tm = _comicBangBanner.AddComponent<TextMesh>();
            tm.text = hitGuilty ? "TAM İSABET! SABOTAJCI YAKALANDI!" : "YANLIŞ ŞÜPHELİ! (MASUM ÇIKTI)";
            tm.characterSize = 0.085f;
            tm.fontSize = 44;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = hitGuilty ? new Color(0.15f, 0.95f, 0.35f) : new Color(0.98f, 0.28f, 0.24f);

            // White cartoon smoke puff out of the revolver barrel
            if (_revolverPivot != null)
            {
                CreatePart(PrimitiveType.Sphere, "ComicSmokePuff", _revolverPivot,
                    new Vector3(0f, 0.06f, 0.52f), Vector3.one * 0.24f, new Color(0.96f, 0.96f, 0.98f), 0.1f);
            }
        }

        /// <summary>
        /// Spawns the elevated 3D ceremonial Victory Balcony attached to Başkent's Capitol Skyscraper
        /// with the winning leader waving above the cheering crowd.
        /// </summary>
        public void ShowBalconyVictoryStage(CityManager cityManager, string winnerPlayerId)
        {
            ClearAllStageVisuals();

            Vector3 baskentCenter = cityManager != null
                ? cityManager.GetCityWorldPosition(CityId.Baskent)
                : CityCatalog.GetWorldPosition(CityId.Baskent);

            Color teamColor = PlayerIdentity.ToSlot(winnerPlayerId) == PlayerSlot.PlayerB
                ? new Color(0.88f, 0.26f, 0.24f)
                : new Color(0.20f, 0.54f, 0.96f);
            Color gold = new Color(0.98f, 0.80f, 0.24f);
            Color marble = new Color(0.92f, 0.93f, 0.95f);

            _balconyRoot = new GameObject("VictoryBalcony3D");
            _balconyRoot.transform.position = baskentCenter;
            _balconyActive = true;

            // Elevated marble balcony protruding from Başkent Capitol's south facade (y = 2.85, z = -2.25)
            Vector3 balcCenter = new Vector3(0f, 2.85f, -2.35f);
            CreatePart(PrimitiveType.Cube, "BalconyFloor", _balconyRoot.transform,
                balcCenter, new Vector3(4.2f, 0.28f, 1.75f), marble, 0.25f);
            CreatePart(PrimitiveType.Cube, "BalconySupportL", _balconyRoot.transform,
                balcCenter + new Vector3(-1.65f, -1.42f, 0.35f), new Vector3(0.32f, 2.75f, 0.32f), marble * 0.9f, 0.2f);
            CreatePart(PrimitiveType.Cube, "BalconySupportR", _balconyRoot.transform,
                balcCenter + new Vector3(1.65f, -1.42f, 0.35f), new Vector3(0.32f, 2.75f, 0.32f), marble * 0.9f, 0.2f);

            // Front & side marble balustrade railings + golden rail cap
            CreatePart(PrimitiveType.Cube, "RailingFront", _balconyRoot.transform,
                balcCenter + new Vector3(0f, 0.44f, -0.82f), new Vector3(4.2f, 0.62f, 0.12f), marble, 0.22f);
            CreatePart(PrimitiveType.Cube, "RailingGoldCap", _balconyRoot.transform,
                balcCenter + new Vector3(0f, 0.78f, -0.82f), new Vector3(4.3f, 0.08f, 0.18f), gold, 0.50f);
            CreatePart(PrimitiveType.Cube, "RailingLeft", _balconyRoot.transform,
                balcCenter + new Vector3(-2.04f, 0.44f, 0f), new Vector3(0.12f, 0.62f, 1.75f), marble, 0.22f);
            CreatePart(PrimitiveType.Cube, "RailingRight", _balconyRoot.transform,
                balcCenter + new Vector3(2.04f, 0.44f, 0f), new Vector3(0.12f, 0.62f, 1.75f), marble, 0.22f);

            // Giant draping team-colored victory banner + twin vertical side banners
            CreatePart(PrimitiveType.Cube, "VictoryMainBanner", _balconyRoot.transform,
                balcCenter + new Vector3(0f, -0.58f, -0.90f), new Vector3(3.4f, 1.55f, 0.06f), teamColor, 0.18f);
            CreatePart(PrimitiveType.Cube, "VictoryGoldCrest", _balconyRoot.transform,
                balcCenter + new Vector3(0f, -0.45f, -0.94f), new Vector3(0.75f, 0.75f, 0.06f), gold, 0.55f);
            CreatePart(PrimitiveType.Cube, "VictoryPodium", _balconyRoot.transform,
                balcCenter + new Vector3(0f, 0.48f, -0.52f), new Vector3(0.60f, 0.72f, 0.36f), gold * 0.85f, 0.35f);

            // Winning Leader standing triumphantly on the balcony facing south (yaw = 180)
            Vector3 winnerLocal = balcCenter + new Vector3(0f, 0.14f, -0.05f);
            GameObject winnerGo = ModelVisualHelper.InstantiateNormalizedModel(
                UnitPropagandistPath,
                UnitsPalettePath,
                "BalconyWinnerLeader",
                _balconyRoot.transform,
                winnerLocal,
                180f,
                1.30f,
                normalizeByHeight: true,
                groundLocalY: winnerLocal.y,
                tint: Color.Lerp(Color.white, teamColor, 0.12f),
                smoothness: 0.22f,
                stripColliders: true);
            if (winnerGo != null)
            {
                _balconyWinner = winnerGo.transform;
                _balconyWinnerBaseLocalPos = _balconyWinner.localPosition;
            }

            // Victory banner text floating above balcony
            GameObject titleGo = new GameObject("BalconyVictoryTitle");
            titleGo.transform.SetParent(_balconyRoot.transform, false);
            titleGo.transform.localPosition = balcCenter + new Vector3(0f, 2.25f, -0.5f);
            titleGo.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
            TextMesh tm = titleGo.AddComponent<TextMesh>();
            tm.text = $"ZAFER! {PlayerIdentity.GetDisplayName(winnerPlayerId).ToUpperInvariant()}";
            tm.characterSize = 0.18f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = gold;
        }

        private void Update()
        {
            float t = Time.time;

            // 1. Animate Rally Speaker gently gesturing at podium
            if (_speakerTransform != null)
            {
                float nod = Mathf.Sin(t * 4.2f) * 0.04f;
                float sway = Mathf.Sin(t * 2.1f) * 8f;
                _speakerTransform.localPosition = _speakerBaseLocalPos + new Vector3(0f, Mathf.Abs(nod), 0f);
                _speakerTransform.localRotation = Quaternion.Euler(0f, 180f + sway, 0f);
            }

            // 2. Animate Physical Sabotage Brawl & 5-person Security Squad pursuit
            if (_brawlActive && _stageRoot != null)
            {
                _brawlTimer += Time.deltaTime;
                for (int i = 0; i < _brawlPuffs.Count; i++)
                {
                    Transform p = _brawlPuffs[i];
                    if (p == null) continue;
                    float a = t * 4.5f + (i / (float)_brawlPuffs.Count) * Mathf.PI * 2f;
                    float rad = 0.55f + Mathf.Sin(t * 7f + i) * 0.25f;
                    p.localPosition = _brawlLocalCenter + new Vector3(Mathf.Cos(a) * rad, 0.55f + Mathf.Abs(Mathf.Sin(t * 6f + i)) * 0.45f, Mathf.Sin(a) * rad);
                }

                if (_saboteurTransform != null)
                {
                    if (_saboteurArrested)
                    {
                        float shake = Mathf.Sin(t * 22f) * 0.10f;
                        _saboteurTransform.localPosition = _brawlLocalCenter + new Vector3(shake, 0f, 0f);
                    }
                    else
                    {
                        // Escaping saboteur zig-zags south-east
                        float escapeDist = Mathf.Min(3.8f, _brawlTimer * 1.4f);
                        _saboteurTransform.localPosition = _brawlLocalCenter + new Vector3(escapeDist * 0.7f, 0f, -escapeDist * 0.45f);
                    }
                }

                // 5-person Security Squad sprints in from the stage to surround the brawl!
                float converge = Mathf.Clamp01(_brawlTimer / 1.4f);
                for (int i = 0; i < _securitySquad.Count; i++)
                {
                    Transform g = _securitySquad[i];
                    if (g == null) continue;
                    float ringAngle = (i / (float)_securitySquad.Count) * Mathf.PI * 2f;
                    Vector3 targetPos = (_saboteurTransform != null ? _saboteurTransform.localPosition : _brawlLocalCenter)
                        + new Vector3(Mathf.Cos(ringAngle) * 1.15f, 0f, Mathf.Sin(ringAngle) * 1.15f);
                    targetPos.y = 0.08f;
                    g.localPosition = Vector3.Lerp(_securityBaseLocalPositions[i], targetPos, converge);
                    Vector3 look = _brawlLocalCenter - g.localPosition;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.01f)
                        g.localRotation = Quaternion.LookRotation(look);
                }
            }

            // 3. Animate Revolver Duel suspects nervously fidgeting / reacting to shot
            if (_duelRoot != null)
            {
                if (_shotSuspectIndex >= 0)
                    _shotAnimTimer += Time.deltaTime;

                for (int i = 0; i < _suspectTransforms.Count; i++)
                {
                    Transform s = _suspectTransforms[i];
                    if (s == null) continue;

                    if (i == _shotSuspectIndex)
                    {
                        if (_shotWasHit)
                        {
                            // Comedic dizzy spin + tiny hop
                            float hop = Mathf.Abs(Mathf.Sin(_shotAnimTimer * 9f)) * 0.25f;
                            s.localPosition = _suspectBaseLocalPos[i] + new Vector3(0f, hop, 0f);
                            s.localRotation = Quaternion.Euler(0f, 180f + _shotAnimTimer * 360f, 0f);
                        }
                        else
                        {
                            // Innocent suspect jumps indignantly in surprise
                            float hop = Mathf.Abs(Mathf.Sin(_shotAnimTimer * 12f)) * 0.32f;
                            s.localPosition = _suspectBaseLocalPos[i] + new Vector3(0f, hop, 0f);
                        }
                    }
                    else
                    {
                        float nervous = Mathf.Sin(t * 3.5f + i * 1.3f) * 6f;
                        s.localRotation = Quaternion.Euler(0f, 180f + nervous, 0f);
                    }
                }
            }

            // 4. Animate Victory Balcony leader celebrating triumphantly
            if (_balconyActive && _balconyWinner != null)
            {
                float cheerBounce = Mathf.Abs(Mathf.Sin(t * 5.2f)) * 0.16f;
                float waveYaw = 180f + Mathf.Sin(t * 2.5f) * 22f;
                _balconyWinner.localPosition = _balconyWinnerBaseLocalPos + new Vector3(0f, cheerBounce, 0f);
                _balconyWinner.localRotation = Quaternion.Euler(0f, waveYaw, 0f);
            }
        }

        private void AttachSuspectComedyProp(Transform suspectRoot, int index, Color accentColor)
        {
            switch (index)
            {
                case 0: // #1 Bıyıklı Sahte Muhabir — press notepad & camera
                    CreatePart(PrimitiveType.Cube, "PressCamera", suspectRoot, new Vector3(0.24f, 0.85f, 0.20f), new Vector3(0.18f, 0.14f, 0.18f), new Color(0.14f, 0.16f, 0.20f), 0.4f);
                    CreatePart(PrimitiveType.Cube, "PressHat", suspectRoot, new Vector3(0f, 1.18f, 0f), new Vector3(0.32f, 0.08f, 0.34f), accentColor, 0.2f);
                    break;
                case 1: // #2 Megafonlu Gizli Anketör — orange megaphone cone
                    CreatePart(PrimitiveType.Cylinder, "Megaphone", suspectRoot, new Vector3(0.26f, 0.92f, 0.22f), new Vector3(0.16f, 0.18f, 0.16f), accentColor, 0.35f)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                case 2: // #3 Kılık Değiştirmiş Çaycı — tea tray
                    CreatePart(PrimitiveType.Cylinder, "TeaTray", suspectRoot, new Vector3(0.26f, 0.72f, 0.26f), new Vector3(0.36f, 0.03f, 0.36f), new Color(0.78f, 0.80f, 0.84f), 0.6f);
                    CreatePart(PrimitiveType.Cylinder, "TeaGlass", suspectRoot, new Vector3(0.26f, 0.80f, 0.26f), new Vector3(0.08f, 0.08f, 0.08f), new Color(0.76f, 0.24f, 0.10f), 0.5f);
                    break;
                case 3: // #4 Gürültücü Sahte Taraftar — oversized party pennant
                    CreatePart(PrimitiveType.Cube, "FanPole", suspectRoot, new Vector3(0.28f, 0.95f, 0.12f), new Vector3(0.03f, 0.90f, 0.03f), new Color(0.35f, 0.24f, 0.14f), 0.15f);
                    CreatePart(PrimitiveType.Cube, "FanBanner", suspectRoot, new Vector3(0.28f, 1.32f, 0.12f), new Vector3(0.36f, 0.24f, 0.04f), accentColor, 0.2f);
                    break;
                case 4: // #5 Pelerinli Broşürcü — dramatic purple cape
                    CreatePart(PrimitiveType.Cube, "DramaCape", suspectRoot, new Vector3(0f, 0.65f, -0.20f), new Vector3(0.46f, 0.76f, 0.06f), accentColor, 0.18f);
                    break;
            }
        }

        private void ClearRallyStage()
        {
            ClearBrawlVisuals();
            _securitySquad.Clear();
            _securityBaseLocalPositions.Clear();
            _speakerTransform = null;
            if (_stageRoot != null)
            {
                Destroy(_stageRoot);
                _stageRoot = null;
            }
        }

        private void ClearBrawlVisuals()
        {
            _brawlActive = false;
            _saboteurTransform = null;
            _brawlPuffs.Clear();
            if (_brawlRoot != null)
            {
                Destroy(_brawlRoot);
                _brawlRoot = null;
            }
        }

        private void ClearRevolverDuel()
        {
            _suspectTransforms.Clear();
            _suspectBaseLocalPos.Clear();
            _suspectSpotlightRenderers.Clear();
            _duelSpeaker = null;
            _revolverPivot = null;
            _comicBangBanner = null;
            _shotSuspectIndex = -1;
            if (_duelRoot != null)
            {
                Destroy(_duelRoot);
                _duelRoot = null;
            }
        }

        private void ClearBalconyStage()
        {
            _balconyActive = false;
            _balconyWinner = null;
            if (_balconyRoot != null)
            {
                Destroy(_balconyRoot);
                _balconyRoot = null;
            }
        }

        private static GameObject CreatePart(
            PrimitiveType primitive,
            string partName,
            Transform parent,
            Vector3 localPos,
            Vector3 localScale,
            Color color,
            float smoothness)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = partName;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            MaterialHelper.ApplyColor(go.GetComponent<Renderer>(), color, smoothness);
            return go;
        }
    }
}
