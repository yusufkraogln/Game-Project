using System.Collections.Generic;
using PoliticalMayhem.Core;
using UnityEngine;

namespace PoliticalMayhem.Rally
{
    /// <summary>
    /// Drives the NPC Crowd Reaction System in 3D space (CHEER, BOO, PROTEST, THROW_OBJECTS, MIXED)
    /// based on Speaker Overall alignment and Speech Evaluation AI scores.
    /// Also provides exaggerated comedic Balcony Speech confetti & cheering.
    /// </summary>
    public class CrowdReactionController : MonoBehaviour
    {
        [Header("Crowd Setup")]
        [SerializeField] private int _crowdNpcCount = 16;
        [SerializeField] private float _crowdRadius = 6.5f;

        private static readonly string[] CrowdModelPaths =
        {
            "Models/Crowd/crowd_male_1",
            "Models/Crowd/crowd_female_1",
            "Models/Crowd/crowd_male_2",
            "Models/Crowd/crowd_female_2",
        };
        private const string CrowdPaletteTexturePath = "Models/Crowd/crowd_palette";

        private readonly List<Transform> _crowdMembers = new List<Transform>();
        private readonly List<Renderer> _crowdRenderers = new List<Renderer>();
        private readonly List<Vector3> _baseLocalOffsets = new List<Vector3>();
        private readonly List<ThrownProp> _thrownProps = new List<ThrownProp>();
        private readonly List<ConfettiPiece> _confettiPieces = new List<ConfettiPiece>();

        private Transform _crowdRoot;
        private Vector3 _activeStageCenter = Vector3.zero;
        private CrowdReactionType _currentReaction = CrowdReactionType.MIXED;
        private float _sentimentScore = 50f;
        private float _propSpawnTimer;
        private float _confettiTimer;
        private bool _balconyCelebrationActive;
        private string _lastFeedback = "Kalabalık konuşmacıyı bekliyor.";

        private struct ThrownProp
        {
            public Transform transform;
            public Vector3 velocity;
            public float life;
        }

        private struct ConfettiPiece
        {
            public Transform transform;
            public Vector3 velocity;
            public float life;
        }

        public CrowdReactionType CurrentReaction => _currentReaction;
        public float SentimentScore => _sentimentScore;
        public string LastFeedback => _lastFeedback;
        public bool BalconyCelebrationActive => _balconyCelebrationActive;

        public void PrepareCrowdAtCity(CityId cityId, CityManager cityManager, OverallManager overallManager, string speakerId)
        {
            _balconyCelebrationActive = false;
            ClearTemporaryProps();

            _activeStageCenter = cityManager != null
                ? cityManager.GetCityWorldPosition(cityId)
                : CityCatalog.GetWorldPosition(cityId);

            EnsureCrowdCreated();
            _crowdRoot.position = _activeStageCenter;

            // Compute initial crowd sentiment from Speaker's Overall alignment with city demographics
            CityState city = cityManager != null ? cityManager.GetCity(cityId) : null;
            float alignment = (overallManager != null && city != null)
                ? overallManager.ComputeOverallAlignment(speakerId, city.ideologyDistribution)
                : 35f;

            _sentimentScore = Mathf.Clamp(25f + alignment * 0.75f, 10f, 95f);
            if (_sentimentScore >= 65f)
                _currentReaction = CrowdReactionType.CHEER;
            else if (_sentimentScore <= 30f)
                _currentReaction = CrowdReactionType.BOO;
            else
                _currentReaction = CrowdReactionType.MIXED;

            _lastFeedback = $"Başlangıç seyirci tepkisi ({CityCatalog.GetDisplayName(cityId)}): {_currentReaction} (Uyum: {alignment:F1})";
            ApplyCrowdColor();
        }

        public void UpdateLiveSentimentDuringSpeech(string speechText, IdeologyScoresDto cityDistribution, IdeologyScoresDto speakerOverall)
        {
            if (string.IsNullOrWhiteSpace(speechText)) return;

            // Lightweight live preview sentiment shift while speaking/typing before LLM final judgment
            string lower = speechText.ToLowerInvariant();
            float keywordBoost = 0f;
            if (cityDistribution != null)
            {
                IdeologyType dominant = cityDistribution.GetDominantIdeology();
                if (dominant == IdeologyType.Nationalist && (lower.Contains("vatan") || lower.Contains("millet") || lower.Contains("sınır") || lower.Contains("national")))
                    keywordBoost += 14f;
                if (dominant == IdeologyType.Liberal && (lower.Contains("piyasa") || lower.Contains("özgür") || lower.Contains("ticaret") || lower.Contains("market") || lower.Contains("trade")))
                    keywordBoost += 14f;
                if (dominant == IdeologyType.Democrat && (lower.Contains("demokrasi") || lower.Contains("hak") || lower.Contains("uzlaşı") || lower.Contains("democracy") || lower.Contains("rights")))
                    keywordBoost += 14f;
                if (dominant == IdeologyType.Communist && (lower.Contains("emek") || lower.Contains("işçi") || lower.Contains("sendika") || lower.Contains("labor") || lower.Contains("union")))
                    keywordBoost += 14f;
                if (dominant == IdeologyType.Imperialist && (lower.Contains("büyük") || lower.Contains("güç") || lower.Contains("proje") || lower.Contains("empire") || lower.Contains("power")))
                    keywordBoost += 14f;
            }

            float lengthBonus = Mathf.Min(12f, speechText.Length * 0.08f);
            _sentimentScore = Mathf.Clamp(42f + keywordBoost + lengthBonus, 10f, 95f);
            _currentReaction = _sentimentScore >= 62f ? CrowdReactionType.CHEER : CrowdReactionType.MIXED;
            ApplyCrowdColor();
        }

        public void ApplyPhysicalBrawlReaction()
        {
            _currentReaction = CrowdReactionType.PROTEST;
            _sentimentScore = Mathf.Max(5f, _sentimentScore - 30f);
            _lastFeedback = "Kalabalıkta arbede çıktı! Güvenlik ekibi sabotajcıyı kovalıyor!";
            ApplyCrowdColor();
        }

        public void ApplySpeechEvaluation(SpeechEvalResult eval, float booRecoveryBonus = 0f)
        {
            if (eval == null) return;

            _currentReaction = CrowdReactionCodec.Parse(eval.crowd_reaction);
            float rawSentiment = 0.55f * eval.speech_effectiveness + 0.45f * eval.persuasion_impact;
            if (_currentReaction == CrowdReactionType.BOO && booRecoveryBonus > 0f)
            {
                rawSentiment += booRecoveryBonus * 100f;
            }
            _sentimentScore = Mathf.Clamp(rawSentiment, 0f, 100f);
            _lastFeedback = string.IsNullOrWhiteSpace(eval.feedback_summary)
                ? $"Kalabalık tepkisi: {_currentReaction}"
                : eval.feedback_summary;

            ApplyCrowdColor();
        }

        public void TriggerBalconyCelebration(CityManager cityManager)
        {
            _activeStageCenter = cityManager != null
                ? cityManager.GetCityWorldPosition(CityId.Baskent)
                : CityCatalog.GetWorldPosition(CityId.Baskent);

            EnsureCrowdCreated();
            _crowdRoot.position = _activeStageCenter;
            _currentReaction = CrowdReactionType.CHEER;
            _sentimentScore = 100f;
            _balconyCelebrationActive = true;
            _lastFeedback = "Balkon Konuşması! Kalabalık çılgınca tezahürat yapıyor ve konfetiler yağıyor!";
            ApplyCrowdColor();
        }

        private void EnsureCrowdCreated()
        {
            if (_crowdRoot != null) return;
            SpawnCrowdAtCity();
        }

        private void SpawnCrowdAtCity()
        {
            GameObject root = new GameObject("RallyCrowdGroup");
            _crowdRoot = root.transform;

            // Arrange rally crowd in 2 clean staggered rows inside the open southern front courtyard
            // (X in [-4.35, +4.35], Z in [-7.4, -4.6], facing north toward the Capitol) -> 0 overlaps in all 6 cities.
            int frontRowCount = Mathf.Max(1, (_crowdNpcCount - 2) / 2);
            int backRowCount = Mathf.Max(1, _crowdNpcCount - frontRowCount);

            for (int i = 0; i < _crowdNpcCount; i++)
            {
                bool isFront = i < frontRowCount;
                int rowIdx = isFront ? i : (i - frontRowCount);
                int rowTotal = isFront ? frontRowCount : backRowCount;
                float t = rowTotal > 1 ? (rowIdx / (float)(rowTotal - 1)) : 0.5f;
                float arcDeg = isFront ? Mathf.Lerp(-120f, -60f, t) : Mathf.Lerp(-124f, -56f, t);
                float angle = arcDeg * Mathf.Deg2Rad;
                float radius = isFront ? 5.5f : 7.3f;

                Vector3 localPos = new Vector3(Mathf.Cos(angle) * radius, 0.08f, Mathf.Sin(angle) * radius);
                Vector3 inwardDir = new Vector3(-localPos.x, 0f, -localPos.z);
                float yaw = inwardDir.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(inwardDir.x, inwardDir.z) * Mathf.Rad2Deg
                    : 0f;

                float targetHeight = 1.00f + (i % 3) * 0.04f;
                string modelPath = CrowdModelPaths[i % CrowdModelPaths.Length];

                GameObject npc = ModelVisualHelper.InstantiateNormalizedModel(
                    modelPath,
                    CrowdPaletteTexturePath,
                    $"CrowdNPC_{i}",
                    _crowdRoot,
                    localPos,
                    yaw,
                    targetHeight,
                    normalizeByHeight: true,
                    groundLocalY: 0.08f,
                    tint: Color.white,
                    smoothness: 0.20f,
                    stripColliders: true);

                if (npc != null)
                {
                    AttachMoodIndicators(npc.transform, addPennant: (i % 2) == 0);
                }
                else
                {
                    npc = CreateFallbackCrowdPrimitive(i, localPos, yaw);
                }

                _crowdMembers.Add(npc.transform);
                _baseLocalOffsets.Add(localPos);
            }
        }

        private void AttachMoodIndicators(Transform npcRoot, bool addPennant)
        {
            // Ground mood aura disc scaled proportionally to 1.04u human NPCs
            GameObject aura = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aura.name = "MoodAuraRing";
            Destroy(aura.GetComponent<Collider>());
            aura.transform.SetParent(npcRoot, false);
            aura.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            aura.transform.localScale = new Vector3(0.52f, 0.012f, 0.52f);
            Renderer auraRend = aura.GetComponent<Renderer>();
            if (auraRend != null)
                _crowdRenderers.Add(auraRend);

            if (!addPennant) return;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = "RallyPennantPole";
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(npcRoot, false);
            pole.transform.localPosition = new Vector3(0.22f, 0.78f, 0.08f);
            pole.transform.localScale = new Vector3(0.022f, 0.58f, 0.022f);
            MaterialHelper.ApplyColor(pole.GetComponent<Renderer>(), new Color(0.28f, 0.24f, 0.20f), 0.15f);

            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "RallyPennantFlag";
            Destroy(flag.GetComponent<Collider>());
            flag.transform.SetParent(npcRoot, false);
            flag.transform.localPosition = new Vector3(0.22f, 1.08f, 0.08f);
            flag.transform.localScale = new Vector3(0.20f, 0.14f, 0.022f);
            Renderer flagRend = flag.GetComponent<Renderer>();
            if (flagRend != null)
                _crowdRenderers.Add(flagRend);
        }

        private GameObject CreateFallbackCrowdPrimitive(int index, Vector3 localPos, float yaw)
        {
            GameObject npc = new GameObject($"CrowdNPC_{index}");
            npc.transform.SetParent(_crowdRoot, false);
            npc.transform.localPosition = localPos;
            npc.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(npc.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            body.transform.localScale = new Vector3(0.46f, 0.55f, 0.46f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(body.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            head.transform.localScale = new Vector3(0.75f, 0.65f, 0.75f);
            MaterialHelper.ApplyColor(head.GetComponent<Renderer>(), new Color(0.93f, 0.78f, 0.64f), 0.2f);

            Renderer bodyRend = body.GetComponent<Renderer>();
            if (bodyRend != null)
                _crowdRenderers.Add(bodyRend);

            return npc;
        }

        private void ApplyCrowdColor()
        {
            Color c;
            switch (_currentReaction)
            {
                case CrowdReactionType.CHEER: c = new Color(0.25f, 0.92f, 0.42f); break;
                case CrowdReactionType.BOO: c = new Color(0.95f, 0.55f, 0.15f); break;
                case CrowdReactionType.PROTEST: c = new Color(0.92f, 0.22f, 0.22f); break;
                case CrowdReactionType.THROW_OBJECTS: c = new Color(0.85f, 0.12f, 0.45f); break;
                default: c = new Color(0.82f, 0.82f, 0.78f); break;
            }

            for (int i = 0; i < _crowdRenderers.Count; i++)
            {
                if (_crowdRenderers[i] != null)
                    MaterialHelper.ApplyColor(_crowdRenderers[i], c, 0.25f);
            }
        }

        private void Update()
        {
            float t = Time.time;

            for (int i = 0; i < _crowdMembers.Count; i++)
            {
                Transform tf = _crowdMembers[i];
                if (tf == null) continue;
                Vector3 basePos = _baseLocalOffsets[i];

                if (_currentReaction == CrowdReactionType.CHEER)
                {
                    float bounce = Mathf.Abs(Mathf.Sin(t * 7.5f + i * 0.7f)) * (_balconyCelebrationActive ? 1.1f : 0.65f);
                    tf.localPosition = basePos + new Vector3(0f, bounce, 0f);
                }
                else if (_currentReaction == CrowdReactionType.BOO || _currentReaction == CrowdReactionType.PROTEST || _currentReaction == CrowdReactionType.THROW_OBJECTS)
                {
                    float shake = Mathf.Sin(t * 14f + i * 1.1f) * 0.25f;
                    tf.localPosition = basePos + new Vector3(shake, 0f, -shake * 0.5f);
                }
                else
                {
                    float sway = Mathf.Abs(Mathf.Sin(t * 2.5f + i * 0.5f)) * 0.10f;
                    tf.localPosition = basePos + new Vector3(0f, sway, 0f);
                }
            }

            // Comedic prop throwing (tomatoes/shoes) during THROW_OBJECTS reaction
            if (_currentReaction == CrowdReactionType.THROW_OBJECTS && _crowdMembers.Count > 0)
            {
                _propSpawnTimer += Time.deltaTime;
                if (_propSpawnTimer >= 0.45f)
                {
                    _propSpawnTimer = 0f;
                    SpawnThrownProp();
                }
            }

            // Balcony Speech confetti celebration
            if (_balconyCelebrationActive)
            {
                _confettiTimer += Time.deltaTime;
                if (_confettiTimer >= 0.12f)
                {
                    _confettiTimer = 0f;
                    SpawnConfettiPiece();
                }
            }

            UpdateThrownProps();
            UpdateConfetti();
        }

        private void SpawnThrownProp()
        {
            int idx = Random.Range(0, _crowdMembers.Count);
            if (_crowdMembers[idx] == null) return;

            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prop.name = "ComicProp";
            Destroy(prop.GetComponent<Collider>());
            prop.transform.position = _crowdMembers[idx].position + Vector3.up * 1.35f;
            prop.transform.localScale = Vector3.one * 0.35f;
            MaterialHelper.ApplyColor(prop.GetComponent<Renderer>(), Random.value > 0.5f ? new Color(0.90f, 0.18f, 0.15f) : new Color(0.45f, 0.28f, 0.12f), 0.3f);

            Vector3 toCenter = (_activeStageCenter + Vector3.up * 1.5f - prop.transform.position).normalized;
            _thrownProps.Add(new ThrownProp
            {
                transform = prop.transform,
                velocity = toCenter * 9f + Vector3.up * 5.5f,
                life = 1.6f
            });
        }

        private void SpawnConfettiPiece()
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "BalconyConfetti";
            Destroy(piece.GetComponent<Collider>());
            piece.transform.position = _activeStageCenter + new Vector3(Random.Range(-6f, 6f), 8f, Random.Range(-6f, 6f));
            piece.transform.localScale = new Vector3(0.25f, 0.06f, 0.25f);
            MaterialHelper.ApplyColor(piece.GetComponent<Renderer>(), new Color(Random.Range(0.25f, 1f), Random.Range(0.25f, 1f), Random.Range(0.25f, 1f)), 0.5f);

            _confettiPieces.Add(new ConfettiPiece
            {
                transform = piece.transform,
                velocity = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-4.5f, -2.0f), Random.Range(-1.5f, 1.5f)),
                life = 2.6f
            });
        }

        private void UpdateThrownProps()
        {
            for (int i = _thrownProps.Count - 1; i >= 0; i--)
            {
                ThrownProp p = _thrownProps[i];
                p.life -= Time.deltaTime;
                if (p.transform == null || p.life <= 0f)
                {
                    if (p.transform != null) Destroy(p.transform.gameObject);
                    _thrownProps.RemoveAt(i);
                    continue;
                }
                p.velocity += Vector3.down * 14f * Time.deltaTime;
                p.transform.position += p.velocity * Time.deltaTime;
                _thrownProps[i] = p;
            }
        }

        private void UpdateConfetti()
        {
            for (int i = _confettiPieces.Count - 1; i >= 0; i--)
            {
                ConfettiPiece c = _confettiPieces[i];
                c.life -= Time.deltaTime;
                if (c.transform == null || c.life <= 0f)
                {
                    if (c.transform != null) Destroy(c.transform.gameObject);
                    _confettiPieces.RemoveAt(i);
                    continue;
                }
                c.transform.position += c.velocity * Time.deltaTime;
                c.transform.Rotate(120f * Time.deltaTime, 180f * Time.deltaTime, 90f * Time.deltaTime);
                _confettiPieces[i] = c;
            }
        }

        private void ClearTemporaryProps()
        {
            for (int i = 0; i < _thrownProps.Count; i++)
                if (_thrownProps[i].transform != null) Destroy(_thrownProps[i].transform.gameObject);
            _thrownProps.Clear();

            for (int i = 0; i < _confettiPieces.Count; i++)
                if (_confettiPieces[i].transform != null) Destroy(_confettiPieces[i].transform.gameObject);
            _confettiPieces.Clear();
        }
    }
}
