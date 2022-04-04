using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web.Hosting;
using VatsimATCInfo.Helpers;
using VatsimTrafficNotify.Helpers;
using VatsimTrafficNotify.Models;

namespace VatsimTrafficNotify.Process
{
    public enum AlertType
    {
        Regional,
        Inbound,
        Outbound,
        High,
        Area,
        Airport,
        GroupFlight
    }

    public class FirstArrival
    {
        public string ArrivalAirport { get; set; }
        public TimeSpan ArrivalTime { get; set; }
    }

    public class TrafficAlert
    {
        public int AircraftCount { get; set; }
        public string Alert { get; set; }
        public string FirstArrivalLocation { get; set; }
        public string FirstArrivalTime { get; set; }
        public string FirstArrivalTimespan { get; set; }
        public string Message { get; set; }
        public string OutboundAirport { get; set; }
        public List<string> AirportList { get; set; }
        public List<Pilot> Planes { get; set; }
        public List<AirportInfo> BusyAirports { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Update { get; set; }
        
        public List<Pilot> Outbounds { get; set; }
        public List<Pilot> Inbounds { get; set; }
        public List<Pilot> Regionals { get; set; }        
    }

    public class TrafficNotify
    {
        private static List<TrafficAlert> _Alerts;
        private static GeoCoordinate _centerPoint = new GeoCoordinate(-26.051257, 24.781342);
        private static Config _config;
        //private static TrafficAlert _inboundAlert = null;
        //private static TrafficAlert _outboundAlert = null;
        //private static TrafficAlert _regionalAlert = null;
        //private static TrafficAlert _highTrafficAlert = null;
        private static TrafficAlert _areaTrafficAlert = null;
        private static TrafficAlert _airportTrafficAlert = null;
        private static TrafficAlert _groupFlightTrafficAlert = null;
        private static bool _running = false;
        private static Thread _thread;
        private static double _toNM = 0.000539957d;
        private static string _error = "";
        private static int _trafficFAOR = 0;
        private static int _trafficFACT = 0;
        private static int _trafficFALE = 0;
        private static int _trafficFYWH = 0;
        private static int _trafficFQMA = 0;        
        private static string _lastLog = "";

        public static object GetAlerts()
        {
            return new
            {
                Alerts = new List<TrafficAlert>()
                {
                    _areaTrafficAlert,
                    _groupFlightTrafficAlert,
                    _airportTrafficAlert                   
                },
                Regions = _config.RegionCodes,
                Error = _error,
                LastLog = _lastLog
            };
        }

        public static Config LoadConfig()
        {
            try
            {
                var configFile = File.ReadAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "config.json"));
                var config = JsonConvert.DeserializeObject<Config>(configFile);
                return config;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static void SetRegions(string[] regions)
        {
            _areaTrafficAlert = null;
            _groupFlightTrafficAlert = null;
            _airportTrafficAlert = null;
            _config.RegionCodes = regions.ToList();
        }

        public static void SetError(string error)
        {
            _error = error;
        }

        public static Config GetConfig()
        {
            return _config;
        }

        public static bool StartProcess()
        {
            try
            {
                _config = LoadConfig();
                if (_config == null)
                {
                    _error = $"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} No config file found";
                }
            }
            catch (Exception ex)
            {
                //// Use the defaults then
                //_config = new Config()
                //{
                //    RegionCodes = new List<string>() { "FA", "FY", "FB", "FV", "FQ", "FD", "FX" },
                //    AlertLevelRegional = 3,
                //    AlertLevelInbound = 3,
                //    AlertLevelOutbound = 3,
                //    AlertLevelGrow = 3,
                //    RegionName = "VATSSA",
                //    RegionCenterPoint = new double[] { -26.051257, 24.781342 },
                //    RegionRadius = 1680d
                //};
                _error = $"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} - {ex.Message} - {ex.InnerException}";
                return false;
            }
            _centerPoint = new GeoCoordinate(_config.RegionCenterPoint[0], _config.RegionCenterPoint[1]);
            DataStore.Initialize();
            try
            {
                ExternalComHelper.SetupDiscord(_config);
            }
            catch (Exception ex)
            {
                // Probably Discord crap
            }
            _thread = new Thread(() => Run());
            _thread.Start();
            return true;
        }

        public static void StopProcess()
        {
            ExternalComHelper.StopDiscord();
        }

        public static void UpdateConfig()
        {
            try
            {
                _config = LoadConfig();
                _centerPoint = new GeoCoordinate(_config.RegionCenterPoint[0], _config.RegionCenterPoint[1]);
            }
            catch (Exception ex)
            {
                // Use the current config
            };
        }

        private static FirstArrival CalculateFirstArrivalTime(IEnumerable<Pilot> flights)
        {
            var currentTimespan = TimeSpan.FromHours(23);
            var currentAirport = "";
            var airports = DataStore.GetAirports();

            flights.ToList().ForEach((plane) =>
            {
                var arrAirport = airports.FirstOrDefault(a => a.ICAO == plane.flight_plan.arrival);
                if (arrAirport == null)
                {
                    return;
                }

                var depAirport = airports.FirstOrDefault(a => a.ICAO == plane.flight_plan.departure);
                if (depAirport == null)
                {
                    return;
                }

                var planeCoord = new GeoCoordinate(plane.latitude, plane.longitude);
                var arrAirportCoord = new GeoCoordinate(arrAirport.Latitude, arrAirport.Longitude);
                var depAirportCoord = new GeoCoordinate(depAirport.Latitude, depAirport.Longitude);

                // If aircraft is in cruise
                var speed = 400;
                if (planeCoord.GetDistanceTo(depAirportCoord) > 55560
                && planeCoord.GetDistanceTo(arrAirportCoord) > 55560)
                {
                    speed = plane.groundspeed;
                }
                else if (planeCoord.GetDistanceTo(arrAirportCoord) > 55560)
                {
                    var check = int.TryParse(plane.flight_plan.cruise_tas, out speed);
                    if (!check)
                    {
                        speed = 400;
                    }
                }
                else
                {
                    speed = -1;
                }
                if (speed > 0)
                {
                    var nm = (double)planeCoord.GetDistanceTo(arrAirportCoord) * _toNM;
                    var hourDecimal = nm / speed;
                    var hourTimeSpan = TimeSpan.FromHours(hourDecimal);

                    if (hourTimeSpan < currentTimespan)
                    {
                        currentTimespan = hourTimeSpan;
                        currentAirport = plane.flight_plan.arrival;
                    }
                }
            });
            return new FirstArrival()
            {
                ArrivalTime = currentTimespan,
                ArrivalAirport = currentAirport
            };
        }

        private static List<Pilot> CheckPilotDistances(IEnumerable<Pilot> flights)
        {
            List<Pilot> newList = new List<Pilot>();
            foreach (var plane in flights)
            {
                var planeCoord = new GeoCoordinate(plane.latitude, plane.longitude);
                var disToAirspace = planeCoord.GetDistanceTo(_centerPoint) * _toNM;
                if (disToAirspace < _config.RegionRadius)
                {
                    newList.Add(plane);
                }
            }
            return newList;
        }

        private static string GetOutboundAirport(IEnumerable<Pilot> flights)
        {
            var result = flights.GroupBy(f => f.flight_plan.departure)
                .Select(itemGroup => new { Item = itemGroup.Key, Count = itemGroup.Count() })
                    .OrderByDescending(Item => Item.Count).ThenBy(Item => Item.Item).First().Item;
            return result;
        }

        private static void Run()
        {
            _running = true;

            if (_config.TelegramSendDebug)
            {
                ExternalComHelper.SendMessage("Restarted monitoring (v1.0)", _config);
            }

            //// IMAGE
            //var imageDir = AppDomain.CurrentDomain.BaseDirectory;
            //double[] testCoords = new double[] { -26.1392, 28.246 };
            //var url = $"https://stadiamaps.com/static/alidade_smooth_dark?api_key=045c51c2-5a59-488a-981c-953f134f4834&center={testCoords[0]},{testCoords[1]}&zoom=8&markers=-26.1392, 28.246,alidade_smooth_dark_sm&size=250x250@2x";
            //if (!Directory.Exists(Path.Combine(imageDir, "temp")))
            //{
            //    Directory.CreateDirectory(Path.Combine(imageDir, "temp"));
            //}
            //using (WebClient wc = new WebClient())
            //{
            //    wc.DownloadFile(url, Path.Combine(imageDir, "temp", "lastmap.png"));
            //}
            //Bitmap bm = new Bitmap(Path.Combine(imageDir, "temp", "lastmap.png"));
            //Graphics graphics = Graphics.FromImage(bm);
            //Pen pen = new Pen(Color.FromArgb(120, 252, 186, 3), 2);
            //graphics.DrawEllipse(pen, new Rectangle(120, 120, 10, 10));
            //bm.Save(Path.Combine(imageDir, "temp", "lastmap2.png"));
            //bm.Dispose();
            //graphics.Dispose();

            while (_running)
            {
                VatsimData vatsimData = new VatsimData();
                try
                {
                    vatsimData = Communication.DoCall<VatsimData>();

                    vatsimData.pilots = vatsimData.pilots.Where(p => p.flight_plan != null).ToList();
                    vatsimData.pilots = vatsimData.pilots.Where(p => !string.IsNullOrEmpty(p.flight_plan.arrival)
                                                                && !string.IsNullOrEmpty(p.flight_plan.departure)).ToList();
                    vatsimData.prefiles = vatsimData.prefiles.Where(pf => pf.flight_plan != null).ToList();

                    var allPlanesInRange = CheckPilotDistances(vatsimData.pilots);

                    // Alert, area traffic                    
                    if (_areaTrafficAlert != null)
                    {
                        if (allPlanesInRange.Count() < _config.AlertLevelArea - _config.AlertLevelGrow)
                        {
                            _areaTrafficAlert = null;
                        }
                        else if (allPlanesInRange.Count() >= _areaTrafficAlert.AircraftCount + _config.AlertLevelGrow)
                        {
                            var localOutbound = allPlanesInRange
                                .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();

                            var localInbound = allPlanesInRange
                                .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();

                            _areaTrafficAlert.AircraftCount = allPlanesInRange.Count();
                            _areaTrafficAlert.Update = true;
                            _areaTrafficAlert.Inbounds = localInbound;
                            _areaTrafficAlert.Outbounds = localInbound;
                            _areaTrafficAlert.Planes = allPlanesInRange.ToList();
                            ExternalComHelper.SendUpdate(_areaTrafficAlert, _config, true);
                        }
                    }
                    else
                    {
                        if (allPlanesInRange.Count() >= _config.AlertLevelArea)
                        {
                            var localOutbound = allPlanesInRange
                                .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();

                            var localInbound = allPlanesInRange
                                .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();
                            _areaTrafficAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = allPlanesInRange.Count(),
                                Alert = AlertType.Area.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                Outbounds = localOutbound,
                                Inbounds = localInbound,
                                Planes = allPlanesInRange.ToList()
                            };
                            Helpers.ExternalComHelper.SendUpdate(_areaTrafficAlert, _config);
                        }
                    }

                    // Alert, airport traffic
                    List<AirportInfo> highTrafficAirports = GetHighTraffic(allPlanesInRange);
                    if (_airportTrafficAlert != null)
                    {
                        if (!highTrafficAirports.Any())
                        {
                            _airportTrafficAlert = null;
                        }
                        else if (highTrafficAirports.Count() > _airportTrafficAlert.BusyAirports.Count()
                            || highTrafficAirports.Max(gf => gf.Count) >= _airportTrafficAlert.BusyAirports.Max(ba => ba.Count) + _config.AlertLevelGrow)
                        {
                            var localOutbounds = allPlanesInRange
                                .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))
                                && highTrafficAirports.Any(hta => hta.Icao == p.flight_plan.departure)
                                ).ToList();

                            var localInbounds = allPlanesInRange
                                .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))
                                && highTrafficAirports.Any(hta => hta.Icao == p.flight_plan.arrival)
                                ).ToList();

                            _airportTrafficAlert.Inbounds = localInbounds;
                            _airportTrafficAlert.Outbounds = localInbounds;
                            _airportTrafficAlert.Update = true;
                            _airportTrafficAlert.BusyAirports = highTrafficAirports;
                            ExternalComHelper.SendUpdate(_airportTrafficAlert, _config, true);
                        }
                    }
                    else
                    {
                        if (highTrafficAirports.Count() > 0)
                        {
                            var localOutbound = allPlanesInRange
                                .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();

                            var localInbound = allPlanesInRange
                                .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();
                            _airportTrafficAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing at airports.",
                                AircraftCount = allPlanesInRange.Count(),
                                Alert = AlertType.Airport.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                Outbounds = localOutbound,
                                Inbounds = localInbound,
                                Planes = allPlanesInRange.ToList(),
                                BusyAirports = highTrafficAirports
                            };
                            Helpers.ExternalComHelper.SendUpdate(_airportTrafficAlert, _config);
                        }
                    }

                    // Alert, groupflights traffic
                    List<AirportInfo> groupFlights = GetGroupFlights(vatsimData.pilots);
                    if (_groupFlightTrafficAlert != null)
                    {
                        if (!groupFlights.Any())
                        {
                            _groupFlightTrafficAlert = null;
                        }
                        else if (groupFlights.Where(gf => gf.Count > _config.AlertLevelGroupflight).Count() > _groupFlightTrafficAlert.BusyAirports.Count()
                            || groupFlights.Max(gf => gf.Count) >= _groupFlightTrafficAlert.BusyAirports.Max(ba => ba.Count) + _config.AlertLevelGrow)
                        {
                            _groupFlightTrafficAlert.Update = true;
                            _groupFlightTrafficAlert.BusyAirports = groupFlights;
                            ExternalComHelper.SendUpdate(_groupFlightTrafficAlert, _config, true);
                        }
                    }
                    else
                    {
                        if (groupFlights.Where(gf => gf.Count > _config.AlertLevelGroupflight).Count() > 0)
                        {
                            _groupFlightTrafficAlert = new TrafficAlert()
                            {
                                Message = "Groupflights detected.",
                                AircraftCount = groupFlights.Where(gf => gf.Count > _config.AlertLevelGroupflight).Count(),
                                Alert = AlertType.GroupFlight.ToString(),
                                Timestamp = DateTime.Now,
                                BusyAirports = groupFlights.Where(gf => gf.Count > _config.AlertLevelGroupflight).ToList(),
                                Update = true                                
                            };
                            Helpers.ExternalComHelper.SendUpdate(_groupFlightTrafficAlert, _config);
                        }
                    }

                    if (_config.NotifyDiscord)
                    {
                        ExternalComHelper.UpdateDiscord(allPlanesInRange.Count(), _config);
                    }

                    try
                    {
                        var curDir = AppDomain.CurrentDomain.BaseDirectory;
                        if (!Directory.Exists(Path.Combine(curDir, "logs")))
                        {
                            Directory.CreateDirectory(Path.Combine(curDir, "logs"));
                        }
                        using (var sr = new StreamWriter($"{Path.Combine(curDir, "logs", $"stats{DateTime.Today:ddMMyyyy}.csv")}", true))
                        {
                            // Time, Flights in Region, FAOR tfc, FACT tfc, FALE tfc, Inbounds, Outbounds, Regional
                            var time = DateTime.Now.ToString("hh:mm:ss");
                            var flightsInRegion = allPlanesInRange.Count();
                            int localOutboundCount = allPlanesInRange
                                .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList().Count();

                            int localInboundCount = allPlanesInRange
                                .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList().Count();
                            int regional = allPlanesInRange.Count() - localInboundCount - localOutboundCount;
                            var str = $"{time},{flightsInRegion},{_trafficFAOR},{_trafficFACT},{_trafficFALE},{localInboundCount},{localOutboundCount},{regional},{_trafficFQMA},{_trafficFYWH}";
                            sr.WriteLine(str);
                            _lastLog = str;
                        }
                    }
                    catch (Exception ex)
                    {
                        // WHYYY
                    }

                   
                }
                catch (Exception ex)
                {
                    _error = $"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} - {ex.Message} - {ex.InnerException}";
                }
                Thread.Sleep(10000);
            }
        }

        private static List<AirportInfo> GetGroupFlights(List<Pilot> fullList)
        {
            List<AirportInfo> groupFlightList = new List<AirportInfo>();
            var planes = fullList.Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                                || _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2))).ToList();
            var grouped = planes.Select(p => p.flight_plan.departure + "-" + p.flight_plan.arrival)
                .GroupBy(ba => ba)
                .Select(itemGroup => new { Item = itemGroup.Key, Count = itemGroup.Count() }).OrderByDescending(bap => bap.Count);
            var list = grouped.Where(g => g.Count > 1).ToList();            
            foreach (var item in list)
            {
                var apName = item.Item.ToString().Split('-');
                var isInbound = _config.RegionCodes.Any(c => c == apName[1].ToUpper().Substring(0, 2));
                var info = new AirportInfo()
                {
                    Icao = (isInbound ? apName[1] : apName[0]),
                    Count = item.Count,
                    Route = $"{apName[0]}->{apName[1]}"
                };
                if (isInbound)
                {
                    var closestList = planes.Where(a => a.flight_plan.arrival.ToUpper() == apName[1] && a.flight_plan.departure == apName[0]).ToList();
                    var firstTimespan = CalculateFirstArrivalTime(closestList);
                    var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    info.FirstArrivalTime = firstArrival.ToString("HH:mm");
                    info.FirstArrivalTimespan = firstArrivalString;                    
                }
                groupFlightList.Add(info);
            }
            return groupFlightList;

        }

        private static List<AirportInfo> GetHighTraffic(List<Pilot> planes)
        {
            var highTrafficList = new List<AirportInfo>();
            var airports = DataStore.GetAirports();
            
            // Fix airports lenghts to icao airports not in america for now
            airports = airports.Where(a => a.ICAO.Length == 4).ToList();            
            airports = airports.Where(a => _config.RegionCodes.Any(c => c == a.ICAO.ToUpper().Substring(0, 2))).ToList();

            foreach (var airport in airports)
            {
                var airportLocation = new GeoCoordinate(airport.Latitude, airport.Longitude);               
                var planesInRange = planes.Where(p => 
                    new GeoCoordinate(p.latitude, p.longitude)
                    .GetDistanceTo(airportLocation) * _toNM < 100
                    && (p.flight_plan.departure == airport.ICAO || p.flight_plan.arrival == airport.ICAO))
                    .ToList();
                if (planesInRange.Count >= _config.AlertLevelAirport)
                {
                    var localOutbounds = planesInRange
                        .Where(p => p.flight_plan.departure.ToUpper() == airport.ICAO).ToList();

                    var localInbounds = planesInRange
                        .Where(p => p.flight_plan.arrival.ToUpper() == airport.ICAO).ToList();

                    highTrafficList.Add(new AirportInfo()
                    {
                        Count = planesInRange.Count(),
                        Icao = airport.ICAO,
                        InboundsCount = localInbounds.Count(),
                        OutboundsCount = localOutbounds.Count()
                    });
                }
                if (airport.ICAO == "FAOR")
                {
                    _trafficFAOR = planesInRange.Count();
                }
                if (airport.ICAO == "FALE")
                {
                    _trafficFALE = planesInRange.Count();
                }
                if (airport.ICAO == "FACT")
                {
                    _trafficFACT = planesInRange.Count();
                }
                if (airport.ICAO == "FYWH")
                {
                    _trafficFYWH = planesInRange.Count();
                }
                if (airport.ICAO == "FQMA")
                {
                    _trafficFQMA = planesInRange.Count();
                }
            }
            return highTrafficList;
        }

        private static void RunNotifications()
        {
        }
    }
}