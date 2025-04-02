using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;

namespace Mcp.Net.Examples.SimpleServer
{
    /// <summary>
    /// Provides simple Warhammer 40k themed example tools for the MCP protocol.
    /// </summary>
    public class Warhammer40kTools
    {
        private static readonly Random _random = new Random();
        private static readonly string[] _firstNames =
        {
            "Gregor",
            "Tyrus",
            "Darius",
            "Artemis",
            "Mordecai",
            "Katarinya",
            "Leto",
            "Silas",
            "Titus",
            "Amberly",
            "Morrigan",
            "Severina",
            "Felix",
            "Brutus",
            "Alaric",
            "Octavius",
            "Thaddeus",
            "Helene",
            "Cassian",
            "Valerius",
            "Kaius",
            "Torian",
            "Hector",
            "Lysander",
            "Lucretia",
            "Cornelius",
            "Domitius",
            "Cassius",
            "Flavius",
            "Demetrius",
            "Praxus",
            "Victoria",
            "Agathon",
            "Euphemia",
            "Theophilus",
        };

        private static readonly string[] _lastNames =
        {
            "Eisenhorn",
            "Ravenor",
            "Kryptman",
            "Coteaz",
            "Karamazov",
            "Voke",
            "Bronislaw",
            "Draco",
            "Scarn",
            "Covenant",
            "Hex",
            "Blackwood",
            "Darke",
            "Vaarak",
            "Solaris",
            "Grendel",
            "Thrax",
            "Karnak",
            "Haarlock",
            "Rex",
            "Coldheart",
            "Thorne",
            "Vail",
            "Streng",
            "Graves",
            "Ferron",
            "Stern",
            "Valorius",
            "Vyche",
            "Winters",
            "Grimm",
            "Cadmus",
            "Mortus",
            "Vash",
            "Infernus",
            "Acheron",
            "Solomon",
            "Morn",
            "Constantine",
        };

        private static readonly string[] _ordos =
        {
            "Hereticus",
            "Xenos",
            "Malleus",
            "Scriptorum",
            "Chronos",
            "Necros",
            "Astartes",
            "Sicarius",
            "Logos",
            "Obscurus",
            "Vigilus",
        };
        private static readonly string[] _factions =
        {
            "Space Marines",
            "Imperial Guard",
            "Adeptus Mechanicus",
        };
        private static readonly string[] _enemies =
        {
            "Tyranids",
            "Chaos",
            "Orks",
            "Necrons",
            "T'au",
        };
        private static readonly string[] _battlefields =
        {
            "Hive City",
            "Forge World",
            "Death World",
            "Space Hulk",
        };

        private static readonly string[] _homeworlds =
        {
            "Terra",
            "Cadia",
            "Armageddon",
            "Fenris",
            "Macragge",
            "Ophelia VII",
            "Necromunda",
            "Ryza",
            "Elysia",
            "Catachan",
            "Krieg",
            "Tanith",
            "Valhalla",
            "Vostroya",
            "Mordia",
            "Praetoria",
            "Tallarn",
            "Scintilla",
            "Maccabeus Quintus",
            "Prospero",
            "Baal",
            "Nostramo",
            "Olympia",
            "Chogoris",
        };

        private static readonly string[] _weapons =
        {
            "Power Sword",
            "Bolt Pistol",
            "Inferno Pistol",
            "Force Staff",
            "Digital Weapons",
            "Plasma Pistol",
            "Nemesis Force Halberd",
            "Daemonhammer",
            "Conversion Beamer",
            "Condemnor Boltgun",
            "Psyk-out Grenades",
            "Null Rod",
            "Force Blade",
            "Neural Whip",
            "Hellrifle",
            "Exitus Rifle",
            "Anointed Power Sword",
            "Archeotech Pistol",
            "Artificer-crafted Laspistol",
            "Runestaff",
            "Psycannon",
            "Daemon Blade",
            "Radiant Blade",
        };

        private static readonly string[] _achievements =
        {
            "Defeated a Greater Daemon",
            "Purged a genestealer cult",
            "Uncovered a heretical conspiracy",
            "Destroyed a Chaos warband",
            "Captured a notorious rogue psyker",
            "Recovered a lost STC",
            "Sealed a Warp rift",
            "Eliminated a xenos infestation",
            "Foiled an assassination plot",
            "Exposed corruption within a noble house",
            "Survived an Exterminatus",
            "Saved a forge world",
            "Neutralized a daemonic incursion",
            "Thwarted a Tau expansion",
            "Contained a viral outbreak",
            "Rescued a captured Inquisitor",
            "Destroyed an enemy titan",
            "Located a lost Imperial relic",
            "Dismantled a chaos cult",
            "Acquired a rare archeotech device",
            "Purged a corrupted Space Marine chapter",
        };

        /// <summary>
        /// Generates a name and title for a Warhammer 40k Inquisitor character.
        /// </summary>
        /// <param name="includeTitle">Whether to include a title prefix.</param>
        /// <returns>Information about the generated Inquisitor.</returns>
        [McpTool("wh40k_inquisitor_name", "Generate a name for a Warhammer 40k Inquisitor")]
        public static InquisitorInfo GenerateInquisitorName(
            [McpParameter(required: false, description: "Include title")] bool includeTitle = true
        )
        {
            string firstName = _firstNames[_random.Next(_firstNames.Length)];
            string lastName = _lastNames[_random.Next(_lastNames.Length)];
            string ordo = _ordos[_random.Next(_ordos.Length)];

            // Randomly decide title format (Lord or Lady based on typical name gender endings)
            string title;
            if (includeTitle)
            {
                // Simple heuristic for feminine-sounding names
                if (
                    firstName.EndsWith("a")
                    || firstName.EndsWith("ia")
                    || firstName.EndsWith("ina")
                    || firstName == "Victoria"
                    || firstName == "Euphemia"
                    || firstName == "Helene"
                    || firstName == "Morrigan"
                    || firstName == "Severina"
                    || firstName == "Lucretia"
                    || firstName == "Katarinya"
                )
                {
                    title = "Lady ";
                }
                else
                {
                    title = "Lord ";
                }
            }
            else
            {
                title = "";
            }

            string fullName = $"{firstName} {lastName}";
            string homeworld = _homeworlds[_random.Next(_homeworlds.Length)];
            string weapon = _weapons[_random.Next(_weapons.Length)];
            string achievement = _achievements[_random.Next(_achievements.Length)];
            int yearsOfService = _random.Next(15, 201); // Between 15 and 200 years

            return new InquisitorInfo
            {
                Name = fullName,
                FullTitle = $"{title}Inquisitor {fullName}",
                Ordo = $"Ordo {ordo}",
                Description =
                    $"A servant of the Emperor and member of the Ordo {ordo}, known for relentless pursuit of the Emperor's enemies.",
                Homeworld = homeworld,
                YearsOfService = yearsOfService,
                SignatureWeapon = weapon,
                NotableAchievement = achievement,
            };
        }

        /// <summary>
        /// Rolls dice with Warhammer 40k flavor text.
        /// </summary>
        /// <param name="diceCount">Number of dice to roll.</param>
        /// <param name="diceSides">Number of sides on each die.</param>
        /// <param name="flavor">Flavor of the roll (hit, wound, save).</param>
        /// <returns>Results of the dice roll.</returns>
        [McpTool("wh40k_roll_dice", "Roll dice with Warhammer 40k flavor")]
        public static DiceRollResult RollDice(
            [McpParameter(required: true, description: "Number of dice to roll")] int diceCount,
            [McpParameter(required: true, description: "Number of sides on each die")]
                int diceSides,
            [McpParameter(required: false, description: "Flavor of the roll (hit, wound, save)")]
                string flavor = "hit"
        )
        {
            if (diceCount <= 0 || diceCount > 100)
                throw new ArgumentException("Dice count must be between 1 and 100");

            if (diceSides <= 0 || diceSides > 20)
                throw new ArgumentException("Dice sides must be between 1 and 20");

            // Roll the dice
            List<int> rolls = new List<int>();
            for (int i = 0; i < diceCount; i++)
            {
                rolls.Add(_random.Next(1, diceSides + 1));
            }

            // Simple threshold based on flavor
            int threshold = flavor == "wound" ? 4 : 3; // 4+ for wounds, 3+ for others
            int successes = rolls.Count(r => r > threshold);

            string flavorText = flavor switch
            {
                "hit" => $"Your attack hits {successes} times!",
                "wound" => $"You wound your target {successes} times!",
                "save" => $"You successfully save {successes} wounds!",
                _ => $"You rolled {successes} successes.",
            };

            return new DiceRollResult
            {
                Rolls = rolls,
                Total = rolls.Sum(),
                FlavorText = flavorText,
            };
        }

        /// <summary>
        /// Simulates a simple battle between Imperial forces and enemies in the Warhammer 40k universe.
        /// This method is async to demonstrate how to implement async tools in MCP.
        /// </summary>
        /// <param name="imperialForce">The Imperial faction for the battle.</param>
        /// <param name="enemyForce">The enemy faction for the battle.</param>
        /// <returns>Results of the simulated battle.</returns>
        [McpTool("wh40k_battle_simulation", "Simulate a battle in the Warhammer 40k universe")]
        public static async Task<BattleResult> SimulateBattleAsync(
            [McpParameter(required: false, description: "Imperial force")]
                string imperialForce = "",
            [McpParameter(required: false, description: "Enemy force")] string enemyForce = ""
        )
        {
            // This is intentionally async to demonstrate async tool pattern
            await Task.Delay(500);

            string imperial = string.IsNullOrEmpty(imperialForce)
                ? _factions[_random.Next(_factions.Length)]
                : imperialForce;

            string enemy = string.IsNullOrEmpty(enemyForce)
                ? _enemies[_random.Next(_enemies.Length)]
                : enemyForce;

            string battlefield = _battlefields[_random.Next(_battlefields.Length)];

            // Simple 50/50 battle outcome
            bool imperialVictory = _random.Next(2) == 0;

            return new BattleResult
            {
                ImperialForce = imperial,
                EnemyForce = enemy,
                Battlefield = battlefield,
                IsImperialVictory = imperialVictory,
                BattleReport =
                    $"The {imperial} {(imperialVictory ? "defeated" : "were defeated by")} the {enemy} at {battlefield}.",
            };
        }
    }

    /// <summary>
    /// Information about a generated Inquisitor character.
    /// </summary>
    public class InquisitorInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullTitle { get; set; } = string.Empty;
        public string Ordo { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Homeworld { get; set; } = string.Empty;
        public int YearsOfService { get; set; }
        public string SignatureWeapon { get; set; } = string.Empty;
        public string NotableAchievement { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results of a dice roll with Warhammer 40k flavor.
    /// </summary>
    public class DiceRollResult
    {
        public List<int> Rolls { get; set; } = new List<int>();
        public int Total { get; set; }
        public string FlavorText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Results of a simulated Warhammer 40k battle.
    /// </summary>
    public class BattleResult
    {
        public string ImperialForce { get; set; } = string.Empty;
        public string EnemyForce { get; set; } = string.Empty;
        public string Battlefield { get; set; } = string.Empty;
        public bool IsImperialVictory { get; set; }
        public string BattleReport { get; set; } = string.Empty;
    }
}
