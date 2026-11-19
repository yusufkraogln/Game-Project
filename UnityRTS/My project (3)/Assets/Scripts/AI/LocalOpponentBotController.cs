using System.Collections.Generic;
using PoliticalMayhem.Core;
using PoliticalMayhem.Election;
using PoliticalMayhem.Networking;
using PoliticalMayhem.Rally;
using PoliticalMayhem.Units;
using UnityEngine;

namespace PoliticalMayhem.AI
{
    /// <summary>
    /// Autonomous local Opponent Bot controlling Player B across all phases of Political Mayhem
    /// (City Vote, Unit Deployment & Movement, Rally Speeches, Verbal/Physical Sabotage,
    /// Revolver Duel, Election Final Speech, Secret Bribery, and Balcony Speech).
    /// Respects Fog of War and routes every action through PhotonFusionAuthority & GameActionValidator.
    /// </summary>
    public class LocalOpponentBotController : MonoBehaviour
    {
        public static LocalOpponentBotController Instance { get; private set; }

        [Header("Bot Settings")]
        [SerializeField] private bool _botEnabled = true;

        private PoliticalMayhemGameManager _gameManager;
        private UnitController _unitController;
        private RallyController _rallyController;
        private SabotageAndRevolverController _sabotageController;
        private ElectionNightController _electionController;
        private PhotonFusionAuthority _authority;

        private int _lastActedRound = -1;
        private TurnPhase _lastActedPhase = (TurnPhase)(-1);
        private float _phaseElapsed;
        private bool _didCityVote;
        private bool _didPrepDeploy;
        private bool _didPrepMove;
        private bool _didSpeakerSpeech;
        private bool _didVerbalSabotage;
        private bool _didPhysicalSabotageAttempt;
        private bool _didRevolverDecision;
        private bool _didFinalSpeech;
        private bool _didElectionBribe;
        private bool _didBalconySpeech;

        private string _lastBotActionLabel = "Rakip Bot (Oyuncu B) beklemede.";

        public bool BotEnabled
        {
            get => _botEnabled;
            set => _botEnabled = value;
        }

        public string LastBotActionLabel => _lastBotActionLabel;

        public static LocalOpponentBotController EnsureInstance()
        {
            if (Instance != null) return Instance;
            LocalOpponentBotController existing = FindAnyObjectByType<LocalOpponentBotController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            GameObject host = GameObject.Find("GameManagers") ?? new GameObject("LocalOpponentBot");
            Instance = host.AddComponent<LocalOpponentBotController>();
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
            ResolveReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void ResolveReferences()
        {
            if (_gameManager == null) _gameManager = PoliticalMayhemGameManager.Instance ?? FindAnyObjectByType<PoliticalMayhemGameManager>();
            if (_unitController == null) _unitController = FindAnyObjectByType<UnitController>();
            if (_rallyController == null) _rallyController = RallyController.Instance ?? FindAnyObjectByType<RallyController>();
            if (_sabotageController == null) _sabotageController = FindAnyObjectByType<SabotageAndRevolverController>();
            if (_electionController == null) _electionController = ElectionNightController.Instance ?? FindAnyObjectByType<ElectionNightController>();
            if (_authority == null) _authority = PhotonFusionAuthority.Instance ?? FindAnyObjectByType<PhotonFusionAuthority>();
        }

        private void Update()
        {
            if (!_botEnabled) return;
            ResolveReferences();
            if (_gameManager == null) return;

            // Yield automatically if a real networked Photon Fusion 2 session is active or user switched to Player B
            if (_authority != null && _authority.IsFusionSdkEnabled) return;
            if (_gameManager.LocalPlayerId == PlayerIdentity.PlayerBId) return;

            // Respect paused timers (e.g. while user is reading the Start Menu)
            bool paused = (_electionController != null && _electionController.IsElectionPhase())
                ? _electionController.PauseTimers
                : (_rallyController != null && _rallyController.PauseTimers);
            if (paused) return;

            int round = _gameManager.RoundNumber;
            TurnPhase phase = _gameManager.CurrentPhase;
            if (round != _lastActedRound || phase != _lastActedPhase)
            {
                ResetPhaseFlags(round, phase);
            }

            _phaseElapsed += Time.deltaTime;

            switch (phase)
            {
                case TurnPhase.CITY_VOTE:
                    TickCityVote();
                    break;
                case TurnPhase.PREPARATION:
                    TickPreparation();
                    break;
                case TurnPhase.RALLY_SPEECH:
                    TickRallySpeech();
                    break;
                case TurnPhase.REVOLVER_DUEL:
                    TickRevolverDuel();
                    break;
                case TurnPhase.ELECTION_FINAL_SPEECH:
                    TickElectionFinalSpeech();
                    break;
                case TurnPhase.ELECTION_BRIBE:
                    TickElectionBribe();
                    break;
                case TurnPhase.BALCONY_SPEECH:
                    TickBalconySpeech();
                    break;
            }
        }

        private void ResetPhaseFlags(int round, TurnPhase phase)
        {
            _lastActedRound = round;
            _lastActedPhase = phase;
            _phaseElapsed = 0f;
            _didCityVote = false;
            _didPrepDeploy = false;
            _didPrepMove = false;
            _didSpeakerSpeech = false;
            _didVerbalSabotage = false;
            _didPhysicalSabotageAttempt = false;
            _didRevolverDecision = false;
            _didFinalSpeech = false;
            _didElectionBribe = false;
            _didBalconySpeech = false;
        }

        private void TickCityVote()
        {
            if (_didCityVote || _gameManager.CityManager == null) return;
            bool playerAVoted = !string.IsNullOrEmpty(_gameManager.PlayerA.cityVoteChoice);
            if (_phaseElapsed < (playerAVoted ? 0.9f : 3.2f)) return;

            List<CityState> remaining = _gameManager.CityManager.GetRemainingRallyCities();
            if (remaining.Count == 0)
            {
                _didCityVote = true;
                return;
            }

            // Pick highest-value remaining city weighted by population + Bot's Overall alignment
            CityState bestCity = remaining[0];
            float bestScore = float.MinValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                CityState c = remaining[i];
                float align = _gameManager.OverallManager != null
                    ? _gameManager.OverallManager.ComputeOverallAlignment(PlayerIdentity.PlayerBId, c.ideologyDistribution)
                    : 25f;
                float score = (c.population / 1000f) * 0.65f + align * 0.85f + ((c.cityId.GetHashCode() + _gameManager.RoundNumber * 17) & 7);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCity = c;
                }
            }

            _didCityVote = true;
            if (_authority != null)
            {
                _authority.RequestVoteRallyCity(PlayerIdentity.PlayerBId, bestCity.cityId, out _);
                _lastBotActionLabel = $"Rakip Bot {bestCity.displayName} için oy kullandı.";
            }
        }

        private void TickPreparation()
        {
            CityId rallyCity = _gameManager.ActiveRallyCity;

            if (!_didPrepDeploy && _phaseElapsed >= 1.5f)
            {
                _didPrepDeploy = true;
                if (_authority != null)
                {
                    string zone = CityCatalog.GetPrimaryZoneName(rallyCity);
                    // Deploy balanced campaign units within per-round capacity limits (0 TL spent)
                    _authority.RequestDeployUnit(PlayerIdentity.PlayerBId, UnitType.CampaignVolunteer, rallyCity, zone, 1, out _);
                    UnitType secondType = (_gameManager.RoundNumber % 2 == 0) ? UnitType.Bodyguard : UnitType.Propagandist;
                    _authority.RequestDeployUnit(PlayerIdentity.PlayerBId, secondType, rallyCity, zone, 1, out _);
                    if (_gameManager.RoundNumber == 2 || _gameManager.RoundNumber == 5)
                    {
                        _authority.RequestDeployUnit(PlayerIdentity.PlayerBId, UnitType.RallyConvoy, rallyCity, zone, 1, out _);
                    }
                    _lastBotActionLabel = $"Rakip Bot {CityCatalog.GetDisplayName(rallyCity)} şehrine yeni ekip sevk etti.";
                }
            }

            if (!_didPrepMove && _phaseElapsed >= 3.2f)
            {
                _didPrepMove = true;
                if (_unitController != null && _authority != null && _gameManager.CityManager != null)
                {
                    List<int> movableIds = new List<int>();
                    IReadOnlyList<CampaignUnit> all = CampaignUnit.ActiveUnits;
                    for (int i = 0; i < all.Count; i++)
                    {
                        CampaignUnit u = all[i];
                        if (u != null && u.IsAlive && u.OwnerPlayerId == PlayerIdentity.PlayerBId && u.CurrentCity != rallyCity)
                        {
                            movableIds.Add(u.UnitId);
                            if (movableIds.Count >= 3) break;
                        }
                    }

                    if (movableIds.Count > 0)
                    {
                        Vector3 targetPos = _gameManager.CityManager.GetCityWorldPosition(rallyCity) + new Vector3(4.5f, 0f, -5.8f);
                        _authority.RequestMoveGroup(PlayerIdentity.PlayerBId, movableIds, targetPos, rallyCity, CityCatalog.GetPrimaryZoneName(rallyCity));
                        _lastBotActionLabel = $"Rakip Bot {movableIds.Count} birimini {CityCatalog.GetDisplayName(rallyCity)} meydanına yürüttü.";
                    }
                }
            }
        }

        private void TickRallySpeech()
        {
            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            CityState city = _gameManager.CityManager != null ? _gameManager.CityManager.GetCity(_gameManager.ActiveRallyCity) : null;
            IdeologyType dom = city != null ? city.ideologyDistribution.GetDominantIdeology() : IdeologyType.Democrat;

            if (speakerId == PlayerIdentity.PlayerBId)
            {
                // Player B is Speaker (Rounds 2, 4, 6)
                if (!_didSpeakerSpeech && _phaseElapsed >= 4.5f && _rallyController != null)
                {
                    _didSpeakerSpeech = true;
                    PlayerState botState = _gameManager.GetPlayerState(PlayerIdentity.PlayerBId);
                    if (botState.isNextSpeechBlocked)
                    {
                        _lastBotActionLabel = "Rakip Bot'un konuşması Revolver cezası yüzünden bloke edildi!";
                        _rallyController.ForceAdvancePhase();
                    }
                    else
                    {
                        string speech = BuildBotRallySpeech(city, dom);
                        _lastBotActionLabel = $"Rakip Bot {city?.displayName} meydanında konuşma yaptı.";
                        _rallyController.SubmitRallySpeech(PlayerIdentity.PlayerBId, speech);
                    }
                }
            }
            else
            {
                // Player B is Saboteur (Rounds 1, 3, 5)
                if (!_didVerbalSabotage && _phaseElapsed >= 5.0f && _sabotageController != null)
                {
                    _didVerbalSabotage = true;
                    string counter = BuildBotVerbalSabotage(city, dom);
                    _sabotageController.SubmitVerbalSabotage(PlayerIdentity.PlayerBId, counter);
                    _lastBotActionLabel = "Rakip Bot kalabalıktan sözlü sabotaj başlattı!";
                }

                // Trigger Physical Sabotage strategically on high-stakes cities (so player gets to experience Brawl & Revolver!)
                if (!_didPhysicalSabotageAttempt && _phaseElapsed >= 9.0f && _sabotageController != null)
                {
                    _didPhysicalSabotageAttempt = true;
                    bool highPriorityCity = city != null && city.population >= 90000;
                    bool shouldBrawl = highPriorityCity || (_gameManager.RoundNumber == 3) || (Random.value < 0.40f);
                    if (shouldBrawl)
                    {
                        _sabotageController.TriggerPhysicalSabotage(PlayerIdentity.PlayerBId);
                        _lastBotActionLabel = "Rakip Bot kalabalıkta fiziksel kavga çıkardı!";
                    }
                }
            }
        }

        private void TickRevolverDuel()
        {
            if (_didRevolverDecision || _sabotageController == null || _sabotageController.RevolverResolved)
                return;

            string speakerId = _gameManager.GetSpeakerForRound(_gameManager.RoundNumber);
            if (speakerId != PlayerIdentity.PlayerBId) return;
            if (_phaseElapsed < 2.8f) return;

            _didRevolverDecision = true;
            // 20% cautious hold-fire, 80% shoots a random suspect (never cheats by reading GuiltySuspectIndex!)
            if (Random.value < 0.20f)
            {
                _sabotageController.HoldFire(PlayerIdentity.PlayerBId);
                _lastBotActionLabel = "Rakip Bot Revolver düellosunda risk almayıp pas geçti.";
            }
            else
            {
                int guess = Random.Range(0, 5);
                _sabotageController.ResolveRevolverShot(PlayerIdentity.PlayerBId, guess);
                _lastBotActionLabel = $"Rakip Bot Şüpheli #{guess + 1} üzerine ateş etti!";
            }
        }

        private void TickElectionFinalSpeech()
        {
            if (_didFinalSpeech || _electionController == null || _phaseElapsed < 1.8f) return;
            _didFinalSpeech = true;

            CityState city = _gameManager.CityManager != null
                ? _gameManager.CityManager.GetCity(_electionController.CurrentElectionCityId)
                : null;
            IdeologyType dom = city != null ? city.ideologyDistribution.GetDominantIdeology() : IdeologyType.Democrat;
            string speech = BuildBotRallySpeech(city, dom);
            _electionController.SubmitFinalSpeech(PlayerIdentity.PlayerBId, speech);
            _lastBotActionLabel = $"Rakip Bot {city?.displayName} için son dakika konuşmasını gönderdi.";
        }

        private void TickElectionBribe()
        {
            if (_didElectionBribe || _electionController == null || _authority == null || _phaseElapsed < 1.6f) return;
            if (_electionController.BribeLockedB)
            {
                _didElectionBribe = true;
                return;
            }

            _didElectionBribe = true;
            CityId currentCity = _electionController.CurrentElectionCityId;
            float budget = _gameManager.PlayerB.partyBudget;
            int remainingCities = Mathf.Max(1, CityCatalog.All.Length - _electionController.CurrentCityIndex);

            float allocation = 0f;
            if (budget >= 250f)
            {
                float weight = CityCatalog.GetPopulation(currentCity) >= 100000 ? 1.35f : 0.90f;
                allocation = Mathf.Floor(Mathf.Min(budget, (budget / remainingCities) * weight) / 50f) * 50f;
            }

            if (allocation > 0f)
            {
                _authority.RequestAllocateElectionBribe(PlayerIdentity.PlayerBId, currentCity, allocation, out _);
                _lastBotActionLabel = $"Rakip Bot {CityCatalog.GetDisplayName(currentCity)} için gizli seçim bütçesi kararını kilitledi.";
            }
            else
            {
                _authority.RequestAllocateElectionBribe(PlayerIdentity.PlayerBId, currentCity, 0f, out _);
                _lastBotActionLabel = $"Rakip Bot {CityCatalog.GetDisplayName(currentCity)} için bütçesini sakladı.";
            }
        }

        private void TickBalconySpeech()
        {
            if (_didBalconySpeech || _electionController == null || _phaseElapsed < 2.2f) return;
            if (_gameManager.MatchWinnerId != PlayerIdentity.PlayerBId) return;

            _didBalconySpeech = true;
            _electionController.SubmitBalconySpeech("Tüm şehirlerimize teşekkürler! Halkımızın iradesi kazandı!");
            _lastBotActionLabel = "Rakip Bot zafer balkon konuşmasını yaptı!";
        }

        private static string BuildBotRallySpeech(CityState city, IdeologyType dominant)
        {
            string cityName = city != null ? city.displayName : "Şehrimiz";
            switch (dominant)
            {
                case IdeologyType.Nationalist:
                    return $"{cityName} için yerli sanayi, güçlü sınır güvenliği ve milli kalkınma hamlesini başlatıyoruz!";
                case IdeologyType.Liberal:
                    return $"{cityName} girişimcilerine serbest piyasa, düşük vergi ve açık ticaret özgürlüğü getiriyoruz!";
                case IdeologyType.Communist:
                    return $"{cityName} işçileri ve emekçileri için sendikal güvence, adil paylaşım ve büyük kamu yatırımı!";
                case IdeologyType.Imperialist:
                    return $"{cityName} merkezli dev altyapı projeleriyle bölgesel gücümüzü zirveye taşıyacağız!";
                default:
                    return $"{cityName} halkıyla şeffaf yönetim, hukuk devleti ve katılımcı demokrasi kuruyoruz!";
            }
        }

        private static string BuildBotVerbalSabotage(CityState city, IdeologyType dominant)
        {
            string cityName = city != null ? city.displayName : "Bu şehir";
            switch (dominant)
            {
                case IdeologyType.Nationalist:
                    return $"{cityName} halkı boş sloganlara değil, yerli üretime ve somut güvenliğe bakar!";
                case IdeologyType.Liberal:
                    return $"Bu vaatlerin ekonomik karşılığı yok; yüksek maliyetler {cityName} ticaretini durdurur!";
                case IdeologyType.Communist:
                    return $"{cityName} emekçilerinin hakkını görmezden gelen bu sözler gerçeği yansıtmıyor!";
                case IdeologyType.Imperialist:
                    return $"Büyük vizyon olmadan küçük vaatlerle {cityName} hak ettiği güce ulaşamaz!";
                default:
                    return $"{cityName} seçmeni şeffaflık ve hesap verebilirlik istiyor, rakamlar bu sözleri tutmuyor!";
            }
        }
    }
}
