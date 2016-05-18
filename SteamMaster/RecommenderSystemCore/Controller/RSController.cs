using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DatabaseCore;
using DatabaseCore.lib.converter.models;
using Filter_System.Filter_Core.Filters_2._0;
using GameRank;
using RecommenderSystemCore.User_Data_Handling.Models;
using SteamSharpCore.steamUser.models;
using SteamUI;

namespace RecommenderSystemCore.Controller
{
    class RSController
    {
        public RSController(SteamTheme ui)
        {
            database = new Database();
            UI = ui;

            UI.RecommendButtonClick += ExecuteRecommendation;

        }

        private GRGameRank GameRank { get; set; }

        private Database database { get; set; }

        SteamTheme UI { get; set; }

        public void Start()
        {
            UI.Show(); 
        }

        private List<Game> ExecuteRecommendation(string steamID)
        {
            Dictionary<string, double> precalculations = ReadFromFile(); //This should read from the database
            UserWorkClass User = new UserWorkClass(steamID);

            Dictionary<int, Game> dbGames = database.FindAllGames();
            dbGames = RemoveGamesWithBlacklistedWords(dbGames);
            dbGames = RemoveDLCFromList(dbGames);
            List<Game> RecommenderList = new List<Game>();
            int iterations = Convert.ToInt32(100+User.userListGameList.Where(game => game.playtime_forever > 0).ToList().Count*0.3);

            Console.WriteLine($"Iterations to be performed: {iterations}");
            List<double> accuracyList = new List<double>();


            for (int i = 0; i < iterations ; i++)
            {
                Tuple<List<int>, List<UserGameTime.Game>> removedData = RemoveGamesFromUserGames(User.userListGameList);

               // writeToFile(removedData.Item2, "UserGamesListWithRemoval");

                GameRank = new GRGameRank(dbGames, removedData.Item2);
                // GameRank = new GRGameRank(dbGames, User.userListGameList);

                RecommenderList = GameRank.GetRankedGameList(precalculations);
                RecommenderList = FilterManagement(RecommenderList, User);
                RecommenderList.Sort();
                accuracyList.Add(CalculateAccuracy(removedData.Item1, RecommenderList,
                    Convert.ToInt32((User.userListGameList.Count*2)*0.1)));
                Console.WriteLine($"Iteration {i}");
                Console.WriteLine($"Current accuracy: {accuracyList.Sum() / accuracyList.Count}");
            }

            double finalAccuracy = accuracyList.Sum() / accuracyList.Count;
            Console.WriteLine($"Overall accuracy: {finalAccuracy}");

            return RecommenderList;
        }

        private Dictionary<string, double> ReadFromFile()
        {
            //File can be found in the main GameRank folder. Should be placed in your My Documents folder
            DirectoryInfo fileFolder = new DirectoryInfo(Directory.GetCurrentDirectory()).Parent.Parent.Parent;
            string path = fileFolder.FullName + @"\TagsAndRanks.txt";
            StreamReader reader = new StreamReader(path);
            Dictionary<string, double> result = new Dictionary<string, double>();
            while (!reader.EndOfStream)
            {
                string[] line = reader.ReadLine().Split(':');
                string tag = line[0];
                double tagGameRank = double.Parse(line[1]);
                
                result.Add(tag,tagGameRank);
            }
            reader.Close();
            return result;

        }

        private Dictionary<int, Game> RemoveGamesWithBlacklistedWords(Dictionary<int, Game> dbGames)
        {
            List<int> bannedGames = new List<int>();
            //The linq though
            foreach (Game game in dbGames.Values)
            {
                string[] segments = game.Title.Split(' ');
                if (segments.Any(CheckSegment))
                {
                    bannedGames.Add(game.SteamAppId);
                }
            }
            foreach (int id in bannedGames)
            {
                dbGames.Remove(id);
            }
            return dbGames;
        }

        private bool CheckSegment(string segment)
        {
            string currentSegment = segment.Where(char.IsLetter).Aggregate("", (current, ch) => current + ch).ToLower();
            List<string> banList = new List<string>()
            {
                "soundtrack",
                "sdk",
                "dlc",
                "demo"
            };
            return banList.Any(banWord => currentSegment.Contains(banWord));
        }

        private List<Game> FilterManagement(List<Game> InputList, UserWorkClass User)
        {
            //Controls the weight of the filters
            double MostOwnedValue = 1;
            double AvgPlayedForeverValue = 0;
            double AvgPlayTime2WeeksValue = 0;
            double Metacritic = 1;
            double InputListValue = 1; //In this case Pagerank

            double active = MostOwnedValue + AvgPlayedForeverValue + AvgPlayTime2WeeksValue + Metacritic +
                            InputListValue;


            if (active > 0)
            {
                #region FilterExecution

                InputList.Sort((x, y) => x.RecommenderScore.CompareTo(y.RecommenderScore));

                int score = 1;
                foreach (var game in InputList)
                {
                    game.RecommenderScore = score * InputListValue;
                    score++;
                }

                //Dictionary<int, double> recommenderScoreDictionary = InputList.ToDictionary(game => game.SteamAppId, game => game.RecommenderScore);

                //foreach (var game in InputList)
                //{
                //    game.RecommenderScore = 0;
                //}

              //  writeToFile(InputList, "pageRank");

                GameFilterX StandardGameFilter = new GameFilterX();
                PlayerGameFilterX PlayerGameRemoval = new PlayerGameFilterX();


                StandardGameFilter.OwnerCount(MostOwnedValue);
                InputList = StandardGameFilter.Execute(InputList);
             //   writeToFile(InputList, "MostOwned");

                StandardGameFilter.AvgPlayTimeForever(AvgPlayedForeverValue);
                InputList = StandardGameFilter.Execute(InputList);

                StandardGameFilter.AvgPlayTime2Weeks(AvgPlayTime2WeeksValue);
                InputList = StandardGameFilter.Execute(InputList);

                StandardGameFilter.MetaCritic(Metacritic);
                InputList = StandardGameFilter.Execute(InputList);
              //  writeToFile(InputList, "MetaCritic");

               // InputList = PlayerGameRemoval.Execute(InputList, User.DBGameList);

                //foreach (var game in InputList)
                //{
                //    int appID = game.SteamAppId;
                //    if (recommenderScoreDictionary.ContainsKey(appID))
                //    {
                //        game.RecommenderScore *= recommenderScoreDictionary[appID] * InputListValue;
                //    }
                //}

                InputList.Sort();
                #endregion
            }
           
            writeToFile(InputList, "End");
            return InputList;
        }

        public Dictionary<int, Game> RemoveDLCFromList(Dictionary<int, Game> gameDictionary)
        {
            List<int> gameDLCList = gameDictionary.SelectMany(game => game.Value.DLC).ToList();

            foreach (var appID in gameDLCList)
            {
                if (gameDictionary.ContainsKey(appID))
                {
                    gameDictionary.Remove(appID);
                }
            }

            return gameDictionary;
        }

        public void writeToFile(List<Game> inputList, string fileName)
        {
            DirectoryInfo fileFolder = new DirectoryInfo(Directory.GetCurrentDirectory());
            string path = fileFolder.FullName;
            StreamWriter writer = new StreamWriter(path + "\\" + fileName + ".txt", false);

            int i = 1;
            foreach (var game in inputList)
            {
                writer.WriteLine($"{i} : {game.Title} : {game.SteamAppId}");
                i++;
            }

            writer.Close();
        }

        public void writeToFile(List<UserGameTime.Game> inputList, string fileName)
        {
            Dictionary<int, Game> allGames = database.FindAllGames();
            DirectoryInfo fileFolder = new DirectoryInfo(Directory.GetCurrentDirectory());
            string path = fileFolder.FullName;
            StreamWriter writer = new StreamWriter(path + "\\" + fileName + ".txt", false);

            int i = 1;
            foreach (var game in inputList)
            {
                Game workGame;
                if (allGames.TryGetValue(game.appid, out workGame))
                {
                    writer.WriteLine($"ID:{game.appid} Title: {workGame.Title} PTForever: {game.playtime_forever}");
                    i++;

                }
            }

            writer.Close();
        }

        public Tuple<List<int>, List<UserGameTime.Game>> RemoveGamesFromUserGames(List<UserGameTime.Game> originalList)
        {
            originalList = originalList.Where(game => game.playtime_forever > 0).ToList();
            int fullRange = originalList.Count;
            Random random = new Random();
            int testRangelength = Convert.ToInt32(fullRange * 0.1);

            List<UserGameTime.Game> returnList = new List<UserGameTime.Game>(originalList);
            List<UserGameTime.Game> removeList = new List<UserGameTime.Game>();

            for (int i = 0; i < testRangelength; i++)
            {
                int removeAt = random.Next(0, fullRange-i);
                removeList.Add(returnList[removeAt]);
                returnList.Remove(returnList[removeAt]);
            }

            writeToFile(removeList, "RemovedList");
            writeToFile(originalList, "OriginalList");

            List<int> removeIDs = removeList.Select(game => game.appid).ToList();

         //   Console.WriteLine($"Original amount of games: {fullRange}");

            return new Tuple<List<int>, List<UserGameTime.Game>>(removeIDs, returnList);
        }

        public double CalculateAccuracy(List<int> removedGamesIDs, List<Game> recommenderList, int accuracyRange)
        {
            Dictionary<int, int> positionAppID = new Dictionary<int, int>();
            int recommendationRange = 30 + accuracyRange;

            int value = 1;
            foreach (var game in recommenderList)
            {
                positionAppID.Add(game.SteamAppId, value);
                value++;
            }

            List<double> accuracy = new List<double>();
            foreach (var removedGame in removedGamesIDs)
            {
                int position;
                if(positionAppID.TryGetValue(removedGame, out position) && position < recommendationRange)
                {
                    accuracy.Add(100);
                }
                //else if (position < recommendationRange + accuracyRange)
                //{
                //    accuracy.Add(50);
                //}
                else
                {
                    accuracy.Add(0);
                }
            }

            double finalAccuracy = accuracy.Sum()/accuracy.Count;

            return finalAccuracy;
        }
    }
}
