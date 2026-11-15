using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Optional 3D marker attached to each city hub GameObject in the scene
    /// so city colors, active rally highlights, and crowd anchor positions update automatically.
    /// </summary>
    public class CityHubMarker : MonoBehaviour
    {
        public CityId cityId;
        public Renderer hubRenderer;
        public Renderer ringRenderer;
        public TextMesh labelMesh;
        public Transform crowdAnchor;

        public void RefreshVisual(CityState state, bool isActiveRallyCity)
        {
            if (state == null) return;

            Color targetColor = CityCatalog.GetNeutralColor(cityId);
            if (state.winnerSlot == PlayerSlot.PlayerA)
                targetColor = new Color(0.18f, 0.48f, 0.95f);
            else if (state.winnerSlot == PlayerSlot.PlayerB)
                targetColor = new Color(0.90f, 0.22f, 0.24f);

            if (hubRenderer != null)
            {
                MaterialHelper.ApplyColor(hubRenderer, targetColor, 0.35f);
            }

            if (ringRenderer != null)
            {
                if (isActiveRallyCity)
                    MaterialHelper.ApplyColor(ringRenderer, new Color(1.0f, 0.85f, 0.15f), 0.45f);
                else if (state.rallyDone)
                    MaterialHelper.ApplyColor(ringRenderer, new Color(0.42f, 0.45f, 0.50f), 0.2f);
                else
                    MaterialHelper.ApplyColor(ringRenderer, Color.Lerp(targetColor, Color.black, 0.25f), 0.25f);
            }

            if (labelMesh != null)
            {
                string statusTag = state.winnerSlot != PlayerSlot.None
                    ? (state.winnerSlot == PlayerSlot.PlayerA ? " [A KAZANDI]" : " [B KAZANDI]")
                    : (isActiveRallyCity ? " [MİTİNG]" : (state.rallyDone ? " [TAMAM]" : ""));
                labelMesh.text = $"{state.displayName}\n{state.population:N0} Oy{statusTag}";
            }
        }
    }

    /// <summary>
    /// Manages the 6 fixed-population strategic cities, deterministic seeded ideology distributions,
    /// rally completion flags, campaign influence, and city winners.
    /// </summary>
    public class CityManager : MonoBehaviour
    {
        [SerializeField] private List<CityState> _cities = new List<CityState>();
        private readonly Dictionary<CityId, CityHubMarker> _hubMarkers = new Dictionary<CityId, CityHubMarker>();

        public IReadOnlyList<CityState> Cities => _cities;

        public void RegisterMarkersFromScene()
        {
            _hubMarkers.Clear();
            CityHubMarker[] markers = FindObjectsByType<CityHubMarker>();
            foreach (CityHubMarker marker in markers)
            {
                if (marker != null && !_hubMarkers.ContainsKey(marker.cityId))
                {
                    _hubMarkers[marker.cityId] = marker;
                }
            }
        }

        public void InitializeCities(int seed)
        {
            _cities.Clear();
            System.Random rng = new System.Random(seed);

            foreach (CityId cityId in CityCatalog.All)
            {
                IdeologyScoresDto dist = GenerateSeededIdeologyDistribution(rng, cityId);
                CityState state = new CityState
                {
                    cityId = cityId,
                    cityName = CityCatalog.ToKey(cityId),
                    displayName = CityCatalog.GetDisplayName(cityId),
                    population = CityCatalog.GetPopulation(cityId),
                    ideologyDistribution = dist,
                    rallyDone = false,
                    rallyRoundNumber = 0,
                    rallySpeakerSlot = PlayerSlot.None,
                    winnerSlot = PlayerSlot.None
                };
                _cities.Add(state);
            }

            RegisterMarkersFromScene();
            RefreshAllVisuals(null);
        }

        private static IdeologyScoresDto GenerateSeededIdeologyDistribution(System.Random rng, CityId cityId)
        {
            // Randomized per match seed, with slight regional character and strict 100.0% sum.
            float[] weights = new float[5];
            for (int i = 0; i < 5; i++)
            {
                weights[i] = 10f + (float)(rng.NextDouble() * 30.0);
            }

            // Mild realistic baseline tendencies while staying randomized each match
            switch (cityId)
            {
                case CityId.Baskent:
                    weights[(int)IdeologyType.Imperialist] += 6f;
                    weights[(int)IdeologyType.Nationalist] += 4f;
                    break;
                case CityId.Limankent:
                    weights[(int)IdeologyType.Liberal] += 8f;
                    weights[(int)IdeologyType.Democrat] += 4f;
                    break;
                case CityId.Demirkent:
                    weights[(int)IdeologyType.Communist] += 8f;
                    weights[(int)IdeologyType.Nationalist] += 4f;
                    break;
                case CityId.Yesilova:
                    weights[(int)IdeologyType.Democrat] += 6f;
                    break;
                case CityId.Universitekent:
                    weights[(int)IdeologyType.Liberal] += 5f;
                    weights[(int)IdeologyType.Communist] += 5f;
                    break;
                case CityId.Dagsehir:
                    weights[(int)IdeologyType.Nationalist] += 7f;
                    break;
            }

            IdeologyScoresDto dto = new IdeologyScoresDto(weights[0], weights[1], weights[2], weights[3], weights[4]);
            dto.NormalizeTo100();
            return dto;
        }

        public CityState GetCity(CityId cityId)
        {
            for (int i = 0; i < _cities.Count; i++)
            {
                if (_cities[i].cityId == cityId) return _cities[i];
            }
            return null;
        }

        public CityState GetCityByName(string cityName)
        {
            if (CityCatalog.TryParse(cityName, out CityId cityId))
            {
                return GetCity(cityId);
            }
            return null;
        }

        public List<CityState> GetRemainingRallyCities()
        {
            List<CityState> remaining = new List<CityState>();
            for (int i = 0; i < _cities.Count; i++)
            {
                if (!_cities[i].rallyDone)
                    remaining.Add(_cities[i]);
            }
            return remaining;
        }

        public Vector3 GetCityWorldPosition(CityId cityId)
        {
            if (_hubMarkers.TryGetValue(cityId, out CityHubMarker marker) && marker != null)
            {
                return marker.transform.position;
            }
            return CityCatalog.GetWorldPosition(cityId);
        }

        public Vector3 GetZoneWorldPosition(CityId cityId, string targetZone)
        {
            return GetCityWorldPosition(cityId) + CityCatalog.GetZoneOffset(targetZone);
        }

        public CityId GetClosestCity(Vector3 worldPos)
        {
            CityId best = CityId.Baskent;
            float bestDistSq = float.MaxValue;
            foreach (CityId id in CityCatalog.All)
            {
                float dSq = (worldPos - GetCityWorldPosition(id)).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = id;
                }
            }
            return best;
        }

        public Transform GetCrowdAnchor(CityId cityId)
        {
            if (_hubMarkers.TryGetValue(cityId, out CityHubMarker marker) && marker != null)
            {
                return marker.crowdAnchor != null ? marker.crowdAnchor : marker.transform;
            }
            return null;
        }

        public string GetWinnerPerspectiveString(CityState city, string viewerPlayerId)
        {
            if (city == null || city.winnerSlot == PlayerSlot.None) return "NONE";
            PlayerSlot viewerSlot = PlayerIdentity.ToSlot(viewerPlayerId);
            return city.winnerSlot == viewerSlot ? "SELF" : "OPPONENT";
        }

        public void RefreshAllVisuals(CityId? activeRallyCity)
        {
            for (int i = 0; i < _cities.Count; i++)
            {
                CityState city = _cities[i];
                if (_hubMarkers.TryGetValue(city.cityId, out CityHubMarker marker) && marker != null)
                {
                    bool isActive = activeRallyCity.HasValue && activeRallyCity.Value == city.cityId;
                    marker.RefreshVisual(city, isActive);
                }
            }
        }
    }
}
