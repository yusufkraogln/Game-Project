using System;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Populates each of the 6 strategic cities with a real 3D central Capitol landmark (.fbx)
    /// plus surrounding thematic district architecture, highway streetlights/signs, and island
    /// forest/rock/park scenery while preserving CityPlaza, StatusRing, and CityCapitol (hubRenderer).
    /// Shared by both Edit-Mode Scene Builder and Play-Mode safety upgrade.
    /// </summary>
    public static class CityVisualBuilder
    {
        public const float PlazaDiameter = 21.0f;
        public const float StatusRingDiameter = 23.0f;
        public const float CapitolPedestalDiameter = 6.0f;
        private const float PlazaTopY = 0.08f;
        private const float PedestalTopY = 0.14f;

        public struct CityModelSpec
        {
            public string modelPath;
            public string texturePath;
            public string matKey;
            public Vector3 localOffset;
            public float yaw;
            public float targetHeight;
            public float groundY;

            public CityModelSpec(
                string modelPath,
                string texturePath,
                string matKey,
                Vector3 localOffset,
                float yaw,
                float targetHeight,
                float groundY = PlazaTopY)
            {
                this.modelPath = modelPath;
                this.texturePath = texturePath;
                this.matKey = matKey;
                this.localOffset = localOffset;
                this.yaw = yaw;
                this.targetHeight = targetHeight;
                this.groundY = groundY;
            }
        }

        private struct ForestClusterSpec
        {
            public Vector3 center;
            public float spreadRadius;
            public int largeTreeCount;
            public int smallTreeCount;
            public bool includeRockOutcrop;
            public string accentModelPath;
            public string accentTexPath;
            public string accentMatKey;
            public float accentHeight;

            public ForestClusterSpec(
                Vector3 center,
                float spreadRadius,
                int largeTreeCount,
                int smallTreeCount,
                bool includeRockOutcrop = false,
                string accentModelPath = null,
                string accentTexPath = null,
                string accentMatKey = null,
                float accentHeight = 2.0f)
            {
                this.center = center;
                this.spreadRadius = spreadRadius;
                this.largeTreeCount = largeTreeCount;
                this.smallTreeCount = smallTreeCount;
                this.includeRockOutcrop = includeRockOutcrop;
                this.accentModelPath = accentModelPath;
                this.accentTexPath = accentTexPath;
                this.accentMatKey = accentMatKey;
                this.accentHeight = accentHeight;
            }
        }

        private static readonly string[] LegacyPlaceholderNames =
        {
            "WingLeft", "WingRight",
            "HarborPier", "DepotBlock",
            "Smokestack_0", "Smokestack_1",
            "TerraceA", "TerraceB",
            "ClockTower",
            "Mountain_0", "Mountain_1", "Mountain_2"
        };

        public static readonly (CityId from, CityId to)[] HighwayRoutes =
        {
            (CityId.Baskent, CityId.Limankent),
            (CityId.Baskent, CityId.Demirkent),
            (CityId.Baskent, CityId.Yesilova),
            (CityId.Baskent, CityId.Universitekent),
            (CityId.Baskent, CityId.Dagsehir),
            (CityId.Limankent, CityId.Yesilova),
            (CityId.Yesilova, CityId.Dagsehir),
            (CityId.Dagsehir, CityId.Universitekent),
            (CityId.Universitekent, CityId.Demirkent),
            (CityId.Limankent, CityId.Demirkent)
        };

        public const string CommercialTex = "Models/Cities/Commercial/Textures/colormap";
        public const string IndustrialTex = "Models/Cities/Industrial/Textures/colormap";
        public const string SuburbanTex = "Models/Cities/Suburban/Textures/colormap";
        public const string RoadsTex = "Models/Props/Roads/Textures/colormap";
        public const string NatureTex = "Models/Nature/nature_palette";
        public const string HarborTex = "Models/Props/Harbor/Textures/colormap";

        public static bool PopulateCityLandmarks(
            CityId cityId,
            Transform cityRoot,
            MapEnvironmentVisuals envVisuals,
            Action<Renderer, Texture2D, string> customMaterialAssigner = null)
        {
            if (cityRoot == null) return false;

            UpgradeCapitolPedestalAndPlaza(cityRoot, cityId);

            int instantiatedCount = 0;

            // 1. Central 3D Capitol Landmark sitting on top of the CityCapitol team pedestal
            CityModelSpec capitolSpec = GetCapitolLandmarkSpec(cityId);
            Transform oldCapitolLandmark = cityRoot.Find("CityCapitolLandmark");
            if (oldCapitolLandmark != null)
                DestroySafe(oldCapitolLandmark.gameObject);

            GameObject centralLandmark = InstantiateAndBindModel(
                capitolSpec,
                "CityCapitolLandmark",
                cityRoot,
                envVisuals,
                customMaterialAssigner);
            if (centralLandmark != null)
                instantiatedCount++;

            // 2. Surrounding district architecture & trees spread around the 14.5-unit plaza
            CityModelSpec[] specs = GetSpecsForCity(cityId);
            for (int i = 0; i < specs.Length; i++)
            {
                string modelName = $"CityModel_{i}";
                Transform existing = cityRoot.Find(modelName);
                if (existing != null)
                    DestroySafe(existing.gameObject);

                GameObject wrapper = InstantiateAndBindModel(
                    specs[i],
                    modelName,
                    cityRoot,
                    envVisuals,
                    customMaterialAssigner);

                if (wrapper != null)
                    instantiatedCount++;
            }

            return instantiatedCount > 0;
        }

        private static GameObject InstantiateAndBindModel(
            CityModelSpec spec,
            string wrapperName,
            Transform parent,
            MapEnvironmentVisuals envVisuals,
            Action<Renderer, Texture2D, string> customMaterialAssigner)
        {
            if (string.IsNullOrEmpty(spec.modelPath))
                return null;

            GameObject wrapper = ModelVisualHelper.InstantiateNormalizedModel(
                spec.modelPath,
                spec.texturePath,
                wrapperName,
                parent,
                spec.localOffset,
                spec.yaw,
                spec.targetHeight,
                normalizeByHeight: true,
                groundLocalY: spec.groundY,
                tint: Color.white,
                smoothness: 0.18f,
                stripColliders: true);

            if (wrapper == null)
                return null;

            Texture2D tex = Resources.Load<Texture2D>(spec.texturePath);
            Renderer[] renderers = wrapper.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rend = renderers[r];
                if (rend == null) continue;

                if (customMaterialAssigner != null)
                    customMaterialAssigner(rend, tex, spec.matKey);

                if (envVisuals != null)
                    envVisuals.Register(rend, Color.white, 0.18f, tex);
            }

            return wrapper;
        }

        /// <summary>
        /// Converts any tall grey cube CityCapitol into a sleek low ceremonial pedestal so the 3D
        /// central landmark stands prominently while CityHubMarker.hubRenderer still tints on capture.
        /// </summary>
        public static void UpgradeCapitolPedestalAndPlaza(Transform cityRoot, CityId? cityIdOverride = null)
        {
            if (cityRoot == null) return;

            Transform plazaTf = cityRoot.Find("CityPlaza");
            if (plazaTf != null)
            {
                plazaTf.localPosition = new Vector3(0f, 0.04f, 0f);
                plazaTf.localScale = new Vector3(PlazaDiameter, 0.08f, PlazaDiameter);
            }

            Transform ringTf = cityRoot.Find("StatusRing");
            if (ringTf != null)
            {
                ringTf.localPosition = new Vector3(0f, 0.02f, 0f);
                ringTf.localScale = new Vector3(StatusRingDiameter, 0.04f, StatusRingDiameter);
            }

            Transform capitolTf = cityRoot.Find("CityCapitol");
            if (capitolTf != null)
            {
                capitolTf.localPosition = new Vector3(0f, 0.07f, 0f);
                capitolTf.localScale = new Vector3(CapitolPedestalDiameter, 0.14f, CapitolPedestalDiameter);
            }

            float labelY = 12.2f;
            if (cityIdOverride.HasValue)
            {
                labelY = Mathf.Max(9.5f, GetCapitolLandmarkSpec(cityIdOverride.Value).targetHeight + 3.2f);
            }
            else if (cityRoot.TryGetComponent(out CityHubMarker marker))
            {
                labelY = Mathf.Max(9.5f, GetCapitolLandmarkSpec(marker.cityId).targetHeight + 3.2f);
            }

            Transform labelTf = cityRoot.Find("CityLabel");
            if (labelTf != null)
            {
                labelTf.localPosition = new Vector3(0f, labelY, 0f);
                TextMesh tm = labelTf.GetComponent<TextMesh>();
                if (tm != null)
                {
                    tm.characterSize = 0.24f;
                    tm.fontSize = 48;
                    tm.color = new Color(0.98f, 0.99f, 1.0f);
                }
            }
        }

        /// <summary>
        /// Places roadside streetlamps, overhead highway signs (centered cleanly over roads),
        /// and boulevard trees along a connected highway strip.
        /// </summary>
        public static void PopulateRoadCorridorProps(
            Transform roadsRoot,
            CityId fromCity,
            CityId toCity,
            MapEnvironmentVisuals envVisuals,
            Action<Renderer, Texture2D, string> customMaterialAssigner = null)
        {
            if (roadsRoot == null) return;

            Vector3 a = CityCatalog.GetWorldPosition(fromCity);
            Vector3 b = CityCatalog.GetWorldPosition(toCity);
            Vector3 delta = b - a;
            float dist = delta.magnitude;
            if (dist < 28f) return;

            Vector3 dir = delta / dist;
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            float roadYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            string groupName = $"RoadProps_{CityCatalog.ToKey(fromCity)}_{CityCatalog.ToKey(toCity)}";
            Transform oldGroup = roadsRoot.Find(groupName);
            if (oldGroup != null)
                DestroySafe(oldGroup.gameObject);

            GameObject propGroup = new GameObject(groupName);
            propGroup.transform.SetParent(roadsRoot, false);

            // Place overhead highway gantry signs centered cleanly over the road outside each city's gate
            float signDistFromCity = 15.2f;
            if (dist > signDistFromCity * 2.25f)
            {
                Vector3 signPosA = a + dir * signDistFromCity;
                if (IsClearOfCitiesAndOtherRoads(signPosA, fromCity, toCity, 13.5f, 3.6f))
                {
                    InstantiateAndBindModel(
                        new CityModelSpec("Models/Props/Roads/sign-highway", RoadsTex, "Mat_Kenney_Roads", signPosA, roadYaw, 3.6f, 0f),
                        "Sign_Departure",
                        propGroup.transform,
                        envVisuals,
                        customMaterialAssigner);
                }

                Vector3 signPosB = b - dir * signDistFromCity;
                if (IsClearOfCitiesAndOtherRoads(signPosB, fromCity, toCity, 13.5f, 3.6f))
                {
                    InstantiateAndBindModel(
                        new CityModelSpec("Models/Props/Roads/sign-highway", RoadsTex, "Mat_Kenney_Roads", signPosB, roadYaw + 180f, 3.6f, 0f),
                        "Sign_Arrival",
                        propGroup.transform,
                        envVisuals,
                        customMaterialAssigner);
                }
            }

            // Place tall streetlamps (3.35u) and roadside trees (3.0-4.4u) along the widened highway corridor
            float startT = 18.5f;
            float endT = dist - 18.5f;
            float step = 16.5f;
            int index = 0;

            for (float d = startT; d <= endT; d += step)
            {
                Vector3 centerPt = a + dir * d;
                float side = (index % 2 == 0) ? 1f : -1f;

                Vector3 lampPos = centerPt + right * (side * 2.35f);
                if (IsClearOfCitiesAndOtherRoads(lampPos, fromCity, toCity, 13.8f, 3.2f))
                {
                    bool useDouble = (index % 3 == 0);
                    string lampModel = useDouble ? "Models/Props/Roads/light-curved-double" : "Models/Props/Roads/light-curved";
                    float lampYaw = roadYaw + (side > 0f ? -90f : 90f);
                    InstantiateAndBindModel(
                        new CityModelSpec(lampModel, RoadsTex, "Mat_Kenney_Roads", lampPos, lampYaw, 3.35f, 0f),
                        $"RoadLamp_{index}",
                        propGroup.transform,
                        envVisuals,
                        customMaterialAssigner);
                }

                Vector3 treePos = centerPt - right * (side * 4.4f);
                if (IsClearOfCitiesAndOtherRoads(treePos, fromCity, toCity, 14.5f, 4.0f))
                {
                    bool large = (index % 2 == 0);
                    string treeModel = large ? "Models/Cities/Suburban/tree-large" : "Models/Cities/Suburban/tree-small";
                    float treeHeight = large ? 4.3f : 3.0f;
                    InstantiateAndBindModel(
                        new CityModelSpec(treeModel, SuburbanTex, "Mat_Kenney_Suburban", treePos, (index * 47f) % 360f, treeHeight, 0f),
                        $"RoadTree_{index}",
                        propGroup.transform,
                        envVisuals,
                        customMaterialAssigner);
                }

                index++;
            }
        }

        /// <summary>
        /// Populates the open terrain across the 150x130 island with forest clusters, subtle park/highland
        /// terrain patches, coastal/mountain rock monoliths, and rural landmark accents.
        /// </summary>
        public static void PopulateIslandScenery(
            Transform envRoot,
            MapEnvironmentVisuals envVisuals,
            Action<Renderer, Texture2D, string> customMaterialAssigner = null,
            Action<Renderer, string, Color, float> customColorAssigner = null)
        {
            if (envRoot == null) return;

            Transform existing = envRoot.Find("SceneryRoot");
            if (existing != null)
                DestroySafe(existing.gameObject);

            GameObject sceneryRoot = new GameObject("SceneryRoot");
            sceneryRoot.transform.SetParent(envRoot, false);

            Color mossPatchColor = new Color(0.24f, 0.39f, 0.22f);
            Color highlandPatchColor = new Color(0.35f, 0.42f, 0.30f);

            string[] rockModels = { "Models/Nature/rock_1", "Models/Nature/rock_2", "Models/Nature/rock_moss_1" };

            ForestClusterSpec[] clusters = GetIslandClusters();
            for (int c = 0; c < clusters.Length; c++)
            {
                ForestClusterSpec cluster = clusters[c];
                if (!IsClearOfAllCitiesAndRoads(cluster.center, 15.2f, 4.8f))
                    continue;

                GameObject clusterGo = new GameObject($"SceneryCluster_{c}");
                clusterGo.transform.SetParent(sceneryRoot.transform, false);
                clusterGo.transform.position = cluster.center;

                // Subtle lush meadow / highland ground patch under the cluster (no collider so unit raycasts hit MapGround)
                Color patchColor = cluster.includeRockOutcrop ? highlandPatchColor : mossPatchColor;
                CreateSceneryBlock(
                    PrimitiveType.Cylinder,
                    $"TerrainPatch_{c}",
                    clusterGo.transform,
                    new Vector3(0f, 0.015f, 0f),
                    new Vector3(cluster.spreadRadius * 1.9f, 0.02f, cluster.spreadRadius * 1.7f),
                    patchColor,
                    0.06f,
                    envVisuals,
                    customColorAssigner);

                // Optional rural/outpost accent building or industrial silo (properly scaled!)
                if (!string.IsNullOrEmpty(cluster.accentModelPath))
                {
                    InstantiateAndBindModel(
                        new CityModelSpec(
                            cluster.accentModelPath,
                            cluster.accentTexPath,
                            cluster.accentMatKey,
                            Vector3.zero,
                            (c * 55f) % 360f,
                            cluster.accentHeight,
                            0f),
                        $"ClusterAccent_{c}",
                        clusterGo.transform,
                        envVisuals,
                        customMaterialAssigner);
                }

                // Real 3D low-poly rock outcrops (replacing legacy grey cubes)
                if (cluster.includeRockOutcrop)
                {
                    string rockA = rockModels[c % rockModels.Length];
                    string rockB = rockModels[(c + 1) % rockModels.Length];
                    InstantiateAndBindModel(
                        new CityModelSpec(rockA, NatureTex, "Mat_Nature_Palette", new Vector3(-1.8f, 0f, 1.3f), c * 29f, 3.0f, 0f),
                        $"RockOutcropA_{c}",
                        clusterGo.transform,
                        envVisuals,
                        customMaterialAssigner);

                    InstantiateAndBindModel(
                        new CityModelSpec(rockB, NatureTex, "Mat_Nature_Palette", new Vector3(1.5f, 0f, 1.6f), -c * 37f, 3.9f, 0f),
                        $"RockOutcropB_{c}",
                        clusterGo.transform,
                        envVisuals,
                        customMaterialAssigner);
                }

                int totalTrees = cluster.largeTreeCount + cluster.smallTreeCount;
                for (int t = 0; t < totalTrees; t++)
                {
                    float angle = (t / (float)Mathf.Max(1, totalTrees)) * Mathf.PI * 2f + (c * 0.65f);
                    float ringFactor = (t % 2 == 0) ? 0.56f : 0.90f;
                    float r = cluster.spreadRadius * ringFactor;
                    Vector3 localOffset = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                    Vector3 worldTreePos = cluster.center + localOffset;

                    if (!IsClearOfAllCitiesAndRoads(worldTreePos, 14.5f, 4.0f))
                        continue;

                    bool isLarge = t < cluster.largeTreeCount;
                    bool usePine = isLarge && ((c + t) % 3 == 0);
                    string treePath = usePine
                        ? "Models/Nature/pine_tree_1"
                        : (isLarge ? "Models/Cities/Suburban/tree-large" : "Models/Cities/Suburban/tree-small");
                    string treeTex = usePine ? NatureTex : SuburbanTex;
                    string treeMat = usePine ? "Mat_Nature_Palette" : "Mat_Kenney_Suburban";
                    float height = isLarge
                        ? 4.4f + ((c + t) % 3) * 0.45f
                        : 2.9f + ((c + t) % 3) * 0.30f;

                    InstantiateAndBindModel(
                        new CityModelSpec(treePath, treeTex, treeMat, localOffset, ((c * 31 + t * 67) % 360), height, 0f),
                        $"Tree_{c}_{t}",
                        clusterGo.transform,
                        envVisuals,
                        customMaterialAssigner);
                }
            }

            // Harbor boats floating cleanly in the coastal ocean visible near Limankent & shores
            GameObject harborRoot = new GameObject("HarborBoats");
            harborRoot.transform.SetParent(sceneryRoot.transform, false);
            InstantiateAndBindModel(
                new CityModelSpec("Models/Props/Harbor/boat_sail", HarborTex, "Mat_Kenney_Harbor", new Vector3(-80f, 0f, -26f), 32f, 4.8f, -0.40f),
                "HarborBoat_Sail1", harborRoot.transform, envVisuals, customMaterialAssigner);
            InstantiateAndBindModel(
                new CityModelSpec("Models/Props/Harbor/boat_fishing", HarborTex, "Mat_Kenney_Harbor", new Vector3(-62f, 0f, -55f), 75f, 3.4f, -0.40f),
                "HarborBoat_Fishing1", harborRoot.transform, envVisuals, customMaterialAssigner);
            InstantiateAndBindModel(
                new CityModelSpec("Models/Props/Harbor/boat_speed", HarborTex, "Mat_Kenney_Harbor", new Vector3(-44f, 0f, -55f), 110f, 2.4f, -0.40f),
                "HarborBoat_Speed1", harborRoot.transform, envVisuals, customMaterialAssigner);
            InstantiateAndBindModel(
                new CityModelSpec("Models/Props/Harbor/boat_sail", HarborTex, "Mat_Kenney_Harbor", new Vector3(82f, 0f, -14f), -45f, 4.6f, -0.40f),
                "HarborBoat_EastSail", harborRoot.transform, envVisuals, customMaterialAssigner);
        }

        private static GameObject CreateSceneryBlock(
            PrimitiveType primitiveType,
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 localScale,
            Color color,
            float smoothness,
            MapEnvironmentVisuals envVisuals,
            Action<Renderer, string, Color, float> customColorAssigner)
        {
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                DestroySafe(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            Renderer rend = go.GetComponent<Renderer>();
            if (customColorAssigner != null)
            {
                customColorAssigner(rend, $"Mat_Scenery_{primitiveType}_{ColorUtility.ToHtmlStringRGB(color)}", color, smoothness);
            }
            else
            {
                MaterialHelper.ApplyColor(rend, color, smoothness);
            }

            if (envVisuals != null)
                envVisuals.Register(rend, color, smoothness);

            return go;
        }

        public static bool IsClearOfAllCitiesAndRoads(Vector3 worldPos, float minCityDist, float minRoadDist)
        {
            Vector2 p = new Vector2(worldPos.x, worldPos.z);

            for (int i = 0; i < CityCatalog.All.Length; i++)
            {
                Vector3 c = CityCatalog.GetWorldPosition(CityCatalog.All[i]);
                if (Vector2.Distance(p, new Vector2(c.x, c.z)) < minCityDist)
                    return false;
            }

            for (int i = 0; i < HighwayRoutes.Length; i++)
            {
                Vector3 a = CityCatalog.GetWorldPosition(HighwayRoutes[i].from);
                Vector3 b = CityCatalog.GetWorldPosition(HighwayRoutes[i].to);
                if (DistancePointToSegment2D(p, new Vector2(a.x, a.z), new Vector2(b.x, b.z)) < minRoadDist)
                    return false;
            }

            return true;
        }

        private static bool IsClearOfCitiesAndOtherRoads(
            Vector3 worldPos,
            CityId ownFrom,
            CityId ownTo,
            float minCityDist,
            float minOtherRoadDist)
        {
            Vector2 p = new Vector2(worldPos.x, worldPos.z);

            for (int i = 0; i < CityCatalog.All.Length; i++)
            {
                Vector3 c = CityCatalog.GetWorldPosition(CityCatalog.All[i]);
                if (Vector2.Distance(p, new Vector2(c.x, c.z)) < minCityDist)
                    return false;
            }

            for (int i = 0; i < HighwayRoutes.Length; i++)
            {
                var route = HighwayRoutes[i];
                if ((route.from == ownFrom && route.to == ownTo) || (route.from == ownTo && route.to == ownFrom))
                    continue;

                Vector3 a = CityCatalog.GetWorldPosition(route.from);
                Vector3 b = CityCatalog.GetWorldPosition(route.to);
                if (DistancePointToSegment2D(p, new Vector2(a.x, a.z), new Vector2(b.x, b.z)) < minOtherRoadDist)
                    return false;
            }

            return true;
        }

        private static float DistancePointToSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        /// <summary>
        /// Ensures an existing scene loaded in Play Mode upgrades legacy city positions, plazas,
        /// central Capitol landmarks, highways, and island scenery even if MainMapScene was not regenerated.
        /// </summary>
        public static void EnsureCityLandmarksUpgraded(MapEnvironmentVisuals envVisuals = null)
        {
            if (envVisuals == null)
                envVisuals = UnityEngine.Object.FindAnyObjectByType<MapEnvironmentVisuals>();

            GameObject envRoot = GameObject.Find("EnvironmentRoot");
            UpgradeRuntimeMapDimensionsIfNeeded(envRoot, envVisuals);

            foreach (CityId cityId in CityCatalog.All)
            {
                GameObject hubGo = GameObject.Find($"CityHub_{CityCatalog.ToKey(cityId)}");
                if (hubGo == null) continue;

                hubGo.transform.position = CityCatalog.GetWorldPosition(cityId);
                UpgradeCapitolPedestalAndPlaza(hubGo.transform, cityId);

                bool hasCapitolLandmark = hubGo.transform.Find("CityCapitolLandmark") != null;
                bool hasDistrictModels = hubGo.transform.Find("CityModel_0") != null;
                Transform plazaCheck = hubGo.transform.Find("CityPlaza");
                bool plazaNeedsScaleUpgrade = plazaCheck == null || plazaCheck.localScale.x < 19f;
                if (hasCapitolLandmark && hasDistrictModels && !plazaNeedsScaleUpgrade)
                    continue;

                CityModelSpec[] specs = GetSpecsForCity(cityId);
                if (specs.Length == 0 || Resources.Load<GameObject>(specs[0].modelPath) == null)
                    continue;

                RemoveLegacyPlaceholders(hubGo.transform);
                PopulateCityLandmarks(cityId, hubGo.transform, envVisuals);
            }

            if (envRoot != null && envRoot.transform.Find("SceneryRoot") == null)
            {
                PopulateIslandScenery(envRoot.transform, envVisuals);
            }
        }

        private static void UpgradeRuntimeMapDimensionsIfNeeded(GameObject envRoot, MapEnvironmentVisuals envVisuals)
        {
            GameObject mapGround = GameObject.Find("MapGround");
            if (mapGround != null && mapGround.transform.localScale.x < 140f)
            {
                mapGround.transform.position = new Vector3(0f, -0.25f, 16f);
                mapGround.transform.localScale = new Vector3(150f, 0.5f, 130f);
            }

            GameObject ocean = GameObject.Find("OceanWater");
            if (ocean != null && ocean.transform.localScale.x < 300f)
            {
                ocean.transform.position = new Vector3(0f, -0.55f, 16f);
                ocean.transform.localScale = new Vector3(360f, 0.3f, 320f);
            }

            if (envRoot == null) return;

            Transform coastNorth = envRoot.transform.Find("CoastNorth");
            if (coastNorth != null)
            {
                coastNorth.position = new Vector3(0f, -0.18f, 81.6f);
                coastNorth.localScale = new Vector3(152.4f, 0.55f, 1.4f);
            }
            Transform coastSouth = envRoot.transform.Find("CoastSouth");
            if (coastSouth != null)
            {
                coastSouth.position = new Vector3(0f, -0.18f, -49.6f);
                coastSouth.localScale = new Vector3(152.4f, 0.55f, 1.4f);
            }
            Transform coastWest = envRoot.transform.Find("CoastWest");
            if (coastWest != null)
            {
                coastWest.position = new Vector3(-75.6f, -0.18f, 16f);
                coastWest.localScale = new Vector3(1.4f, 0.55f, 132.4f);
            }
            Transform coastEast = envRoot.transform.Find("CoastEast");
            if (coastEast != null)
            {
                coastEast.position = new Vector3(75.6f, -0.18f, 16f);
                coastEast.localScale = new Vector3(1.4f, 0.55f, 132.4f);
            }

            Transform roadsRoot = envRoot.transform.Find("RoadsRoot");
            if (roadsRoot != null && roadsRoot.Find("RoadCurb_Baskent_Limankent") == null)
            {
                for (int i = roadsRoot.childCount - 1; i >= 0; i--)
                {
                    DestroySafe(roadsRoot.GetChild(i).gameObject);
                }

                Color roadColor = new Color(0.24f, 0.26f, 0.30f);
                Color curbColor = new Color(0.54f, 0.57f, 0.60f);
                Color medianColor = new Color(0.90f, 0.80f, 0.32f);

                for (int i = 0; i < HighwayRoutes.Length; i++)
                {
                    var route = HighwayRoutes[i];
                    Vector3 a = CityCatalog.GetWorldPosition(route.from);
                    Vector3 b = CityCatalog.GetWorldPosition(route.to);
                    Vector3 mid = (a + b) * 0.5f;
                    float dist = Vector3.Distance(a, b);
                    Vector3 dir = (b - a).normalized;
                    Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;
                    string keyA = CityCatalog.ToKey(route.from);
                    string keyB = CityCatalog.ToKey(route.to);

                    GameObject curb = CreateSceneryBlock(
                        PrimitiveType.Cube,
                        $"RoadCurb_{keyA}_{keyB}",
                        roadsRoot,
                        new Vector3(mid.x, 0.01f, mid.z),
                        new Vector3(3.8f, 0.02f, dist),
                        curbColor,
                        0.12f,
                        envVisuals,
                        null);
                    curb.transform.rotation = rot;

                    GameObject road = CreateSceneryBlock(
                        PrimitiveType.Cube,
                        $"Road_{keyA}_{keyB}",
                        roadsRoot,
                        new Vector3(mid.x, 0.025f, mid.z),
                        new Vector3(3.2f, 0.035f, dist),
                        roadColor,
                        0.15f,
                        envVisuals,
                        null);
                    road.transform.rotation = rot;

                    float medianLen = Mathf.Max(2f, dist - 24.5f);
                    GameObject median = CreateSceneryBlock(
                        PrimitiveType.Cube,
                        $"RoadMedian_{keyA}_{keyB}",
                        roadsRoot,
                        new Vector3(mid.x, 0.042f, mid.z),
                        new Vector3(0.18f, 0.02f, medianLen),
                        medianColor,
                        0.20f,
                        envVisuals,
                        null);
                    median.transform.rotation = rot;

                    PopulateRoadCorridorProps(roadsRoot, route.from, route.to, envVisuals);
                }
            }
        }

        private static void RemoveLegacyPlaceholders(Transform cityRoot)
        {
            for (int i = 0; i < LegacyPlaceholderNames.Length; i++)
            {
                Transform legacy = cityRoot.Find(LegacyPlaceholderNames[i]);
                if (legacy != null)
                    DestroySafe(legacy.gameObject);
            }
        }

        private static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        public static CityModelSpec GetCapitolLandmarkSpec(CityId cityId)
        {
            switch (cityId)
            {
                case CityId.Baskent:
                    return new CityModelSpec(
                        "Models/Cities/Commercial/building-skyscraper-c",
                        CommercialTex,
                        "Mat_Kenney_Commercial",
                        Vector3.zero,
                        0f,
                        10.2f,
                        PedestalTopY);

                case CityId.Limankent:
                    return new CityModelSpec(
                        "Models/Cities/Commercial/building-l",
                        CommercialTex,
                        "Mat_Kenney_Commercial",
                        Vector3.zero,
                        0f,
                        6.8f,
                        PedestalTopY);

                case CityId.Demirkent:
                    return new CityModelSpec(
                        "Models/Cities/Industrial/building-l",
                        IndustrialTex,
                        "Mat_Kenney_Industrial",
                        Vector3.zero,
                        0f,
                        6.8f,
                        PedestalTopY);

                case CityId.Yesilova:
                    return new CityModelSpec(
                        "Models/Cities/Suburban/building-type-a",
                        SuburbanTex,
                        "Mat_Kenney_Suburban",
                        Vector3.zero,
                        0f,
                        5.4f,
                        PedestalTopY);

                case CityId.Universitekent:
                    return new CityModelSpec(
                        "Models/Cities/Commercial/building-m",
                        CommercialTex,
                        "Mat_Kenney_Commercial",
                        Vector3.zero,
                        0f,
                        7.8f,
                        PedestalTopY);

                case CityId.Dagsehir:
                default:
                    return new CityModelSpec(
                        "Models/Cities/Suburban/building-type-e",
                        SuburbanTex,
                        "Mat_Kenney_Suburban",
                        Vector3.zero,
                        0f,
                        5.4f,
                        PedestalTopY);
            }
        }

        private static CityModelSpec[] GetSpecsForCity(CityId cityId)
        {
            // All cities keep the southern front courtyard (Z in [-9.2, -3.5], X in [-5.8, 5.8])
            // wide open so Rally Crowds & Campaign Units NEVER clip into buildings.
            switch (cityId)
            {
                case CityId.Baskent:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Commercial/building-skyscraper-a", CommercialTex, "Mat_Kenney_Commercial", new Vector3(-5.8f, 0f, 3.6f), 15f, 8.2f),
                        new CityModelSpec("Models/Cities/Commercial/building-m", CommercialTex, "Mat_Kenney_Commercial", new Vector3(5.8f, 0f, 3.6f), -15f, 7.2f),
                        new CityModelSpec("Models/Cities/Commercial/building-l", CommercialTex, "Mat_Kenney_Commercial", new Vector3(-7.4f, 0f, -1.0f), 25f, 5.6f),
                        new CityModelSpec("Models/Cities/Commercial/building-j", CommercialTex, "Mat_Kenney_Commercial", new Vector3(7.4f, 0f, -1.0f), -25f, 5.2f),
                        new CityModelSpec("Models/Cities/Commercial/building-k", CommercialTex, "Mat_Kenney_Commercial", new Vector3(0f, 0f, 6.8f), 0f, 5.8f),
                        new CityModelSpec("Models/Props/Roads/light-curved-double", RoadsTex, "Mat_Kenney_Roads", new Vector3(-3.8f, 0f, -8.5f), 0f, 3.3f),
                        new CityModelSpec("Models/Props/Roads/light-curved-double", RoadsTex, "Mat_Kenney_Roads", new Vector3(3.8f, 0f, -8.5f), 0f, 3.3f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-8.2f, 0f, -5.8f), 15f, 4.4f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(8.2f, 0f, -5.8f), 65f, 4.4f)
                    };

                case CityId.Limankent:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Industrial/building-d", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(-6.2f, 0f, 3.8f), 15f, 5.2f),
                        new CityModelSpec("Models/Cities/Industrial/building-q", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(6.4f, 0f, 3.6f), -15f, 4.4f),
                        new CityModelSpec("Models/Cities/Industrial/building-p", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(-7.8f, 0f, -0.6f), 10f, 4.2f),
                        new CityModelSpec("Models/Cities/Industrial/detail-tank", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(7.6f, 0f, -0.6f), 35f, 3.1f),
                        new CityModelSpec("Models/Cities/Commercial/building-j", CommercialTex, "Mat_Kenney_Commercial", new Vector3(0f, 0f, 7.0f), 0f, 5.0f),
                        new CityModelSpec("Models/Props/Roads/light-curved", RoadsTex, "Mat_Kenney_Roads", new Vector3(-4.2f, 0f, -8.5f), 0f, 3.2f),
                        new CityModelSpec("Models/Props/Roads/light-curved", RoadsTex, "Mat_Kenney_Roads", new Vector3(4.2f, 0f, -8.5f), 180f, 3.2f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-8.4f, 0f, 6.2f), 20f, 4.2f)
                    };

                case CityId.Demirkent:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Industrial/building-e", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(-6.4f, 0f, 3.6f), 0f, 5.2f),
                        new CityModelSpec("Models/Cities/Industrial/building-d", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(6.4f, 0f, 3.6f), 180f, 5.0f),
                        new CityModelSpec("Models/Cities/Industrial/chimney-large", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(-7.6f, 0f, -1.0f), 0f, 8.6f),
                        new CityModelSpec("Models/Cities/Industrial/chimney-large", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(7.6f, 0f, -1.0f), 45f, 7.8f),
                        new CityModelSpec("Models/Cities/Industrial/detail-tank", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(0f, 0f, 7.0f), 0f, 3.1f),
                        new CityModelSpec("Models/Props/Roads/light-curved-double", RoadsTex, "Mat_Kenney_Roads", new Vector3(-4.2f, 0f, -8.5f), 0f, 3.3f),
                        new CityModelSpec("Models/Props/Roads/light-curved-double", RoadsTex, "Mat_Kenney_Roads", new Vector3(4.2f, 0f, -8.5f), 0f, 3.3f),
                        new CityModelSpec("Models/Cities/Industrial/building-p", IndustrialTex, "Mat_Kenney_Industrial", new Vector3(11.0f, 0f, -6.2f), -30f, 4.2f, 0f)
                    };

                case CityId.Yesilova:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Suburban/building-type-b", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-6.2f, 0f, 3.6f), 20f, 4.3f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-f", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(6.2f, 0f, 3.6f), -20f, 4.2f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-j", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-7.5f, 0f, -1.5f), 15f, 4.0f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-n", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(7.5f, 0f, -1.5f), -15f, 4.0f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(0f, 0f, 7.2f), 0f, 4.8f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-7.2f, 0f, 6.4f), 40f, 4.6f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(7.2f, 0f, 6.4f), 75f, 4.6f),
                        new CityModelSpec("Models/Cities/Suburban/tree-small", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-4.2f, 0f, -8.4f), 15f, 3.0f),
                        new CityModelSpec("Models/Cities/Suburban/tree-small", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(4.2f, 0f, -8.4f), 65f, 3.0f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-e", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-11.8f, 0f, 6.5f), 25f, 4.1f, 0f)
                    };

                case CityId.Universitekent:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Commercial/building-k", CommercialTex, "Mat_Kenney_Commercial", new Vector3(-6.2f, 0f, 3.5f), 15f, 5.4f),
                        new CityModelSpec("Models/Cities/Commercial/building-j", CommercialTex, "Mat_Kenney_Commercial", new Vector3(6.2f, 0f, 3.5f), -15f, 5.0f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-n", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-7.4f, 0f, -1.4f), 25f, 4.1f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-b", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(7.4f, 0f, -1.4f), -25f, 4.1f),
                        new CityModelSpec("Models/Cities/Suburban/tree-large", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(0f, 0f, 7.0f), 25f, 4.6f),
                        new CityModelSpec("Models/Cities/Suburban/tree-small", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-4.2f, 0f, -8.3f), 0f, 3.0f),
                        new CityModelSpec("Models/Props/Roads/light-curved", RoadsTex, "Mat_Kenney_Roads", new Vector3(4.2f, 0f, -8.3f), 180f, 3.2f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-f", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(11.8f, 0f, 5.8f), -30f, 4.0f, 0f)
                    };

                case CityId.Dagsehir:
                    return new[]
                    {
                        new CityModelSpec("Models/Cities/Suburban/building-type-j", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-7.4f, 0f, -0.5f), 20f, 4.1f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-a", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(7.4f, 0f, -0.5f), -20f, 4.1f),
                        new CityModelSpec("Models/Cities/Suburban/building-type-f", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(0f, 0f, 6.4f), 0f, 4.0f),
                        new CityModelSpec("Models/Nature/pine_tree_1", NatureTex, "Mat_Nature_Palette", new Vector3(-6.4f, 0f, 4.2f), 10f, 5.2f),
                        new CityModelSpec("Models/Nature/pine_tree_1", NatureTex, "Mat_Nature_Palette", new Vector3(6.4f, 0f, 4.2f), 55f, 5.2f),
                        new CityModelSpec("Models/Cities/Suburban/tree-small", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(-4.2f, 0f, -8.3f), 30f, 3.0f),
                        new CityModelSpec("Models/Cities/Suburban/tree-small", SuburbanTex, "Mat_Kenney_Suburban", new Vector3(4.2f, 0f, -8.3f), 80f, 3.0f),
                        // Real 3D snow-capped alpine mountains framing Dağşehir's northern skyline (replacing legacy grey cubes)
                        new CityModelSpec("Models/Nature/mountain_snow_2", NatureTex, "Mat_Nature_Palette", new Vector3(-11.5f, 0f, 10.5f), 25f, 11.5f, 0f),
                        new CityModelSpec("Models/Nature/mountain_snow_1", NatureTex, "Mat_Nature_Palette", new Vector3(0f, 0f, 14.5f), -12f, 14.8f, 0f),
                        new CityModelSpec("Models/Nature/mountain_snow_3", NatureTex, "Mat_Nature_Palette", new Vector3(11.5f, 0f, 10.5f), 140f, 10.8f, 0f)
                    };

                default:
                    return Array.Empty<CityModelSpec>();
            }
        }

        private static ForestClusterSpec[] GetIslandClusters()
        {
            return new[]
            {
                // Central-West Valley between Başkent, Limankent, and Yeşilova
                new ForestClusterSpec(new Vector3(-26f, 0f, 10f), 6.2f, 4, 3, false, "Models/Cities/Suburban/building-type-f", SuburbanTex, "Mat_Kenney_Suburban", 3.8f),
                new ForestClusterSpec(new Vector3(-34f, 0f, -4f), 5.6f, 4, 2, false),
                new ForestClusterSpec(new Vector3(-16f, 0f, -6f), 5.0f, 3, 3, false),

                // Central-East Valley between Başkent, Demirkent, and Üniversitekent
                new ForestClusterSpec(new Vector3(25f, 0f, 15f), 6.0f, 4, 3, false, "Models/Cities/Suburban/building-type-j", SuburbanTex, "Mat_Kenney_Suburban", 3.8f),
                new ForestClusterSpec(new Vector3(34f, 0f, 16f), 5.2f, 3, 2, true),
                new ForestClusterSpec(new Vector3(17f, 0f, -8f), 5.0f, 3, 2, false),

                // Southern Heartland & Coastal Plains (between Limankent and Demirkent)
                new ForestClusterSpec(new Vector3(0f, 0f, -29f), 6.6f, 5, 3, true),
                new ForestClusterSpec(new Vector3(-22f, 0f, -35f), 5.8f, 4, 3, false, "Models/Cities/Suburban/building-type-b", SuburbanTex, "Mat_Kenney_Suburban", 3.7f),
                new ForestClusterSpec(new Vector3(22f, 0f, -31f), 5.8f, 4, 3, true, "Models/Cities/Industrial/detail-tank", IndustrialTex, "Mat_Kenney_Industrial", 3.0f),

                // Northern Alpine Foothills (framing Başkent, Yeşilova, Dağşehir, Üniversitekent)
                new ForestClusterSpec(new Vector3(-19f, 0f, 40f), 5.8f, 4, 3, true),
                new ForestClusterSpec(new Vector3(19f, 0f, 40f), 5.8f, 4, 3, true),
                new ForestClusterSpec(new Vector3(-26f, 0f, 65f), 6.2f, 5, 3, true),
                new ForestClusterSpec(new Vector3(26f, 0f, 65f), 6.2f, 5, 3, true),

                // Western Coastal Outskirts (west of Limankent & Yeşilova)
                new ForestClusterSpec(new Vector3(-63f, 0f, 6f), 6.0f, 4, 3, true),
                new ForestClusterSpec(new Vector3(-64f, 0f, -13f), 5.2f, 3, 2, true, "Models/Cities/Industrial/detail-tank", IndustrialTex, "Mat_Kenney_Industrial", 2.9f),
                new ForestClusterSpec(new Vector3(-61f, 0f, 24f), 5.4f, 4, 2, false),
                new ForestClusterSpec(new Vector3(-59f, 0f, 54f), 5.8f, 4, 3, true),

                // Eastern Coastal Outskirts (east of Demirkent & Üniversitekent)
                new ForestClusterSpec(new Vector3(63f, 0f, 16f), 6.0f, 4, 3, true),
                new ForestClusterSpec(new Vector3(63f, 0f, -24f), 5.6f, 3, 2, true, "Models/Cities/Industrial/building-p", IndustrialTex, "Mat_Kenney_Industrial", 4.0f),
                new ForestClusterSpec(new Vector3(59f, 0f, 56f), 5.8f, 4, 3, true)
            };
        }
    }
}
