using Boerman.OpenAip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public static Airspace GetAirspace(string countryCode, string name, string suffix)
        {
            var path = Path.Combine(HostingEnvironment.ApplicationPhysicalPath, $"openaipdata", $"{countryCode}_asp.aip");

            if (File.Exists(path))
            {
                var data = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(data))
                {
                    var countryAirspaceData = Boerman.OpenAip.Parsers.Airspace.Parse(data).ToList();
                    if (countryAirspaceData.Any())
                    {
                        var result = countryAirspaceData.FirstOrDefault(a =>
                            a.Name.ToUpper().Contains(name.ToUpper())
                            &&
                                (
                                    a.Name.ToUpper().Contains(" CTLZ")
                                    || a.Name.ToUpper().Contains(" CLTZ")
                                    || a.Name.ToUpper().Contains(" CLASS B")
                                    || a.Name.ToUpper().Contains(" CTR")
                                    || a.Name.ToUpper().Contains(" CONTROL ZONE")
                                )
                            );

                        return result;
                    }
                    else
                        return null;
                }
            }
            else
            {
                throw new Exception("Path: " + path);
            }
            return null;
        }
    }
}