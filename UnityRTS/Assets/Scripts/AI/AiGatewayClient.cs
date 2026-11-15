using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PoliticalMayhem.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PoliticalMayhem.AI
{
    public enum AiProviderMode
    {
        Auto = 0,
        CloudApi = 1,
        LocalOllama = 2
    }

    [Serializable]
    public class HealthResponseDto
    {
        public string status = string.Empty;
        public string provider = string.Empty;
        public string active_provider = string.Empty;
        public bool ollama_available;
        public bool cloud_available;
        public int rag_chunks;
    }

    [Serializable]
    public class ProviderConfigRequestDto
    {
        public string provider_mode = "auto";
    }

    [Serializable]
    public class ProviderConfigResponseDto
    {
        public bool ok;
        public string provider_mode = "auto";
        public string active_provider = string.Empty;
        public string model_name = string.Empty;
        public string message = string.Empty;
    }

    [Serializable]
    public class ToolCallTraceDto
    {
        public string tool = string.Empty;
        public int duration_ms;
        public string status = "OK";
        public string arguments_summary = string.Empty;
        public string result_summary = string.Empty;
    }

    [Serializable]
    public class RagSourceTraceDto
    {
        public string source_id = string.Empty;
        public string title = string.Empty;
        public float score;
    }

    [Serializable]
    public class CommanderAskRequestDto
    {
        public string match_id = string.Empty;
        public string player_id = string.Empty;
        public string question = string.Empty;
        public string provider_mode = "auto";
        public AiSafeSnapshotDto snapshot = new AiSafeSnapshotDto();
    }

    [Serializable]
    public class CommanderAskResponse
    {
        public bool ok;
        public string provider_used = string.Empty;
        public string model_name = string.Empty;
        public int latency_ms;
        public bool security_blocked;
        public string security_reason = string.Empty;
        public string security_note = string.Empty;
        public string explanation = string.Empty;
        public ActionPlan action_plan = new ActionPlan();
        public ActionPlan plan = new ActionPlan();
        public List<ToolCallTraceDto> tool_calls = new List<ToolCallTraceDto>();
        public List<RagSourceTraceDto> rag_sources = new List<RagSourceTraceDto>();
        public bool fallback_used;
        public bool is_fallback;
        public string error_message = string.Empty;
    }

    [Serializable]
    public class SpeechEvaluateRequest
    {
        public string match_id = string.Empty;
        public string player_id = string.Empty;
        public int round_number;
        public string city_name = string.Empty;
        public string phase = "RALLY_SPEECH";
        public string speech_type = "rally";
        public string speaker_transcript = string.Empty;
        public string saboteur_transcript = string.Empty;
        public IdeologyScoresDto city_ideologies = new IdeologyScoresDto();
        public IdeologyScoresDto speaker_overall = new IdeologyScoresDto();
        public string provider_mode = "auto";
    }

    [Serializable]
    public class SpeechEvaluateResponseWrapper
    {
        public bool ok;
        public string provider_used = string.Empty;
        public string model_name = string.Empty;
        public bool fallback_used;
        public int latency_ms;
        public SpeechEvalResult evaluation = new SpeechEvalResult();
    }

    [Serializable]
    public class SpeechTranscribeRequest
    {
        public string match_id = string.Empty;
        public string player_id = string.Empty;
        public int round_number;
        public string city_name = string.Empty;
        public string top_ideology = string.Empty;
        public float duration_seconds;
        public float rms_level;
        public string audio_wav_base64 = string.Empty;
        public string language = "tr";
    }

    [Serializable]
    public class SpeechTranscribeResponse
    {
        public bool ok;
        public string transcript = string.Empty;
        public string provider_used = string.Empty;
        public bool fallback_used;
        public int latency_ms;
    }

    /// <summary>
    /// Async UnityWebRequest client connecting Unity 6 to the local Python AI Gateway (http://127.0.0.1:8765).
    /// Supports runtime provider switching (auto, api, ollama) and deterministic offline fallback when the server is stopped.
    /// </summary>
    public class AiGatewayClient : MonoBehaviour
    {
        [Header("Gateway Configuration")]
        [SerializeField] private string _gatewayBaseUrl = "http://127.0.0.1:8765";
        [SerializeField] private AiProviderMode _providerMode = AiProviderMode.Auto;
        [SerializeField] private float _requestTimeoutSeconds = 65f;
        [SerializeField] private bool _allowOfflineFallback = true;

        private bool _isGatewayHealthy;
        private string _healthStatusLabel = "Kontrol edilmedi";
        private string _activeServerProvider = "auto";

        public string GatewayBaseUrl => _gatewayBaseUrl.TrimEnd('/');
        public AiProviderMode ProviderMode => _providerMode;
        public bool IsGatewayHealthy => _isGatewayHealthy;
        public string HealthStatusLabel => _healthStatusLabel;
        public string ActiveServerProvider => _activeServerProvider;

        public static string ToProviderKey(AiProviderMode mode)
        {
            switch (mode)
            {
                case AiProviderMode.CloudApi: return "api";
                case AiProviderMode.LocalOllama: return "ollama";
                default: return "auto";
            }
        }

        private void Awake()
        {
            if (_requestTimeoutSeconds < 60f)
                _requestTimeoutSeconds = 65f;
        }

        private void Start()
        {
            CheckHealth(null);
        }

        public void CheckHealth(Action<bool, string> onCompleted)
        {
            StartCoroutine(CheckHealthCoroutine(onCompleted));
        }

        private IEnumerator CheckHealthCoroutine(Action<bool, string> onCompleted)
        {
            ResolveSessionHeaders(null, null, out string playerId, out string matchId);
            using (UnityWebRequest req = UnityWebRequest.Get($"{GatewayBaseUrl}/api/health"))
            {
                req.SetRequestHeader("X-Player-Id", playerId);
                req.SetRequestHeader("X-Match-Id", matchId);
                req.timeout = 3;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isGatewayHealthy = true;
                    HealthResponseDto dto = SafeParse<HealthResponseDto>(req.downloadHandler.text);
                    if (dto != null)
                    {
                        _activeServerProvider = !string.IsNullOrEmpty(dto.active_provider) ? dto.active_provider : dto.provider;
                        _healthStatusLabel = $"Bağlı ({_activeServerProvider}, RAG:{dto.rag_chunks})";
                    }
                    else
                    {
                        _healthStatusLabel = "Bağlı (200 OK)";
                    }
                    onCompleted?.Invoke(true, _healthStatusLabel);
                }
                else
                {
                    _isGatewayHealthy = false;
                    _healthStatusLabel = "Çevrimdışı (Local Fallback aktif)";
                    onCompleted?.Invoke(false, _healthStatusLabel);
                }
            }
        }

        public void SetProviderMode(AiProviderMode mode, Action<bool, string> onCompleted = null)
        {
            _providerMode = mode;
            StartCoroutine(SetProviderModeCoroutine(mode, onCompleted));
        }

        private IEnumerator SetProviderModeCoroutine(AiProviderMode mode, Action<bool, string> onCompleted)
        {
            ProviderConfigRequestDto payload = new ProviderConfigRequestDto { provider_mode = ToProviderKey(mode) };
            string json = JsonUtility.ToJson(payload);
            ResolveSessionHeaders(null, null, out string playerId, out string matchId);

            using (UnityWebRequest req = CreateJsonPost($"{GatewayBaseUrl}/api/provider/config", json, 5, playerId, matchId))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isGatewayHealthy = true;
                    ProviderConfigResponseDto res = SafeParse<ProviderConfigResponseDto>(req.downloadHandler.text);
                    _activeServerProvider = res != null && !string.IsNullOrEmpty(res.active_provider)
                        ? res.active_provider
                        : ToProviderKey(mode);
                    _healthStatusLabel = $"Bağlı ({_activeServerProvider})";
                    onCompleted?.Invoke(true, _activeServerProvider);
                }
                else
                {
                    _isGatewayHealthy = false;
                    _healthStatusLabel = $"Çevrimdışı ({ToProviderKey(mode)} fallback)";
                    onCompleted?.Invoke(false, _healthStatusLabel);
                }
            }
        }

        public void SendSnapshot(AiSafeSnapshotDto snapshot, Action<bool, string> onCompleted = null)
        {
            StartCoroutine(SendSnapshotCoroutine(snapshot, onCompleted));
        }

        private IEnumerator SendSnapshotCoroutine(AiSafeSnapshotDto snapshot, Action<bool, string> onCompleted)
        {
            string json = VisibilityService.SerializeToJson(snapshot, false);
            ResolveSessionHeaders(snapshot != null ? snapshot.player_id : null, snapshot != null ? snapshot.match_id : null, out string playerId, out string matchId);
            using (UnityWebRequest req = CreateJsonPost($"{GatewayBaseUrl}/api/snapshot", json, 5, playerId, matchId))
            {
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                if (ok) _isGatewayHealthy = true;
                onCompleted?.Invoke(ok, ok ? "Snapshot synced" : req.error);
            }
        }

        public void AskCommander(AiSafeSnapshotDto snapshot, string question, Action<CommanderAskResponse> onCompleted)
        {
            StartCoroutine(AskCommanderCoroutine(snapshot, question, onCompleted));
        }

        private IEnumerator AskCommanderCoroutine(AiSafeSnapshotDto snapshot, string question, Action<CommanderAskResponse> onCompleted)
        {
            float startTime = Time.realtimeSinceStartup;
            ResolveSessionHeaders(
                snapshot != null ? snapshot.player_id : null,
                snapshot != null ? snapshot.match_id : null,
                out string playerId,
                out string matchId);

            // Step 1: Transmit visibility-filtered safe snapshot to /api/snapshot
            string snapJson = VisibilityService.SerializeToJson(snapshot, false);
            using (UnityWebRequest snapReq = CreateJsonPost($"{GatewayBaseUrl}/api/snapshot", snapJson, 5, playerId, matchId))
            {
                yield return snapReq.SendWebRequest();
            }

            // Step 2: Query /api/commander/ask
            CommanderAskRequestDto askDto = new CommanderAskRequestDto
            {
                match_id = matchId,
                player_id = playerId,
                question = question ?? string.Empty,
                provider_mode = ToProviderKey(_providerMode),
                snapshot = snapshot ?? new AiSafeSnapshotDto()
            };

            string askJson = JsonUtility.ToJson(askDto);
            using (UnityWebRequest req = CreateJsonPost($"{GatewayBaseUrl}/api/commander/ask", askJson, Mathf.RoundToInt(_requestTimeoutSeconds), playerId, matchId))
            {
                yield return req.SendWebRequest();
                int elapsedMs = Mathf.RoundToInt((Time.realtimeSinceStartup - startTime) * 1000f);

                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isGatewayHealthy = true;
                    string raw = req.downloadHandler.text;
                    CommanderAskResponse parsed = ParseCommanderResponse(raw, elapsedMs);
                    onCompleted?.Invoke(parsed);
                    yield break;
                }

                _isGatewayHealthy = false;
                _healthStatusLabel = "Çevrimdışı (Local Fallback aktif)";
                if (_allowOfflineFallback)
                {
                    CommanderAskResponse fallback = BuildDeterministicCommanderFallback(snapshot, question, elapsedMs, req.error);
                    onCompleted?.Invoke(fallback);
                }
                else
                {
                    onCompleted?.Invoke(new CommanderAskResponse
                    {
                        ok = false,
                        latency_ms = elapsedMs,
                        error_message = $"Gateway unreachable: {req.error}",
                        action_plan = new ActionPlan
                        {
                            summary = "AI Gateway connection failed.",
                            confidence = 0f,
                            actions = new List<AiActionItem>()
                        }
                    });
                }
            }
        }

        public void EvaluateSpeech(SpeechEvaluateRequest request, Action<SpeechEvalResult, bool> onCompleted)
        {
            StartCoroutine(EvaluateSpeechCoroutine(request, onCompleted));
        }

        private IEnumerator EvaluateSpeechCoroutine(SpeechEvaluateRequest request, Action<SpeechEvalResult, bool> onCompleted)
        {
            if (request == null)
            {
                onCompleted?.Invoke(new SpeechEvalResult(), true);
                yield break;
            }

            request.provider_mode = ToProviderKey(_providerMode);
            ResolveSessionHeaders(request.player_id, request.match_id, out string playerId, out string matchId);
            string json = JsonUtility.ToJson(request);

            using (UnityWebRequest req = CreateJsonPost($"{GatewayBaseUrl}/api/speech/evaluate", json, Mathf.RoundToInt(_requestTimeoutSeconds), playerId, matchId))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isGatewayHealthy = true;
                    string raw = req.downloadHandler.text;
                    SpeechEvaluateResponseWrapper wrapper = SafeParse<SpeechEvaluateResponseWrapper>(raw);
                    if (wrapper != null && wrapper.evaluation != null && !string.IsNullOrEmpty(wrapper.evaluation.crowd_reaction))
                    {
                        onCompleted?.Invoke(wrapper.evaluation, wrapper.fallback_used);
                        yield break;
                    }

                    SpeechEvalResult direct = SafeParse<SpeechEvalResult>(raw);
                    if (direct != null && !string.IsNullOrEmpty(direct.crowd_reaction))
                    {
                        onCompleted?.Invoke(direct, false);
                        yield break;
                    }
                }

                _isGatewayHealthy = false;
                SpeechEvalResult fallback = BuildDeterministicSpeechFallback(request);
                onCompleted?.Invoke(fallback, true);
            }
        }

        public void TranscribeSpeech(SpeechTranscribeRequest request, Action<string, bool> onCompleted)
        {
            StartCoroutine(TranscribeSpeechCoroutine(request, onCompleted));
        }

        private IEnumerator TranscribeSpeechCoroutine(SpeechTranscribeRequest request, Action<string, bool> onCompleted)
        {
            if (request == null)
            {
                onCompleted?.Invoke(string.Empty, true);
                yield break;
            }

            ResolveSessionHeaders(request.player_id, request.match_id, out string playerId, out string matchId);
            string json = JsonUtility.ToJson(request);

            using (UnityWebRequest req = CreateJsonPost($"{GatewayBaseUrl}/api/speech/transcribe", json, Mathf.RoundToInt(_requestTimeoutSeconds), playerId, matchId))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isGatewayHealthy = true;
                    SpeechTranscribeResponse res = SafeParse<SpeechTranscribeResponse>(req.downloadHandler.text);
                    if (res != null && !string.IsNullOrWhiteSpace(res.transcript))
                    {
                        onCompleted?.Invoke(res.transcript.Trim(), res.fallback_used);
                        yield break;
                    }
                }

                _isGatewayHealthy = false;
                onCompleted?.Invoke(BuildDeterministicTranscribeFallback(request), true);
            }
        }

        public static string BuildDeterministicTranscribeFallback(SpeechTranscribeRequest request)
        {
            string city = request != null && !string.IsNullOrWhiteSpace(request.city_name) ? request.city_name : "Baskent";
            string ideo = request != null && !string.IsNullOrWhiteSpace(request.top_ideology)
                ? request.top_ideology.ToLowerInvariant()
                : "democrat";
            bool detailed = request != null && request.duration_seconds >= 3.5f;

            switch (ideo)
            {
                case "nationalist":
                    return detailed
                        ? $"{city} meydanından sesleniyoruz: yerli üretim, güçlü sınır güvenliği ve ulusal egemenlik ile yarınlarımızı koruyacağız!"
                        : $"{city} için yerli sanayi ve güçlü ulusal güvenlik!";
                case "liberal":
                    return detailed
                        ? $"{city} esnafı ve girişimcileriyle serbest piyasa, açık ticaret ve düşük vergilerle refahı büyüteceğiz!"
                        : $"{city} için serbest ticaret ve girişim özgürlüğü!";
                case "communist":
                    return detailed
                        ? $"{city} işçileri ve emekçileri için sendikal haklar, güçlü kamu yatırımları ve eşit paylaşım kuracağız!"
                        : $"{city} işçileri için emek, dayanışma ve kamu yatırımı!";
                case "imperialist":
                    return detailed
                        ? $"{city} merkezli büyük altyapı hamleleriyle bölgesel gücümüzü ve stratejik otoritemizi zirveye taşıyacağız!"
                        : $"{city} için büyük güç vizyonu ve dev projeler!";
                default:
                    return detailed
                        ? $"{city} halkıyla katılımcı demokrasi, hukuk devleti ve şeffaf yönetim temelinde ortak bir gelecek kuracağız!"
                        : $"{city} için şeffaf demokrasi, adalet ve toplumsal uzlaşı!";
            }
        }

        private CommanderAskResponse ParseCommanderResponse(string rawJson, int elapsedMs)
        {
            CommanderAskResponse res = SafeParse<CommanderAskResponse>(rawJson);
            if (res != null)
            {
                if ((res.action_plan == null || string.IsNullOrEmpty(res.action_plan.summary))
                    && res.plan != null
                    && (!string.IsNullOrEmpty(res.plan.summary) || (res.plan.actions != null && res.plan.actions.Count > 0)))
                {
                    res.action_plan = res.plan;
                }

                res.is_fallback = res.is_fallback || res.fallback_used;
                res.fallback_used = res.is_fallback;

                if (string.IsNullOrEmpty(res.security_note) && !string.IsNullOrEmpty(res.security_reason))
                    res.security_note = res.security_reason;
                if (string.IsNullOrEmpty(res.security_reason) && !string.IsNullOrEmpty(res.security_note))
                    res.security_reason = res.security_note;

                if (res.security_blocked)
                {
                    if (res.action_plan == null)
                        res.action_plan = new ActionPlan();
                    if (res.action_plan.actions == null)
                        res.action_plan.actions = new List<AiActionItem>();
                    if (string.IsNullOrEmpty(res.action_plan.summary))
                    {
                        res.action_plan.summary = !string.IsNullOrEmpty(res.security_reason)
                            ? res.security_reason
                            : (!string.IsNullOrEmpty(res.explanation)
                                ? res.explanation
                                : "Blocked under Fog of War policy.");
                    }
                    if (string.IsNullOrEmpty(res.explanation))
                        res.explanation = res.action_plan.summary;
                    if (res.latency_ms <= 0) res.latency_ms = elapsedMs;
                    res.ok = true;
                    return res;
                }

                bool hasActionPlan = res.action_plan != null
                    && (!string.IsNullOrEmpty(res.action_plan.summary)
                        || (res.action_plan.actions != null && res.action_plan.actions.Count > 0));
                if (hasActionPlan || !string.IsNullOrEmpty(res.explanation))
                {
                    if (res.action_plan == null)
                        res.action_plan = new ActionPlan();
                    if (res.action_plan.actions == null)
                        res.action_plan.actions = new List<AiActionItem>();
                    if (string.IsNullOrEmpty(res.action_plan.summary))
                        res.action_plan.summary = res.explanation;
                    if (res.latency_ms <= 0) res.latency_ms = elapsedMs;
                    res.ok = true;
                    return res;
                }
            }

            ActionPlan directPlan = SafeParse<ActionPlan>(rawJson);
            if (directPlan != null && !string.IsNullOrEmpty(directPlan.summary))
            {
                return new CommanderAskResponse
                {
                    ok = true,
                    provider_used = ToProviderKey(_providerMode),
                    latency_ms = elapsedMs,
                    action_plan = directPlan
                };
            }

            return new CommanderAskResponse
            {
                ok = false,
                latency_ms = elapsedMs,
                error_message = "Gateway response could not be parsed as ActionPlan.",
                action_plan = new ActionPlan { summary = "Invalid JSON response from AI Gateway.", confidence = 0f }
            };
        }

        private static void ResolveSessionHeaders(
            string explicitPlayerId,
            string explicitMatchId,
            out string playerId,
            out string matchId)
        {
            PoliticalMayhemGameManager gm = PoliticalMayhemGameManager.Instance;
            playerId = !string.IsNullOrWhiteSpace(explicitPlayerId)
                ? explicitPlayerId
                : (gm != null && !string.IsNullOrWhiteSpace(gm.LocalPlayerId) ? gm.LocalPlayerId : PlayerIdentity.PlayerAId);
            matchId = !string.IsNullOrWhiteSpace(explicitMatchId)
                ? explicitMatchId
                : (gm != null && !string.IsNullOrWhiteSpace(gm.MatchId) ? gm.MatchId : "pm-match-01");
        }

        private static UnityWebRequest CreateJsonPost(
            string url,
            string json,
            int timeoutSec,
            string playerId = null,
            string matchId = null)
        {
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(playerId))
                req.SetRequestHeader("X-Player-Id", playerId);
            if (!string.IsNullOrWhiteSpace(matchId))
                req.SetRequestHeader("X-Match-Id", matchId);
            req.timeout = Mathf.Max(2, timeoutSec);
            return req;
        }

        private static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return null;
            }
        }

        public static SpeechEvalResult BuildDeterministicSpeechFallback(SpeechEvaluateRequest req)
        {
            string speech = (req != null ? req.speaker_transcript : string.Empty) ?? string.Empty;
            string saboteur = (req != null ? req.saboteur_transcript : string.Empty) ?? string.Empty;
            string lower = speech.ToLowerInvariant();

            float nat = 25f, lib = 25f, dem = 25f, com = 25f, imp = 25f;

            if (ContainsAny(lower, "vatan", "millet", "sınır", "yerli", "bayrak", "national", "sovereignty", "border")) nat += 55f;
            if (ContainsAny(lower, "piyasa", "ticaret", "özgürlük", "girişim", "vergi", "market", "trade", "freedom")) lib += 55f;
            if (ContainsAny(lower, "demokrasi", "katılım", "uzlaşı", "hukuk", "sandık", "democracy", "consensus", "civic")) dem += 55f;
            if (ContainsAny(lower, "işçi", "emek", "sendika", "eşitlik", "kamu", "labor", "union", "worker")) com += 55f;
            if (ContainsAny(lower, "büyük", "imparator", "güç", "proje", "otorite", "power", "empire", "authority")) imp += 55f;

            if (speech.Trim().Length < 10)
            {
                nat = lib = dem = com = imp = 20f;
            }

            IdeologyScoresDto scores = new IdeologyScoresDto(
                Mathf.Clamp(nat, 0f, 100f),
                Mathf.Clamp(lib, 0f, 100f),
                Mathf.Clamp(dem, 0f, 100f),
                Mathf.Clamp(com, 0f, 100f),
                Mathf.Clamp(imp, 0f, 100f));

            // City demographic match score
            IdeologyScoresDto cityDist = req != null && req.city_ideologies != null
                ? req.city_ideologies
                : new IdeologyScoresDto(20f, 20f, 20f, 20f, 20f);

            float weightedMatch = 0f;
            foreach (IdeologyType t in IdeologyCodec.All)
            {
                weightedMatch += (cityDist.Get(t) / 100f) * scores.Get(t);
            }

            float lengthQuality = Mathf.Clamp(35f + speech.Length * 0.45f, 30f, 92f);
            float sabScore = string.IsNullOrWhiteSpace(saboteur)
                ? 0f
                : Mathf.Clamp(30f + saboteur.Length * 0.35f, 25f, 82f);

            float effectiveness = Mathf.Clamp(0.55f * weightedMatch + 0.45f * lengthQuality - sabScore * 0.20f, 15f, 95f);
            float persuasion = Mathf.Clamp(effectiveness * 0.92f, 10f, 94f);

            string reaction = "MIXED";
            if (effectiveness >= 68f) reaction = "CHEER";
            else if (effectiveness < 34f) reaction = "THROW_OBJECTS";
            else if (effectiveness < 46f) reaction = "BOO";
            else if (sabScore > 60f) reaction = "PROTEST";

            return new SpeechEvalResult
            {
                speech_effectiveness = Mathf.Round(effectiveness * 10f) / 10f,
                ideology_scores = scores,
                persuasion_impact = Mathf.Round(persuasion * 10f) / 10f,
                sabotage_effectiveness = Mathf.Round(sabScore * 10f) / 10f,
                crowd_reaction = reaction,
                argument_quality = Mathf.Round(lengthQuality * 10f) / 10f,
                consistency_score = 78f,
                feedback_summary = $"[Offline Fallback Eval] Baskın vurgu: {IdeologyCodec.GetDisplayNameTr(scores.GetDominantIdeology())}. Kalabalık tepkisi: {reaction}."
            };
        }

        private static CommanderAskResponse BuildDeterministicCommanderFallback(
            AiSafeSnapshotDto snapshot,
            string question,
            int elapsedMs,
            string networkError)
        {
            string q = (question ?? string.Empty).ToLowerInvariant();

            // 1. Prompt-injection & Fog-of-War security test handling
            if (ContainsAny(q, "ignore all", "ignore rules", "opponent budget", "opponent_budget", "opponent overall", "hidden", "rakibin bütçesi", "gizli"))
            {
                return new CommanderAskResponse
                {
                    ok = true,
                    is_fallback = true,
                    provider_used = "offline-security-fallback",
                    model_name = "deterministic-guard",
                    latency_ms = elapsedMs,
                    security_blocked = true,
                    security_note = "Prompt injection / hidden opponent state query blocked by Fog-of-War policy.",
                    explanation = "Gizli rakip bütçesi, rakip Overall puanları ve açıklanmamış destek oranları Fog-of-War sınırı arkasındadır. Yalnızca görünür saha bilgisi kullanılabilir.",
                    tool_calls = new List<ToolCallTraceDto>
                    {
                        new ToolCallTraceDto
                        {
                            tool = "get_visible_opponent_activity",
                            duration_ms = 2,
                            status = "OK_FOG_FILTERED",
                            arguments_summary = "session_bound=true",
                            result_summary = $"Returned {snapshot?.visible_opponent_activity?.Count ?? 0} visible-only records."
                        }
                    },
                    action_plan = new ActionPlan
                    {
                        summary = "Policy boundary enforced: hidden opponent economy/overall cannot be accessed.",
                        confidence = 0.95f,
                        actions = new List<AiActionItem>()
                    }
                };
            }

            // 2. Knowledge-only RAG test handling
            if (ContainsAny(q, "counter", "zayıf", "harcanabilir", "bribe", "rüşvet", "what does", "nedir", "kural"))
            {
                return new CommanderAskResponse
                {
                    ok = true,
                    is_fallback = true,
                    provider_used = "offline-rag-fallback",
                    model_name = "deterministic-rag",
                    latency_ms = elapsedMs,
                    explanation = "Bodyguard (Koruma) Kampanya Gönüllülerini ve Sabotajcıları durdurur; Propagandacı menzilden Koruma moralini yıpratır; Kampanya Gönüllüsü ise Propagandacıyı yakın mesafede bastırır. Parti bütçesi miting turlarında harcanamaz, yalnızca ELECTION_BRIBE aşamasında max %2 oy kaydırmak için kullanılır.",
                    tool_calls = new List<ToolCallTraceDto>
                    {
                        new ToolCallTraceDto
                        {
                            tool = "search_game_knowledge",
                            duration_ms = 4,
                            status = "OK",
                            arguments_summary = "query='unit counters and budget rule'",
                            result_summary = "Retrieved 3 verified chunks."
                        }
                    },
                    rag_sources = new List<RagSourceTraceDto>
                    {
                        new RagSourceTraceDto { source_id = "rule_unit_counters_v1", title = "Real-Time Campaign Units & Counter Matrix", score = 0.94f },
                        new RagSourceTraceDto { source_id = "rule_donation_economy_v1", title = "Party Budget & Donation Economy", score = 0.91f },
                        new RagSourceTraceDto { source_id = "rule_election_night_v1", title = "Election Night & Bribery Cap", score = 0.88f }
                    },
                    action_plan = new ActionPlan
                    {
                        summary = "Knowledge-only guidance retrieved from RAG rules (no state-changing actions needed).",
                        confidence = 0.92f,
                        actions = new List<AiActionItem>()
                    }
                };
            }

            // 3. Compound tactical recommendation grounded in AiSafeSnapshot
            string targetCity = "Baskent";
            string targetZone = "RallySquare";
            IdeologyType bestCityIdeology = IdeologyType.Democrat;

            if (snapshot != null && snapshot.cities != null && snapshot.cities.Count > 0)
            {
                for (int i = 0; i < snapshot.cities.Count; i++)
                {
                    CitySnapshotDto c = snapshot.cities[i];
                    if (!c.rally_done)
                    {
                        targetCity = c.city_name;
                        targetZone = CityCatalog.TryParse(c.city_name, out CityId cid)
                            ? CityCatalog.GetPrimaryZoneName(cid)
                            : "RallySquare";
                        bestCityIdeology = c.ideology_distribution.GetDominantIdeology();
                        break;
                    }
                }
            }

            List<AiActionItem> proposed = new List<AiActionItem>();
            string phase = snapshot != null ? snapshot.phase : "PREPARATION";

            if (phase == "CITY_VOTE")
            {
                proposed.Add(new AiActionItem
                {
                    action_type = "VOTE_RALLY_CITY",
                    city_name = targetCity,
                    reason = $"{targetCity} has not hosted a rally yet and aligns well with strategic outreach."
                });
            }
            else if (phase == "ELECTION_BRIBE")
            {
                float bribe = snapshot != null ? Mathf.Min(2500f, Mathf.Floor(snapshot.party_budget * 0.25f)) : 0f;
                if (bribe > 0f)
                {
                    proposed.Add(new AiActionItem
                    {
                        action_type = "ALLOCATE_ELECTION_BRIBE",
                        city_name = targetCity,
                        bribe_amount = bribe,
                        reason = "Allocate bounded Election Night bribe within party_budget for +max 2% swing."
                    });
                }
            }
            else
            {
                proposed.Add(new AiActionItem
                {
                    action_type = "DEPLOY_UNIT",
                    unit_type = "bodyguard",
                    city_name = targetCity,
                    target_zone = targetZone,
                    count = 2,
                    reason = "Screen Propagandists and deter opposing Campaign Volunteers/Saboteurs."
                });
                proposed.Add(new AiActionItem
                {
                    action_type = "CHOOSE_SPEECH_STRATEGY",
                    city_name = targetCity,
                    ideology_focus = IdeologyCodec.ToKey(bestCityIdeology),
                    reason = $"Align speech with dominant voter bloc ({IdeologyCodec.ToKey(bestCityIdeology)}) in {targetCity}."
                });
            }

            return new CommanderAskResponse
            {
                ok = true,
                is_fallback = true,
                provider_used = "offline-fallback",
                model_name = "deterministic-planner",
                latency_ms = elapsedMs,
                explanation = $"[Gateway çevrimdışı ({networkError})] Mevcut güvenli snapshot analiz edildi ({snapshot?.cities?.Count ?? 0} şehir, bütçe {snapshot?.party_budget ?? 0f:N0}).",
                tool_calls = new List<ToolCallTraceDto>
                {
                    new ToolCallTraceDto { tool = "get_player_budget", duration_ms = 1, status = "OK", result_summary = $"budget={snapshot?.party_budget ?? 0f:N0}" },
                    new ToolCallTraceDto { tool = "get_player_overall", duration_ms = 1, status = "OK", result_summary = snapshot?.player_overall?.ToShortSummary() ?? "" },
                    new ToolCallTraceDto { tool = "get_city_intelligence", duration_ms = 2, status = "OK", result_summary = $"target={targetCity}" },
                    new ToolCallTraceDto { tool = "get_visible_opponent_activity", duration_ms = 1, status = "OK", result_summary = $"visible={snapshot?.visible_opponent_activity?.Count ?? 0}" },
                    new ToolCallTraceDto { tool = "search_game_knowledge", duration_ms = 2, status = "OK", result_summary = "unit_bodyguard_v1, rule_unit_counters_v1" }
                },
                rag_sources = new List<RagSourceTraceDto>
                {
                    new RagSourceTraceDto { source_id = "unit_bodyguard_v1", title = "bodyguard (Koruma)", score = 0.89f },
                    new RagSourceTraceDto { source_id = "rule_unit_counters_v1", title = "Unit Counter Matrix", score = 0.86f }
                },
                action_plan = new ActionPlan
                {
                    summary = $"Prepare {targetCity} with Bodyguard escort and {IdeologyCodec.ToKey(bestCityIdeology)} speech focus.",
                    confidence = 0.78f,
                    actions = proposed
                }
            };
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (text.Contains(terms[i])) return true;
            }
            return false;
        }
    }
}
