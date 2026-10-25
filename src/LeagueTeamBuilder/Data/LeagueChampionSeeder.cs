using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Data
{
    // Seeds the champion catalog ONCE (only while the table is empty), so champions the owner
    // later removes or renames are never re-added on restart. The list below was written from
    // memory and may miss the newest champions: add them in League Settings > Champions
    // (bulk-add accepts one name per line).
    public static class LeagueChampionSeeder
    {
        private static readonly string[] Names =
        {
            "Aatrox","Ahri","Akali","Akshan","Alistar","Ambessa","Amumu","Anivia","Annie","Aphelios","Ashe",
            "Aurelion Sol","Aurora","Azir","Bard","Bel'Veth","Blitzcrank","Brand","Braum","Briar","Caitlyn",
            "Camille","Cassiopeia","Cho'Gath","Corki","Darius","Diana","Dr. Mundo","Draven","Ekko","Elise",
            "Evelynn","Ezreal","Fiddlesticks","Fiora","Fizz","Galio","Gangplank","Garen","Gnar","Gragas",
            "Graves","Gwen","Hecarim","Heimerdinger","Hwei","Illaoi","Irelia","Ivern","Janna","Jarvan IV",
            "Jax","Jayce","Jhin","Jinx","K'Sante","Kai'Sa","Kalista","Karma","Karthus","Kassadin","Katarina",
            "Kayle","Kayn","Kennen","Kha'Zix","Kindred","Kled","Kog'Maw","LeBlanc","Lee Sin","Leona","Lillia",
            "Lissandra","Lucian","Lulu","Lux","Malphite","Malzahar","Maokai","Master Yi","Mel","Milio",
            "Miss Fortune","Mordekaiser","Morgana","Naafiri","Nami","Nasus","Nautilus","Neeko","Nidalee",
            "Nilah","Nocturne","Nunu & Willump","Olaf","Orianna","Ornn","Pantheon","Poppy","Pyke","Qiyana",
            "Quinn","Rakan","Rammus","Rek'Sai","Rell","Renata Glasc","Renekton","Rengar","Riven","Rumble",
            "Ryze","Samira","Sejuani","Senna","Seraphine","Sett","Shaco","Shen","Shyvana","Singed","Sion",
            "Sivir","Skarner","Smolder","Sona","Soraka","Swain","Sylas","Syndra","Tahm Kench","Taliyah",
            "Talon","Taric","Teemo","Thresh","Tristana","Trundle","Tryndamere","Twisted Fate","Twitch",
            "Udyr","Urgot","Varus","Vayne","Veigar","Vel'Koz","Vex","Vi","Viego","Viktor","Vladimir",
            "Volibear","Warwick","Wukong","Xayah","Xerath","Xin Zhao","Yasuo","Yone","Yorick","Yunara",
            "Yuumi","Zac","Zed","Zeri","Ziggs","Zilean","Zoe","Zyra",
        };

        public static async Task SeedAsync(ApplicationDbContext db)
        {
            if (await db.LeagueChampions.AnyAsync())
                return;

            db.LeagueChampions.AddRange(Names.Select(n => new LeagueChampion { Name = n, IsActive = true }));
            await db.SaveChangesAsync();
        }
    }
}
