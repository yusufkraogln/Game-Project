using System;
using UnityEngine;

namespace PoliticalMayhem.Core
{
    /// <summary>
    /// Self-contained procedural Audio Manager synthesizing clean, non-harsh game sound effects
    /// (UI clicks, RTS orders, combat hits, applause/cheering, crowd booing, security whistle/brawl,
    /// comedic revolver & sad trombone, donation chime, phase bell, and Balcony victory fanfare).
    /// Automatically ensures an AudioListener exists on the Main Camera.
    /// </summary>
    public class GameAudioManager : MonoBehaviour
    {
        public static GameAudioManager Instance { get; private set; }

        private const int SampleRate = 22050;

        [Range(0f, 1f)]
        [SerializeField] private float _masterVolume = 0.65f;
        [SerializeField] private bool _muted;

        private AudioSource _sfxSource;
        private AudioSource _crowdSource;

        private AudioClip _uiClickClip;
        private AudioClip _unitOrderClip;
        private AudioClip _unitDeployClip;
        private AudioClip _meleeHitClip;
        private AudioClip _rangedPulseClip;
        private AudioClip _unitDefeatClip;
        private AudioClip _crowdCheerClip;
        private AudioClip _crowdBooClip;
        private AudioClip _brawlWhistleClip;
        private AudioClip _revolverHitClip;
        private AudioClip _revolverMissClip;
        private AudioClip _donationClip;
        private AudioClip _phaseBellClip;
        private AudioClip _balconyFanfareClip;

        private float _lastCombatSoundTime = -10f;
        private float _lastUiClickTime = -10f;

        public bool IsMuted => _muted;
        public float MasterVolume => _masterVolume;

        public static GameAudioManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            GameAudioManager existing = FindAnyObjectByType<GameAudioManager>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            GameObject host = GameObject.Find("GameManagers") ?? new GameObject("GameAudioManager");
            Instance = host.AddComponent<GameAudioManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            EnsureAudioListener();
            EnsureAudioSources();
            BuildProceduralClips();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ToggleMute()
        {
            _muted = !_muted;
            if (_muted && _crowdSource != null)
                _crowdSource.Stop();
            if (!_muted)
                PlayUiClick();
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            if (_muted && _crowdSource != null)
                _crowdSource.Stop();
        }

        private void EnsureAudioListener()
        {
            if (FindAnyObjectByType<AudioListener>() != null) return;
            Camera cam = Camera.main ?? FindAnyObjectByType<Camera>();
            if (cam != null)
                cam.gameObject.AddComponent<AudioListener>();
            else
                gameObject.AddComponent<AudioListener>();
        }

        private void EnsureAudioSources()
        {
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
                _sfxSource.spatialBlend = 0f;
            }
            if (_crowdSource == null)
            {
                _crowdSource = gameObject.AddComponent<AudioSource>();
                _crowdSource.playOnAwake = false;
                _crowdSource.spatialBlend = 0f;
            }
        }

        private void PlayClip(AudioClip clip, float volumeScale = 1f, bool onCrowdChannel = false)
        {
            if (_muted || clip == null) return;
            EnsureAudioListener();
            EnsureAudioSources();
            AudioSource src = onCrowdChannel ? _crowdSource : _sfxSource;
            if (src != null)
                src.PlayOneShot(clip, Mathf.Clamp01(_masterVolume * volumeScale));
        }

        public void PlayUiClick()
        {
            if (Time.realtimeSinceStartup - _lastUiClickTime < 0.04f) return;
            _lastUiClickTime = Time.realtimeSinceStartup;
            PlayClip(_uiClickClip, 0.45f);
        }

        public void PlayUnitOrder() => PlayClip(_unitOrderClip, 0.55f);
        public void PlayUnitDeploy() => PlayClip(_unitDeployClip, 0.65f);

        public void PlayCombatHit(bool isRanged)
        {
            // Throttle dense multi-unit combat hits so audio stays crisp
            if (Time.realtimeSinceStartup - _lastCombatSoundTime < 0.14f) return;
            _lastCombatSoundTime = Time.realtimeSinceStartup;
            PlayClip(isRanged ? _rangedPulseClip : _meleeHitClip, 0.50f);
        }

        public void PlayUnitDefeated() => PlayClip(_unitDefeatClip, 0.65f);
        public void PlayCrowdCheer() => PlayClip(_crowdCheerClip, 0.72f, onCrowdChannel: true);
        public void PlayCrowdBoo() => PlayClip(_crowdBooClip, 0.70f, onCrowdChannel: true);
        public void PlayBrawl() => PlayClip(_brawlWhistleClip, 0.80f, onCrowdChannel: true);
        public void PlayRevolverShot(bool hitGuilty) => PlayClip(hitGuilty ? _revolverHitClip : _revolverMissClip, 0.85f);
        public void PlayDonationEarned() => PlayClip(_donationClip, 0.75f);
        public void PlayPhaseTransition() => PlayClip(_phaseBellClip, 0.55f);
        public void PlayBalconyFanfare() => PlayClip(_balconyFanfareClip, 0.85f, onCrowdChannel: true);

        private void BuildProceduralClips()
        {
            _uiClickClip = CreateToneSequence("SFX_UiClick", new[] { (720f, 0.032f, 0.35f) });
            _unitOrderClip = CreateToneSequence("SFX_UnitOrder", new[] { (520f, 0.055f, 0.32f), (760f, 0.085f, 0.36f) });
            _unitDeployClip = CreateToneSequence("SFX_UnitDeploy", new[] { (440f, 0.06f, 0.30f), (554f, 0.06f, 0.32f), (659f, 0.11f, 0.35f) });
            _meleeHitClip = CreateMeleeHitClip();
            _rangedPulseClip = CreateRangedPulseClip();
            _unitDefeatClip = CreateSlideClip("SFX_UnitDefeat", 460f, 150f, 0.28f, 0.42f);
            _crowdCheerClip = CreateApplauseCheerClip();
            _crowdBooClip = CreateCrowdBooClip();
            _brawlWhistleClip = CreateBrawlWhistleClip();
            _revolverHitClip = CreateRevolverOutcomeClip(hitGuilty: true);
            _revolverMissClip = CreateRevolverOutcomeClip(hitGuilty: false);
            _donationClip = CreateToneSequence("SFX_Donation", new[] { (988f, 0.065f, 0.32f), (1318f, 0.16f, 0.38f) });
            _phaseBellClip = CreateToneSequence("SFX_PhaseBell", new[] { (587f, 0.11f, 0.28f), (880f, 0.20f, 0.30f) });
            _balconyFanfareClip = CreateBalconyFanfareClip();
        }

        private static AudioClip CreateToneSequence(string clipName, (float freq, float dur, float amp)[] notes)
        {
            float totalDur = 0f;
            for (int i = 0; i < notes.Length; i++) totalDur += notes[i].dur;
            int totalSamples = Mathf.Max(1, Mathf.RoundToInt(totalDur * SampleRate));
            float[] data = new float[totalSamples];

            int cursor = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                int count = Mathf.RoundToInt(notes[n].dur * SampleRate);
                float freq = notes[n].freq;
                float amp = notes[n].amp;
                for (int i = 0; i < count && (cursor + i) < totalSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Sin((i / (float)Mathf.Max(1, count)) * Mathf.PI);
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.78f + Mathf.Sin(4f * Mathf.PI * freq * t) * 0.22f;
                    data[cursor + i] = wave * env * amp;
                }
                cursor += count;
            }
            return MakeClip(clipName, data);
        }

        private static AudioClip CreateSlideClip(string clipName, float startFreq, float endFreq, float duration, float amp)
        {
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)count;
                float freq = Mathf.Lerp(startFreq, endFreq, progress);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float env = (1f - progress) * Mathf.Min(1f, progress * 12f);
                data[i] = Mathf.Sin(phase) * env * amp;
            }
            return MakeClip(clipName, data);
        }

        private static AudioClip CreateMeleeHitClip()
        {
            int count = Mathf.RoundToInt(0.14f * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(101);
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float p = i / (float)count;
                float freq = Mathf.Lerp(170f, 55f, p);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float thud = Mathf.Sin(phase) * (1f - p);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-p * 14f) * 0.45f;
                data[i] = Mathf.Clamp((thud * 0.55f + noise) * 0.65f, -1f, 1f);
            }
            return MakeClip("SFX_MeleeHit", data);
        }

        private static AudioClip CreateRangedPulseClip()
        {
            int count = Mathf.RoundToInt(0.16f * SampleRate);
            float[] data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float p = i / (float)count;
                float freq = Mathf.Lerp(680f, 360f, p);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float env = Mathf.Sin(p * Mathf.PI) * (1f - 0.35f * p);
                data[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 1.5f) * 0.3f) * env * 0.42f;
            }
            return MakeClip("SFX_RangedPulse", data);
        }

        private static AudioClip CreateApplauseCheerClip()
        {
            float duration = 1.45f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(202);
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float p = i / (float)count;
                float env = Mathf.Min(1f, p * 6f) * Mathf.Pow(1f - p, 0.75f);
                // Layered rhythmic hand-clap pulses + warm triumphant major triad
                float clapPulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 28f)) * Mathf.Abs(Mathf.Sin(t * 17f + 0.6f));
                float rawNoise = (float)rng.NextDouble() * 2f - 1f;
                lp = lp * 0.72f + rawNoise * 0.28f;
                float clap = (rawNoise - lp) * clapPulse * 0.38f;
                float chord = (Mathf.Sin(2f * Mathf.PI * 523.25f * t) +
                               Mathf.Sin(2f * Mathf.PI * 659.25f * t) +
                               Mathf.Sin(2f * Mathf.PI * 783.99f * t)) * 0.08f;
                data[i] = Mathf.Clamp((clap + chord) * env, -1f, 1f);
            }
            return MakeClip("SFX_CrowdCheer", data);
        }

        private static AudioClip CreateCrowdBooClip()
        {
            float duration = 1.15f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(303);
            float lp = 0f;
            float phaseA = 0f;
            float phaseB = 0f;
            for (int i = 0; i < count; i++)
            {
                float p = i / (float)count;
                float env = Mathf.Min(1f, p * 8f) * (1f - p * 0.85f);
                float fA = Mathf.Lerp(210f, 138f, p);
                float fB = Mathf.Lerp(175f, 112f, p);
                phaseA += 2f * Mathf.PI * fA / SampleRate;
                phaseB += 2f * Mathf.PI * fB / SampleRate;
                float raw = (float)rng.NextDouble() * 2f - 1f;
                lp = lp * 0.88f + raw * 0.12f;
                float groan = (Mathf.Sin(phaseA) * 0.24f + Mathf.Sin(phaseB) * 0.22f + lp * 0.30f);
                data[i] = Mathf.Clamp(groan * env * 0.85f, -1f, 1f);
            }
            return MakeClip("SFX_CrowdBoo", data);
        }

        private static AudioClip CreateBrawlWhistleClip()
        {
            float duration = 0.95f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(404);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float p = i / (float)count;
                // Security pea-whistle trill + cartoonish scuffle thuds
                float trill = 1f + 0.045f * Mathf.Sin(2f * Mathf.PI * 34f * t);
                float whistleEnv = t < 0.55f ? Mathf.Sin((t / 0.55f) * Mathf.PI) : 0f;
                float whistle = Mathf.Sin(2f * Mathf.PI * 2450f * trill * t) * whistleEnv * 0.20f;
                float thudBeat = Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 6.5f * t));
                float scuffle = (((float)rng.NextDouble() * 2f - 1f) * 0.22f + Mathf.Sin(2f * Mathf.PI * 115f * t) * 0.22f)
                                * thudBeat * (1f - p * 0.7f);
                data[i] = Mathf.Clamp(whistle + scuffle, -1f, 1f);
            }
            return MakeClip("SFX_BrawlWhistle", data);
        }

        private static AudioClip CreateRevolverOutcomeClip(bool hitGuilty)
        {
            float duration = 1.05f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(hitGuilty ? 505 : 606);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float sample = 0f;
                // Comedic crisp cap-gun pop in first 0.14s
                if (t < 0.14f)
                {
                    float popEnv = Mathf.Exp(-t * 32f);
                    sample += (((float)rng.NextDouble() * 2f - 1f) * 0.6f + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.4f) * popEnv * 0.7f;
                }
                else
                {
                    float nt = t - 0.18f;
                    if (nt >= 0f)
                    {
                        if (hitGuilty)
                        {
                            // Bright celebratory ding-ding!
                            float freq = nt < 0.16f ? 784f : (nt < 0.34f ? 988f : 1175f);
                            float env = Mathf.Exp(-((nt % 0.18f) * 8f)) * (1f - nt / 0.90f);
                            sample += Mathf.Sin(2f * Mathf.PI * freq * nt) * Mathf.Max(0f, env) * 0.38f;
                        }
                        else
                        {
                            // Classic cartoon descending sad trombone wah-wah-waaah
                            float freq = nt < 0.22f ? 311f : (nt < 0.44f ? 293f : Mathf.Lerp(277f, 240f, (nt - 0.44f) / 0.45f));
                            float env = Mathf.Max(0f, 1f - nt / 0.88f);
                            sample += (Mathf.Sin(2f * Mathf.PI * freq * nt) * 0.65f + Mathf.Sin(4f * Mathf.PI * freq * nt) * 0.25f) * env * 0.34f;
                        }
                    }
                }
                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }
            return MakeClip(hitGuilty ? "SFX_RevolverHit" : "SFX_RevolverMiss", data);
        }

        private static AudioClip CreateBalconyFanfareClip()
        {
            float duration = 2.1f;
            int count = Mathf.RoundToInt(duration * SampleRate);
            float[] data = new float[count];
            System.Random rng = new System.Random(707);
            float lp = 0f;
            // Celebratory brass-like fanfare C5 -> E5 -> G5 -> C6 + crowd applause!
            (float start, float end, float freq)[] notes =
            {
                (0.00f, 0.24f, 523.25f),
                (0.24f, 0.48f, 659.25f),
                (0.48f, 0.72f, 783.99f),
                (0.72f, 1.85f, 1046.50f)
            };

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float p = i / (float)count;
                float melody = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    if (t >= notes[n].start && t <= notes[n].end)
                    {
                        float lt = (t - notes[n].start) / (notes[n].end - notes[n].start);
                        float env = Mathf.Min(1f, lt * 12f) * (1f - lt * 0.45f);
                        float f = notes[n].freq;
                        melody += (Mathf.Sin(2f * Mathf.PI * f * t) * 0.65f +
                                   Mathf.Sin(4f * Mathf.PI * f * t) * 0.22f +
                                   Mathf.Sin(2f * Mathf.PI * (f * 0.5f) * t) * 0.20f) * env * 0.28f;
                    }
                }
                float raw = (float)rng.NextDouble() * 2f - 1f;
                lp = lp * 0.74f + raw * 0.26f;
                float applause = (raw - lp) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 24f))) * Mathf.Sin(p * Mathf.PI) * 0.26f;
                data[i] = Mathf.Clamp(melody + applause, -1f, 1f);
            }
            return MakeClip("SFX_BalconyFanfare", data);
        }

        private static AudioClip MakeClip(string clipName, float[] samples)
        {
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
