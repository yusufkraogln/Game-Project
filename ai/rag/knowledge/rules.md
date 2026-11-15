# POLITICAL MAYHEM: Battle for the Cities — Game Rules & Tactical Knowledge Base

## [rule_cities_v1] Cities, Populations, and Ideological Demographics
Political Mayhem takes place across 6 fixed strategic cities (şehirler) whose geographic locations and total populations never change between matches:
- **Baskent** (Başkent): 120,000 voters — largest capital prize.
- **Demirkent**: 110,000 voters — major industrial hub.
- **Limankent**: 90,000 voters — coastal commercial port.
- **Yesilova** (Yeşilova): 80,000 voters — balanced agricultural/regional center.
- **Universitekent** (Üniversitekent): 70,000 voters — academic and youth hub.
- **Dagsehir** (Dağşehir): 60,000 voters — smaller highland city.

Every city contains 5 ideological voter blocs (siyasi gruplar): `nationalist` (Milliyetçiler), `liberal` (Liberaller), `democrat` (Demokratlar), `communist` (Komünistler), and `imperialist` (Emperyalistler). At the start of each match, each city's ideological distribution is randomized and sums to 100%. Cities serve directly as strategic control centers without traditional RTS building construction.

## [rule_overall_formula_v1] Overall Dynamic Political Identity Formula (Siyasi Kimlik Formülü)
Players do not pick a fixed faction or ideology at match start. Instead, each player maintains five independent **Overall** ratings (`0` to `100`): `nationalist` (Milliyetçilik), `liberal` (Liberalizm), `democrat` (Demokratlık), `communist` (Komünizm), and `imperialist` (Emperyalizm). After every speech evaluation by the LLM, each ideological Overall score updates deterministically via:

`New Overall = 0.80 * Previous Overall + 0.20 * Speech Ideology Score`

Overall functions as the RTS technology and doctrine progression system. Reaching threshold milestones (e.g., Overall >= 40) unlocks passive ideological bonuses (`tech_*`). Overall directly affects:
1. Initial crowd reaction in a city (`CHEER`, `BOO`, `PROTEST`, `THROW_OBJECTS`, `MIXED`).
2. Speech persuasion effectiveness.
3. Saboteur persuasion effectiveness.
4. Rally donation yield (`party_budget`).
5. Ideological alignment weight (30%) on Election Night.

Players can inspect only their own exact Overall scores; opponent Overall ratings remain hidden under fog-of-war.

## [rule_rally_rounds_v1] Six Rally Rounds & Phase Timeline
A match has **6 rally rounds (miting turları)** before Election Night. Roles alternate strictly so each player serves as Speaker (Konuşmacı) 3 times and Saboteur (Sabotajcı) 3 times:
- Round 1: Player A Speaker / Player B Saboteur
- Round 2: Player B Speaker / Player A Saboteur
- Round 3: Player A Speaker / Player B Saboteur
- Round 4: Player B Speaker / Player A Saboteur
- Round 5: Player A Speaker / Player B Saboteur
- Round 6: Player B Speaker / Player A Saboteur

Before each round, both players vote on which unvisited city hosts the rally; cities already visited cannot be selected again, and a 1-1 tie picks randomly among the nominated candidates. Each round follows four timed phases:
1. **City Vote (`CITY_VOTE`)**: 15 seconds.
2. **Preparation (`PREPARATION`)**: 30 seconds (deploy and position campaign units within capacity limits).
3. **Rally Speech & Sabotage (`RALLY_SPEECH`)**: 60 seconds (Speaker delivers text or speech-to-text address; opponent acts as Saboteur).
4. **AI Evaluation (`AI_EVALUATION`)**: 10 seconds (LLM Speech Evaluation + deterministic Overall and donation calculation).

## [rule_sabotage_revolver_v1] Verbal Sabotage, Physical Sabotage, and Revolver Duel
During the 60-second `RALLY_SPEECH` phase, the opponent infiltrates the rally crowd as the Saboteur (`saboteur_agent`) and may perform two forms of sabotage:
- **Verbal Sabotage (Sözlü Sabotaj)**: Counter-arguing against the Speaker's points. The LLM judges relative rhetorical persuasion (`sabotage_effectiveness`). Verbal sabotage carries **no direct legal/arrest penalty**.
- **Physical Sabotage (Fiziksel Sabotaj)**: Instigating a cartoonish crowd brawl to heavily disrupt rally performance and reduce donations. However, the Speaker's 5-person Security Squad (`security_squad` / Güvenlik Ekibi) pursues physical brawlers.

**Revolver Mechanic (`REVOLVER_DUEL`)**: If the Security Squad arrests the physical Saboteur, the infiltrator is placed into a 5-suspect lineup at the end of the rally. The Speaker may fire one revolver shot in non-graphic political-comedy style:
- **Correct suspect**: The Saboteur's next speaking turn is blocked.
- **Wrong suspect**: The Speaker suffers a severe political reputation penalty.

## [rule_donation_economy_v1] Party Budget & Donation Economy (Parti Bütçesi ve Bağış Ekonomisi)
The game uses a **single monetary resource: `party_budget` (Parti Bütçesi)**. Money (para / bağış) is generated exclusively from successful rally donations according to the deterministic formula executed on the Photon Host:

`Donation = Group Population * Donation Propensity * Speech Impact * Overall Alignment * Scale Factor`

LLM evaluation outputs normalized scores (`0..100`), while Unity/Photon calculates the actual monetary payout deterministically. **Crucial rule: `party_budget` is NEVER spent on deploying units or upgrading buildings during the 6 rally rounds.** Every coin accumulates until Election Night (`ELECTION_BRIBE`), where `party_budget` can only be spent on limited voter bribery across the 6 cities.

## [rule_election_night_v1] Election Night, Bribery Cap, and Victory Condition (Seçim Gecesi, Rüşvet Sınırı ve Kazanma Şartı)
After all 6 rally rounds finish, **Election Night (Seçim Gecesi)** resolves each of the 6 cities one by one across three phases:
1. **Final Minute Speech (`ELECTION_FINAL_SPEECH`)**: Both candidates receive 15 seconds to deliver a closing pitch evaluated by the LLM.
2. **Bribery (`ELECTION_BRIBE`)**: Optional, secret, simultaneous `party_budget` spending per city. Spent money is deducted immediately and non-refundable, subject to diminishing returns. Opposing bribes offset each other, and **the maximum net effect of bribery is strictly capped at 2 percentage points (2% vote swing)**. Bribery cannot win a landslide alone, but decides razor-thin margins (e.g., flipping 49.0% to 50.5%).
3. **Deterministic Vote Calculation (`ELECTION_RESULT`)**: Photon Host computes each candidate's base support share using:

`Base Support = 0.30 * Overall_Alignment + 0.30 * Rally_Speech + 0.15 * Campaign_Activity + 0.10 * Sabotage_Effect + 0.15 * Final_Speech`

The bounded bribe shift is then applied without inflating total city population. **Victory Condition**: The player winning the most cities out of 6 wins the match. A 3-3 city tie is broken by total popular vote across all 6 cities (followed by a deterministic tie-breaker if popular vote is tied). The winner concludes the match with a celebratory 60-second **Balcony Speech (`BALCONY_SPEECH`)**.

## [rule_unit_counters_v1] Real-Time Campaign Units & Counter Matrix
Players control three primary campaign unit types (plus mobile/rally role units) deployed via per-round capacity limits without spending `party_budget`:
- **Campaign Volunteer (`campaign_volunteer` / Kampanya Gönüllüsü)**: Fast grassroots unit (limit 6/round). **Counters unescorted Propagandists** and rapidly spreads campaign activity (15% election weight). **Countered by Bodyguards and Security Squads**.
- **Propagandist (`propagandist` / Propagandacı)**: Ranged ideological broadcaster (limit 4/round). Pressures opposing morale from range and boosts persuasion modifiers, **effectively pressuring slow Bodyguards when screened**. **Countered by Campaign Volunteers and Saboteurs** in melee.
- **Bodyguard (`bodyguard` / Koruma)**: Heavy melee protector (limit 3/round, high HP & armor 5). **Hard-counters Campaign Volunteers and Saboteurs**, protecting Propagandists and stages. **Vulnerable to ranged Propagandist focus and Rally Convoy kiting**.
- **Rally Convoy (`rally_convoy` / Miting Konvoyu)**: Fast mobile outreach vehicle (limit 2/round, speed 6.2) for rapid redeployment across distant cities like Dağşehir and Başkent.

## [rule_fog_of_war_v1] Fog of War, Authorization, and AI Safety Boundaries (Bilgi Gizliliği ve Güvenlik Sınırları)
Strict information isolation (Fog of War / Bilgi Gizliliği) governs both players and the AI Strategic Commander:
- **Hidden State (NEVER exposed to LLM, MCP, or opponent client)**: Opponent's `party_budget`, opponent's exact `player_overall` scores, opponent's private strategic plans or queued bribes, and unannounced intermediate city vote support percentages.
- **Visible State (`AiSafeSnapshot`)**: Player's own `party_budget`, own `player_overall`, public city populations and ideological demographics, own deployed units, visibly observed opponent units/speeches (`visible_opponent_activity`), and public match events.
- **Identity & Control**: `player_id` and `match_id` are bound server-side on the AI Gateway; the LLM cannot request enemy state by passing `player_id="opponent"`. Every AI `ActionPlan` is strictly advisory: it must validate against JSON Schema, require explicit player `APPROVE`/`REJECT` input, and pass deterministic `GameActionValidator` + Photon Fusion Host authority checks before execution.
