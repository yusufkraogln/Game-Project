#if UNITY_EDITOR
using System.IO;
using PoliticalMayhem.AI;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using PoliticalMayhem.Networking;
using PoliticalMayhem.Rally;
using PoliticalMayhem.UI;
using PoliticalMayhem.Units;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoliticalMayhem.Editor
{
    /// <summary>
    /// 1-click Editor Scene Generator creating Assets/Scenes/MainMapScene.unity
    /// with a 150x130 styled island map, widened highways with streetlamps/signs, 6 distinct architectural
    /// landmark cities with 3D central Capitol buildings, forest/rock scenery clusters, persisted URP/Built-in
    /// safe materials, RTS Camera, and wired managers.
    /// </summary>
    [InitializeOnLoad]
    public static class PoliticalMayhemSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MaterialsFolder = "Assets/Materials";
        private const string ScenePath = "Assets/Scenes/MainMapScene.unity";
        private static bool _isAutoUpgrading;

        static PoliticalMayhemSceneBuilder()
        {
            EditorApplication.delayCall -= AutoUpgradeOpenSceneIfNeeded;
            EditorApplication.delayCall += AutoUpgradeOpenSceneIfNeeded;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                AutoUpgradeOpenSceneIfNeeded();
            }
        }

        private static void AutoUpgradeOpenSceneIfNeeded()
        {
            if (_isAutoUpgrading || Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            try
            {
                _isAutoUpgrading = true;
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                    return;

                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    RepairOpenSceneMaterials();
                    return;
                }

                if (activeScene.name != "MainMapScene")
                    return;

                GameObject envRoot = GameObject.Find("EnvironmentRoot");
                GameObject mapGroundGo = GameObject.Find("MapGround");
                Renderer mapRend = mapGroundGo != null ? mapGroundGo.GetComponent<Renderer>() : null;
                bool invalidGroundMat = mapRend == null || !MaterialHelper.IsValidForActivePipeline(mapRend.sharedMaterial);
                bool hasFbxAssets = Resources.Load<GameObject>("Models/Cities/Commercial/building-skyscraper-a") != null;
                bool needsCityModelUpgrade = hasFbxAssets &&
                    (GameObject.Find("CityHub_Baskent/CityModel_0") == null ||
                     GameObject.Find("CityHub_Baskent/CityCapitolLandmark") == null);
                GameObject baskentPlaza = GameObject.Find("CityHub_Baskent/CityPlaza");
                bool needsProportionUpgrade = baskentPlaza == null || baskentPlaza.transform.localScale.x < 19f;
                bool needsMapExpansionUpgrade = mapGroundGo == null ||
                    mapGroundGo.transform.localScale.x < 140f ||
                    GameObject.Find("EnvironmentRoot/SceneryRoot") == null ||
                    needsProportionUpgrade;

                if (envRoot == null || invalidGroundMat || needsCityModelUpgrade || needsMapExpansionUpgrade)
                {
                    GenerateCompletePrototypeScene();
                }
            }
            finally
            {
                _isAutoUpgrading = false;
            }
        }

        private static void RepairOpenSceneMaterials()
        {
            PoliticalMayhemGameManager.RepairSceneEnvironmentVisuals();
        }

        [MenuItem("Political Mayhem/Generate Complete Prototype Scene")]
        public static void GenerateCompletePrototypeScene()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Daylight Lighting & Atmosphere
            GameObject lightGo = new GameObject("Directional Light");
            Light dirLight = lightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = 1.35f;
            dirLight.color = new Color(1.0f, 0.96f, 0.88f);
            dirLight.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, -34f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.76f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.54f, 0.60f, 0.56f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.34f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.60f, 0.77f, 0.91f);
            RenderSettings.fogStartDistance = 135f;
            RenderSettings.fogEndDistance = 320f;

            // 2. Environment Root & Runtime Material Safeguard
            GameObject envRoot = new GameObject("EnvironmentRoot");
            MapEnvironmentVisuals envVisuals = envRoot.AddComponent<MapEnvironmentVisuals>();
            envVisuals.ClearBindings();

            // Rich Coastal Ocean Backdrop (420x380)
            GameObject ocean = CreateBoardBlock(
                "OceanWater",
                envRoot.transform,
                new Vector3(0f, -0.55f, 16f),
                new Vector3(420f, 0.3f, 380f),
                new Color(0.11f, 0.36f, 0.54f),
                0.55f,
                envVisuals,
                withCollider: false);

            // Lush Country-Map Island Ground (150x130 centered at Z=16)
            GameObject ground = CreateBoardBlock(
                "MapGround",
                envRoot.transform,
                new Vector3(0f, -0.25f, 16f),
                new Vector3(150f, 0.5f, 130f),
                new Color(0.31f, 0.46f, 0.28f),
                0.06f,
                envVisuals,
                withCollider: true);
            _ = ocean;
            _ = ground;

            // Warm Sandy-Stone Coastline Border Trims matching the 150x130 landmass
            Color coastColor = new Color(0.76f, 0.70f, 0.54f);
            CreateBoardBlock("CoastNorth", envRoot.transform, new Vector3(0f, -0.18f, 81.8f), new Vector3(153.2f, 0.52f, 1.8f), coastColor, 0.12f, envVisuals, false);
            CreateBoardBlock("CoastSouth", envRoot.transform, new Vector3(0f, -0.18f, -49.8f), new Vector3(153.2f, 0.52f, 1.8f), coastColor, 0.12f, envVisuals, false);
            CreateBoardBlock("CoastWest", envRoot.transform, new Vector3(-75.8f, -0.18f, 16f), new Vector3(1.8f, 0.52f, 133.2f), coastColor, 0.12f, envVisuals, false);
            CreateBoardBlock("CoastEast", envRoot.transform, new Vector3(75.8f, -0.18f, 16f), new Vector3(1.8f, 0.52f, 133.2f), coastColor, 0.12f, envVisuals, false);

            // 3. Widened Asphalt Highways Connecting the 6 Cities (with Curbs, Yellow Medians, Tall Lamps & Overhead Signs)
            GameObject roadsRoot = new GameObject("RoadsRoot");
            roadsRoot.transform.SetParent(envRoot.transform, false);
            Color roadColor = new Color(0.24f, 0.26f, 0.30f);
            Color curbColor = new Color(0.54f, 0.57f, 0.60f);
            Color medianColor = new Color(0.90f, 0.80f, 0.32f);

            for (int i = 0; i < CityVisualBuilder.HighwayRoutes.Length; i++)
            {
                var route = CityVisualBuilder.HighwayRoutes[i];
                CreateRoadStrip(roadsRoot.transform, route.from, route.to, roadColor, curbColor, medianColor, envVisuals);
            }

            // 4. Island Terrain Scenery (Forest Clusters, Natural Rocks, Rural Outposts & Limankent Harbor Boats)
            CityVisualBuilder.PopulateIslandScenery(
                envRoot.transform,
                envVisuals,
                (rend, tex, matKey) => AssignPersistedTexturedMaterial(rend, matKey, tex, Color.white, 0.18f),
                (rend, matKey, col, smooth) => AssignPersistedMaterial(rend, matKey, col, smooth));

            // 5. RTS Main Camera (framed cleanly so all 6 cities including Limankent & Dağşehir fit on screen)
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.54f, 0.73f, 0.90f);
            cam.farClipPlane = 450f;
            camGo.transform.position = new Vector3(0f, 76f, -54f);
            camGo.transform.rotation = Quaternion.Euler(53f, 0f, 0f);
            RtsCameraController camCtrl = camGo.AddComponent<RtsCameraController>();

            // 6. Six Architectural Landmark City Hubs
            GameObject citiesRoot = new GameObject("CitiesRoot");
            foreach (CityId cityId in CityCatalog.All)
            {
                CreateCityHub(cityId, citiesRoot.transform, envVisuals);
            }

            // 7. Units Root
            GameObject unitsRoot = new GameObject("UnitsRoot");

            // 8. Core, Rally, Election, Networking, AI, and HUD Managers
            GameObject managersGo = new GameObject("GameManagers");
            PoliticalMayhemGameManager gameManager = managersGo.AddComponent<PoliticalMayhemGameManager>();
            CityManager cityManager = managersGo.AddComponent<CityManager>();
            OverallManager overallManager = managersGo.AddComponent<OverallManager>();
            PartyBudgetManager budgetManager = managersGo.AddComponent<PartyBudgetManager>();

            UnitController unitController = managersGo.AddComponent<UnitController>();
            CrowdReactionController crowdReaction = managersGo.AddComponent<CrowdReactionController>();
            RallyStageVisualController stageVisuals = managersGo.AddComponent<RallyStageVisualController>();
            SabotageAndRevolverController sabotageController = managersGo.AddComponent<SabotageAndRevolverController>();
            RallyController rallyController = managersGo.AddComponent<RallyController>();
            ElectionNightController electionController = managersGo.AddComponent<ElectionNightController>();
            PhotonFusionAuthority authority = managersGo.AddComponent<PhotonFusionAuthority>();
            AiGatewayClient gatewayClient = managersGo.AddComponent<AiGatewayClient>();
            AiCommanderPanel commanderPanel = managersGo.AddComponent<AiCommanderPanel>();
            PoliticalMayhemHUD hud = managersGo.AddComponent<PoliticalMayhemHUD>();

            WireSerializedField(gameManager, "_cityManager", cityManager);
            WireSerializedField(gameManager, "_overallManager", overallManager);
            WireSerializedField(gameManager, "_partyBudgetManager", budgetManager);

            WireSerializedField(unitController, "_gameManager", gameManager);
            WireSerializedField(unitController, "_rtsCamera", cam);
            WireSerializedField(unitController, "_unitsRoot", unitsRoot.transform);

            WireSerializedField(sabotageController, "_gameManager", gameManager);
            WireSerializedField(sabotageController, "_crowdReaction", crowdReaction);

            WireSerializedField(rallyController, "_gameManager", gameManager);
            WireSerializedField(rallyController, "_sabotageController", sabotageController);
            WireSerializedField(rallyController, "_crowdReaction", crowdReaction);
            WireSerializedField(rallyController, "_stageVisuals", stageVisuals);
            WireSerializedField(rallyController, "_electionController", electionController);
            WireSerializedField(rallyController, "_gatewayClient", gatewayClient);

            WireSerializedField(electionController, "_gameManager", gameManager);
            WireSerializedField(electionController, "_gatewayClient", gatewayClient);
            WireSerializedField(electionController, "_crowdReaction", crowdReaction);

            WireSerializedField(authority, "_gameManager", gameManager);
            WireSerializedField(authority, "_unitController", unitController);
            WireSerializedField(authority, "_rallyController", rallyController);
            WireSerializedField(authority, "_electionController", electionController);

            WireSerializedField(commanderPanel, "_gameManager", gameManager);
            WireSerializedField(commanderPanel, "_gatewayClient", gatewayClient);
            WireSerializedField(commanderPanel, "_authority", authority);

            WireSerializedField(hud, "_gameManager", gameManager);
            WireSerializedField(hud, "_unitController", unitController);
            WireSerializedField(hud, "_rallyController", rallyController);
            WireSerializedField(hud, "_sabotageController", sabotageController);
            WireSerializedField(hud, "_crowdReaction", crowdReaction);
            WireSerializedField(hud, "_electionController", electionController);
            WireSerializedField(hud, "_authority", authority);
            WireSerializedField(hud, "_gatewayClient", gatewayClient);
            WireSerializedField(hud, "_commanderPanel", commanderPanel);
            WireSerializedField(hud, "_cameraController", camCtrl);

            // 9. Save Scene & Assets
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PoliticalMayhemSceneBuilder] Generated expanded 150x130 prototype scene at {ScenePath}");
        }

        private static void EnsureFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        private static void CreateCityHub(CityId cityId, Transform parent, MapEnvironmentVisuals envVisuals)
        {
            Vector3 pos = CityCatalog.GetWorldPosition(cityId);
            Color cityColor = CityCatalog.GetNeutralColor(cityId);

            GameObject root = new GameObject($"CityHub_{CityCatalog.ToKey(cityId)}");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            // Clean Stone Urban Plaza Platform (21.0 diameter, low sleek 0.08 height so unit feet never sink)
            GameObject platform = CreateScenePrimitive(
                PrimitiveType.Cylinder,
                "CityPlaza",
                root.transform,
                new Vector3(0f, 0.04f, 0f),
                new Vector3(CityVisualBuilder.PlazaDiameter, 0.08f, CityVisualBuilder.PlazaDiameter),
                new Color(0.70f, 0.72f, 0.74f),
                0.10f,
                envVisuals,
                keepCollider: true);
            _ = platform;

            // Expanded Status / Rally Ring (23.0 diameter)
            GameObject ring = CreateScenePrimitive(
                PrimitiveType.Cylinder,
                "StatusRing",
                root.transform,
                new Vector3(0f, 0.02f, 0f),
                new Vector3(CityVisualBuilder.StatusRingDiameter, 0.04f, CityVisualBuilder.StatusRingDiameter),
                Color.Lerp(cityColor, Color.black, 0.25f),
                0.18f,
                envVisuals,
                keepCollider: false);

            // Sleek Ceremonial Capitol Pedestal (updates to team tint when captured, topped by 3D .fbx landmark)
            GameObject capitol = CreateScenePrimitive(
                PrimitiveType.Cylinder,
                "CityCapitol",
                root.transform,
                new Vector3(0f, 0.07f, 0f),
                new Vector3(CityVisualBuilder.CapitolPedestalDiameter, 0.14f, CityVisualBuilder.CapitolPedestalDiameter),
                cityColor,
                0.28f,
                envVisuals,
                keepCollider: true);

            // 3D Central Capitol Landmark (.fbx) & Surrounding District Architecture
            float landmarkHeight = CityVisualBuilder.GetCapitolLandmarkSpec(cityId).targetHeight;
            BuildCityLandmarkDetails(cityId, root.transform, landmarkHeight, cityColor, envVisuals);

            // Crowd Anchor
            GameObject crowdAnchor = new GameObject("CrowdAnchor");
            crowdAnchor.transform.SetParent(root.transform, false);
            crowdAnchor.transform.localPosition = Vector3.zero;

            // 3D Floating Label (Crisp & Legible above tallest Capitol landmark)
            GameObject labelGo = new GameObject("CityLabel");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, Mathf.Max(9.5f, landmarkHeight + 3.2f), 0f);
            labelGo.transform.rotation = Quaternion.Euler(53f, 0f, 0f);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = $"{CityCatalog.GetDisplayName(cityId)}\n{CityCatalog.GetPopulation(cityId):N0} Oy";
            tm.characterSize = 0.24f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.98f, 0.99f, 1.0f);

            CityHubMarker marker = root.AddComponent<CityHubMarker>();
            marker.cityId = cityId;
            marker.hubRenderer = capitol.GetComponent<Renderer>();
            marker.ringRenderer = ring.GetComponent<Renderer>();
            marker.labelMesh = tm;
            marker.crowdAnchor = crowdAnchor.transform;
        }

        private static void BuildCityLandmarkDetails(
            CityId cityId,
            Transform root,
            float height,
            Color cityColor,
            MapEnvironmentVisuals envVisuals)
        {
            Color lightSlate = new Color(0.76f, 0.79f, 0.83f);
            Color midSlate = new Color(0.42f, 0.46f, 0.52f);
            _ = height;
            _ = cityColor;

            bool populated = CityVisualBuilder.PopulateCityLandmarks(
                cityId,
                root,
                envVisuals,
                (rend, tex, matKey) => AssignPersistedTexturedMaterial(rend, matKey, tex, Color.white, 0.18f));

            if (populated)
                return;

            // Primitive fallback if .fbx resources are absent
            switch (cityId)
            {
                case CityId.Baskent:
                    CreateScenePrimitive(PrimitiveType.Cube, "WingLeft", root, new Vector3(-4.0f, 1.2f, 0f), new Vector3(2.0f, 2.0f, 1.8f), lightSlate, 0.18f, envVisuals, false);
                    CreateScenePrimitive(PrimitiveType.Cube, "WingRight", root, new Vector3(4.0f, 1.2f, 0f), new Vector3(2.0f, 2.0f, 1.8f), lightSlate, 0.18f, envVisuals, false);
                    break;

                case CityId.Limankent:
                    CreateScenePrimitive(PrimitiveType.Cube, "HarborPier", root, new Vector3(-4.6f, 0.35f, -3.6f), new Vector3(4.8f, 0.45f, 2.0f), midSlate, 0.15f, envVisuals, false);
                    CreateScenePrimitive(PrimitiveType.Cube, "DepotBlock", root, new Vector3(4.0f, 0.85f, -2.0f), new Vector3(1.8f, 1.2f, 1.4f), lightSlate, 0.18f, envVisuals, false);
                    break;

                case CityId.Demirkent:
                    CreateScenePrimitive(PrimitiveType.Cylinder, "Smokestack_0", root, new Vector3(-4.0f, 2.4f, -1.2f), new Vector3(0.75f, 2.2f, 0.75f), midSlate, 0.18f, envVisuals, false);
                    CreateScenePrimitive(PrimitiveType.Cylinder, "Smokestack_1", root, new Vector3(-4.0f, 2.1f, 1.2f), new Vector3(0.75f, 1.9f, 0.75f), lightSlate, 0.18f, envVisuals, false);
                    break;

                case CityId.Yesilova:
                    CreateScenePrimitive(PrimitiveType.Cylinder, "TerraceA", root, new Vector3(-4.2f, 0.55f, 2.4f), new Vector3(2.0f, 0.75f, 2.0f), lightSlate, 0.18f, envVisuals, false);
                    CreateScenePrimitive(PrimitiveType.Cylinder, "TerraceB", root, new Vector3(4.2f, 0.55f, -2.2f), new Vector3(2.0f, 0.75f, 2.0f), midSlate, 0.18f, envVisuals, false);
                    break;

                case CityId.Universitekent:
                    CreateScenePrimitive(PrimitiveType.Cube, "ClockTower", root, new Vector3(-4.0f, 2.6f, 1.8f), new Vector3(1.3f, 4.8f, 1.3f), lightSlate, 0.18f, envVisuals, false);
                    break;
            }
        }

        private static void CreateRoadStrip(
            Transform parent,
            CityId fromCity,
            CityId toCity,
            Color roadColor,
            Color curbColor,
            Color medianColor,
            MapEnvironmentVisuals envVisuals)
        {
            Vector3 a = CityCatalog.GetWorldPosition(fromCity);
            Vector3 b = CityCatalog.GetWorldPosition(toCity);
            Vector3 mid = (a + b) * 0.5f;
            float dist = Vector3.Distance(a, b);
            Vector3 dir = (b - a).normalized;
            Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;

            string keyA = CityCatalog.ToKey(fromCity);
            string keyB = CityCatalog.ToKey(toCity);

            // Outer curb / shoulder bed (3.8f wide)
            GameObject curb = CreateBoardBlock(
                $"RoadCurb_{keyA}_{keyB}",
                parent,
                new Vector3(mid.x, 0.01f, mid.z),
                new Vector3(3.8f, 0.02f, dist),
                curbColor,
                0.12f,
                envVisuals,
                withCollider: false);
            curb.transform.rotation = rot;

            // Main highway asphalt surface (3.2f wide — fits vans & units comfortably)
            GameObject road = CreateBoardBlock(
                $"Road_{keyA}_{keyB}",
                parent,
                new Vector3(mid.x, 0.025f, mid.z),
                new Vector3(3.2f, 0.035f, dist),
                roadColor,
                0.15f,
                envVisuals,
                withCollider: false);
            road.transform.rotation = rot;

            // Center median lane divider stopping cleanly outside the 23.0-diameter city rings
            float medianLen = Mathf.Max(2f, dist - 24.5f);
            GameObject median = CreateBoardBlock(
                $"RoadMedian_{keyA}_{keyB}",
                parent,
                new Vector3(mid.x, 0.042f, mid.z),
                new Vector3(0.18f, 0.02f, medianLen),
                medianColor,
                0.20f,
                envVisuals,
                withCollider: false);
            median.transform.rotation = rot;

            // Populate highway streetlamps, signs, and roadside trees
            CityVisualBuilder.PopulateRoadCorridorProps(
                parent,
                fromCity,
                toCity,
                envVisuals,
                (rend, tex, matKey) => AssignPersistedTexturedMaterial(rend, matKey, tex, Color.white, 0.18f));
        }

        private static GameObject CreateBoardBlock(
            string name,
            Transform parent,
            Vector3 pos,
            Vector3 scale,
            Color color,
            float smoothness,
            MapEnvironmentVisuals envVisuals,
            bool withCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!withCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;

            Renderer rend = go.GetComponent<Renderer>();
            AssignPersistedMaterial(rend, name, color, smoothness);
            if (envVisuals != null)
                envVisuals.Register(rend, color, smoothness);
            return go;
        }

        private static GameObject CreateScenePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 localScale,
            Color color,
            float smoothness,
            MapEnvironmentVisuals envVisuals,
            bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            Renderer rend = go.GetComponent<Renderer>();
            AssignPersistedMaterial(rend, $"{parent.name}_{name}", color, smoothness);
            if (envVisuals != null && name != "CityCapitol" && name != "StatusRing")
                envVisuals.Register(rend, color, smoothness);
            return go;
        }

        private static void AssignPersistedMaterial(Renderer renderer, string matKey, Color color, float smoothness)
        {
            if (renderer == null) return;
            string safeName = matKey.Replace(" ", "_").Replace("/", "_");
            string assetPath = $"{MaterialsFolder}/{safeName}.mat";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null || !MaterialHelper.IsValidForActivePipeline(mat))
            {
                mat = MaterialHelper.CreateColoredMaterial(color, smoothness, safeName);
                if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            else
            {
                MaterialHelper.SetMaterialColor(mat, color, smoothness);
                EditorUtility.SetDirty(mat);
            }

            renderer.sharedMaterial = mat;
        }

        private static void AssignPersistedTexturedMaterial(
            Renderer renderer,
            string matKey,
            Texture2D baseMap,
            Color tint,
            float smoothness)
        {
            if (renderer == null) return;
            string safeName = matKey.Replace(" ", "_").Replace("/", "_");
            string assetPath = $"{MaterialsFolder}/{safeName}.mat";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null || !MaterialHelper.IsValidForActivePipeline(mat))
            {
                mat = MaterialHelper.CreateTexturedMaterial(baseMap, tint, smoothness, safeName);
                if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            else
            {
                MaterialHelper.SetMaterialColor(mat, tint, smoothness);
                MaterialHelper.SetMaterialTextures(mat, baseMap);
                EditorUtility.SetDirty(mat);
            }

            int slotCount = renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0
                ? renderer.sharedMaterials.Length
                : 1;
            if (slotCount == 1)
            {
                renderer.sharedMaterial = mat;
            }
            else
            {
                Material[] mats = new Material[slotCount];
                for (int i = 0; i < slotCount; i++) mats[i] = mat;
                renderer.sharedMaterials = mats;
            }
        }

        private static void WireSerializedField(Object target, string fieldName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
#endif
