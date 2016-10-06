using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TripPlanner
{
    public class Program
    {
        static Random _random = new Random();

        public static void Main(string[] args)
        {
            try
            {
               var destinations = @"Innsbruck, Austria
Munich, Germany
Pag, Croatia
Venice, Italy
Tuscany, Italy
Florence, Italy
Rome, Italy
Vatican City
Pompeii, Italy
Gozo, Malta
Dubrovnik, Croatia
Santorini, Thira, Greece
Vienna, Austria
Prague, Czech Republic
Krakow, Poland
Berlin, Germany
Amsterdam, Netherlands
Keukenhof, Stationsweg, Lisse, Netherlands
Glasgow, United Kingdom
Edinburgh, United Kingdom
Inverness, United Kingdom
Stonehenge, Amesbury, United Kingdom
London, United Kingdom
Brussels, Belgium
Paris, France
Pamplona, Spain
Lagos, Portugal
Granada, Spain
Barcelona, Spain
Luberone, Bonnieux, France
Nice, France
Monte Carlo, Monaco
Interlaken, Switzerland".Replace("\r", "").Split('\n').ToList();

                //Grab API key
                var apikey = "INSERTAPIKEY";
                if (File.Exists("apikey.ignore"))
                    apikey = File.ReadAllText("apikey.ignore");

#if DEBUG
                Debugger.Launch();
#endif

                // Load destinations from file
                if (File.Exists("destinations.txt"))
                    destinations = File.ReadAllText("destinations.txt").Replace("\r","").Split('\n').ToList();

                // Generate distance matrix if not already present 
                // TODO: do a date check or something to see if regeneration is needed.
                var path_distances = "distances.txt";
                if(File.Exists(path_distances) == false)
                    SaveDistancesToFile(destinations, path_distances, apikey);

                var distanceMatrix = GenerateDistanceDictionary(ReadAsCSV(path_distances).Skip(1).ToList());

                int generations = 10000;
                int populationSize = 1000;
                int survivorSizePerc = 20;
                
                var shortest = RunDarwinian(destinations, distanceMatrix, generations, populationSize, survivorSizePerc);
                Console.Write("Shortest path found(generations {0}, populationsize {1}, survivor% {2}:", generations, populationSize, survivorSizePerc);
                Console.WriteLine(shortest.ToString());
                
#if DEBUG
                Console.WriteLine("Enter to exit.");
                Console.ReadLine();
#endif

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ReadLine();
            }
        }

        private static Route RunDarwinian(List<string> destinations, SortedDictionary<string, int> distanceMatrix, int generations = 1000, int populationSize = 1000, int survivorSizePerc = 10)
        {
            Route shortestRoute = null;
            int nonImprovedCount = 0;

            // generate an initial randomized population
            var routes = new List<Route>();
            routes.Add(new Route(destinations, distanceMatrix)); // add unshuffled item
            for (int x = 0; x < populationSize; x++) routes.Add(GenerateShuffledRoute(destinations, distanceMatrix));


            // Run generational mutations
            for (int generation = 0; generation < generations; generation++)
            {
                var shortestInThisPopulation = routes.OrderBy(x => x.Distance).First();
                if (shortestRoute == null) shortestRoute = shortestInThisPopulation;

                if (shortestInThisPopulation.Distance < shortestRoute.Distance)
                {
                    shortestRoute = shortestInThisPopulation;
                    nonImprovedCount = 0;
                }

                if (generation%100 == 0)
                    Console.WriteLine(
                        $"Generation {generation} evolved a route {shortestInThisPopulation.Distance} long.  The shortest so far is {shortestRoute.Distance}");

                // grab the shortest survivors.
                var shortestSurvivors = routes.OrderBy(x => x.Distance).Take(populationSize/survivorSizePerc).ToList();

                // rebuild population
                var tmpRoutes = new List<Route>();
                tmpRoutes.Add(shortestRoute); // add shortest item

                // SUPERMAN theory - mutate shortest path multiple times to seed next generation
                while (tmpRoutes.Count < populationSize/survivorSizePerc)
                    tmpRoutes.Add(Route.MutateRoute(shortestRoute, _random, 1));
                while (tmpRoutes.Count < (populationSize/survivorSizePerc)*2)
                    tmpRoutes.Add(Route.MutateRoute(shortestInThisPopulation, _random, 1));

                // Mutate shortest survivors a bit and add those too
                tmpRoutes.AddRange(shortestSurvivors.Select(x => Route.MutateRoute(x, _random, 1)));
                tmpRoutes.AddRange(shortestSurvivors.Select(x => Route.MutateRoute(x, _random, 2)));
                tmpRoutes.AddRange(shortestSurvivors.Select(x => Route.MutateRoute(x, _random, 3)));
                tmpRoutes.AddRange(shortestSurvivors.Select(x => Route.MutateRoute(x, _random, 5)));
                tmpRoutes.AddRange(shortestSurvivors.Select(x => Route.MutateRoute(x, _random, 10)));

                // Add some random entries to fill the population
                while (tmpRoutes.Count < populationSize)
                    tmpRoutes.Add(GenerateShuffledRoute(destinations, distanceMatrix));

                //Interlocked.Add(routes, tmpRoutes);
                routes = (tmpRoutes);
            }

            Console.WriteLine($"All generations have executed.  The shortest path found is {shortestRoute.Distance}.");
            return shortestRoute;
        }

        private static SortedDictionary<string, int> GenerateDistanceDictionary(IEnumerable<string[]> distanceList)
        {
            var dict = new SortedDictionary<string, int>();
            foreach (var distancePair in distanceList)
            {
                dict.Add(Tokenize(distancePair[0], distancePair[1]), int.Parse(distancePair[2]));
            }
            return dict;

            //return distanceList.ToDictionary(distanceItem => Tokenize(distanceItem[0], distanceItem[1]), distanceItem => int.Parse(distanceItem[2]));
        }

       private static SortedDictionary<int, List<string>> CalculatePopulationDistances(List<List<string>> populations, SortedDictionary<string, int> distanceList)
        {
            var scores = new SortedDictionary<int, List<string>>();
            foreach (var population in populations)
            {
                var currentLocation = "";
                var distanceTotal = 0;
                foreach (var destination in population)
                {
                    if (currentLocation == "")
                    {
                        currentLocation = destination;
                        continue;
                    }
                    distanceTotal += distanceList[Tokenize(currentLocation, destination)];
                }
                scores.Add(distanceTotal, population);
            }
            return scores;
        }

        private static Route GenerateShuffledRoute(List<string> destinations, SortedDictionary<string, int> distanceMatrix)
        {
            List<string> shuffled = Shuffle(Clone(destinations));
            return new Route(shuffled, distanceMatrix);
        }

        public static string Tokenize(string s, string s1)
        {
            return (s + s1).Replace(" ", "_").Replace("\r", "");
        }

        private static List<string> Clone(List<string> source)
        {
            List<string> tmp = new List<string>();
            foreach (var destination in source)
            {
                tmp.Add(destination);
            }
            return tmp;
        }

        public static List<string> Shuffle(List<string> source)
        {
            int n = source.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                string value = source[k];
                source[k] = source[n];
                source[n] = value;
            }
            return source;
        }


        //even cooler as an extension method
        static IEnumerable<string[]> ReadAsCSV(string filename, char seperator = '\t')
        {
            using (var reader = new StreamReader(new FileStream(filename, FileMode.Open)))
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var data = line.Split('\t');
                    yield return data;
                }
        }

        /// <summary>
        /// Get a list of distances between all points in the provided destinations file.
        /// N^2-N calls will be made.
        /// </summary>
        /// <param name="destinations"></param>
        /// <param name="pathDistances"></param>
        private static void SaveDistancesToFile(List<string> destinations, string pathDistances, string apikey)
        {
            Console.WriteLine("Gathering distances.");

            using (var writer = new StreamWriter(new FileStream(pathDistances, FileMode.Create))) 
            {
                string header = "destinationA\tdestinationB\tdistance";
                writer.Write(header);

                foreach (var destinationA in destinations)
                {
                    foreach (var destinationB in destinations)
                    {
                        if (destinationA.Equals(destinationB)) continue;
                        try
                        {
                            var distance = GetDistanceFromGoogle(destinationA, destinationB, apikey);
                            writer.Write("\n" + destinationA + "\t" + destinationB +"\t"+distance);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error when getting distances for {0} and {1}", destinationA, destinationB);
                            Console.WriteLine(ex.Message);
                        }
                    }
                }
            }
        }

        private static int GetDistanceFromGoogle(string destinationA, string destinationB, string apikey)
        {
            using (var client = new HttpClient())
            {
                var response = client.GetAsync("http://maps.googleapis.com/maps/api/distancematrix/json?origins="
                                               + destinationA + "&destinations=" + destinationB
                                               + "&apikey=" + apikey
                                               + "&mode=driving&language=us-en&sensor=false&units=metric");

                var responseString = response.Result.Content.ReadAsStringAsync().Result;
                dynamic data = JsonConvert.DeserializeObject(responseString);
                return data.rows[0].elements[0].distance.value;
            }

        }
    }

    internal class Route
    {
        public static string Tokenize(string s, string s1)
        {
            return (s + s1).Replace(" ", "_").Replace("\r", "");
        }

        public List<string> Destinations { get; set; }

        public SortedDictionary<string, int> DistanceMatrix { get; set; }

        public List<Leg> Legs { get; set; }

        public Route()
        {
            Legs = new List<Leg>();
        }

        public Route(List<string> destinations, SortedDictionary<string, int> distanceMatrix)
        {
            Legs = new List<Leg>();
            Destinations = destinations;
            DistanceMatrix = distanceMatrix;
            CalculateRoute();
        }

        public static Route MutateRoute(Route route, Random random, int mutations)
        {
            // Breeding!
            var newRoute = new Route(route.Destinations, route.DistanceMatrix);

            var tmp = "";
            for (int mut = 0; mut < mutations; mut++)
            {
                var x = random.Next(0, newRoute.Destinations.Count - 1);
                var y = random.Next(0, newRoute.Destinations.Count - 1);
                tmp = newRoute.Destinations[x];
                newRoute.Destinations[x] = newRoute.Destinations[y];
                newRoute.Destinations[y] = tmp;
            }
            newRoute.CalculateRoute();
            return newRoute;
        }

        private void CalculateRoute()
        {
            Legs = new List<Leg>();
            string currentLocation = "";
            foreach (var destination in Destinations)
            {
                if (currentLocation == "")
                {
                    currentLocation = destination;
                    continue;
                }
                var leg = new Leg();
                leg.Start = currentLocation;
                leg.Finish = destination;
                leg.Distance = DistanceMatrix[Tokenize(currentLocation, destination)];
                Legs.Add(leg);
                currentLocation = destination;
            }
        }

        public int Distance
        {
            get
            {
                return Legs.Sum(x => x.Distance);
            } 
        }

        public override string ToString()
        {
            string st = $"{this.GetHashCode()} - Route is {Distance/1000}km long ";
            Legs.ForEach(x=> st = st + "\n"+x.ToString());

            return st;
        }
    }

    internal class Leg
    {
        public string Start { get; set; }
        public string Finish { get; set; }
        public int Distance { get; set; }

        public override string ToString()
        {
            return this.Start + " --> " + this.Finish + " - " + this.Distance + " ";
        }
    }
}
