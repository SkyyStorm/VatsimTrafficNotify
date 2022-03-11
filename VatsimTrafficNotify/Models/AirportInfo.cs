using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VatsimTrafficNotify.Models
{
    public class AirportInfo
    {
        public string Icao { get; set; }
        public int Count { get; set; }
        public int InboundsCount { get; set; }
        public int OutboundsCount { get; set; }
        public string FirstArrivalTime { get; set; }
        public string FirstArrivalTimespan { get; set; }
    }
}