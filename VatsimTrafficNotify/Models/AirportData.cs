using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VatsimTrafficNotify.Models
{
    public class AirportData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string Country { get; set; }
        public string IATA { get; set; }
        public string ICAO { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Altitude { get; set; }
        public List<Runway> Runways { get; set; }
        public string CountryCode { get; set; }
        public string Municipality { get; set; }
        public string Continent { get; set; }
        //public Airspace CTR { get; set; }
    }

    public class Runway
    {
        public string ICAO { get; set; }
        public string Primary { get; set; }
        public int PrimaryDegrees { get; set; }
        public string Secondary { get; set; }
        public int SecondaryDegrees { get; set; }
        public double PrimaryLat { get; set; }
        public double PrimaryLon { get; set; }
        public double SecondaryLat { get; set; }
        public double SecondaryLon { get; set; }
    }
}