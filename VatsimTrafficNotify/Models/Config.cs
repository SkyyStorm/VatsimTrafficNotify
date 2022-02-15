using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VatsimTrafficNotify.Models
{
    public class Config
    {
        public string RegionName { get; set; }
        public List<string> RegionCodes { get; set; }
        public double[] RegionCenterPoint { get; set; }
        public double RegionRadius { get; set; }
        public int AlertLevelRegional { get; set; }
        public int AlertLevelInbound { get; set; }
        public int AlertLevelOutbound { get; set; }
        public int AlertLevelGrow { get; set; }

    }
}