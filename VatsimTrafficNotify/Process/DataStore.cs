using System.Collections.Generic;
using System.IO;
using System.Web.Hosting;
using VatsimTrafficNotify.Models;

namespace VatsimTrafficNotify.Process
{
    public class DataStore
    {
        private static List<AirportData> _airports = new List<AirportData>();

        public static void Initialize()
        {
            LoadAirports();
        }

        public static List<AirportData> GetAirports()
        {
            return _airports;
        }

        private static void LoadAirports()
        {
            var mainFile = File.ReadAllLines(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "airports_new.dat"));
            var airportData = new List<AirportData>();
            var c = 0;
            foreach (var item in mainFile)
            {
                c++;
                var split = item.Split(',');
                if (split.Length == 10)
                {
                    var counter = 0;
                    airportData.Add(new AirportData()
                    {
                        Id = NextNumber(ref counter),
                        Name = split[0],
                        ShortName = split[1],
                        CountryCode = split[2],
                        IATA = split[3],
                        ICAO = split[4],
                        Latitude = double.Parse(split[5]),
                        Longitude = double.Parse(split[6]),
                        Altitude = (!string.IsNullOrEmpty(split[7]) ? int.Parse(split[7]) : 0),
                        Continent = split[8],
                        Municipality = split[9],
                    });
                }
            }
            _airports = airportData;
        }

        private static int NextNumber(ref int counter)
        {
            counter++;
            return counter;
        }
    }
}