using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Device.Location;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Hosting;
using VatsimATCInfo.Helpers;
using VatsimTrafficNotify.Models;

namespace VatsimTrafficNotify.Process
{
    public enum AlertType
    {
        Regional,
        Inbound,
        Outbound,
        High
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
    }

    public class TrafficNotify
    {
        private static List<TrafficAlert> _Alerts;
        private static GeoCoordinate _centerPoint = new GeoCoordinate(-26.051257, 24.781342);
        private static Config _config;
        private static TrafficAlert _inboundAlert = null;
        private static TrafficAlert _outboundAlert = null;
        private static TrafficAlert _regionalAlert = null;
        private static TrafficAlert _highTrafficAlert = null;
        private static bool _running = false;
        private static Thread _thread;
        private static double _toNM = 0.000539957d;

        public static object GetAlerts()
        {
            return new
            {
                Alerts = new List<TrafficAlert>()
                {
                    _regionalAlert,
                    _inboundAlert,
                    _outboundAlert,
                    _highTrafficAlert
                },
                Regions = _config.RegionCodes
            };
        }

        public static Config GetConfig()
        {
            var configFile = File.ReadAllText(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "config.json"));
            var config = JsonConvert.DeserializeObject<Config>(configFile);
            return config;
        }

        public static void SetRegions(string[] regions)
        {
            _regionalAlert = null;
            _inboundAlert = null;
            _outboundAlert = null;
            _config.RegionCodes = regions.ToList();
        }

        public static bool StartProcess()
        {
            try
            {
                _config = GetConfig();
            }
            catch (Exception ex)
            {
                // Use the defaults then
                _config = new Config()
                {
                    RegionCodes = new List<string>() { "FA", "FY", "FB", "FV", "FQ", "FD", "FX" },
                    AlertLevelRegional = 3,
                    AlertLevelInbound = 3,
                    AlertLevelOutbound = 3,
                    AlertLevelGrow = 3,
                    RegionName = "VATSSA",
                    RegionCenterPoint = new double[] { -26.051257, 24.781342 },
                    RegionRadius = 1680d
                };
            }
            _centerPoint = new GeoCoordinate(_config.RegionCenterPoint[0], _config.RegionCenterPoint[1]);
            DataStore.Initialize();
            _thread = new Thread(() => Run());
            _thread.Start();
            return true;
        }

        public static void UpdateConfig()
        {
            try
            {
                _config = GetConfig();
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

                    var regionalFlights = vatsimData.pilots
                        .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    var outboundFlights = vatsimData.pilots
                        .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    var inboundFlights = vatsimData.pilots
                        .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                 

                    var processedOutbound = CheckPilotDistances(outboundFlights);
                    var processedInbound = CheckPilotDistances(inboundFlights);

                    List<string> busyAirports = processedOutbound
                        .Select(at => at.flight_plan.departure).ToList();
                    busyAirports = busyAirports.Concat(processedInbound
                        .Select(at => at.flight_plan.arrival).ToList()).ToList();
                    // Now with a List<string> of all arrival and departure airports at their counts
                    // Calculate the count for each one
                    var busyAirportsProcessed = busyAirports.GroupBy(ba => ba)
                        .Select(itemGroup => new { Item = itemGroup.Key, Count = itemGroup.Count() }).OrderByDescending(bap => bap.Count);
                    var highTrafficList = busyAirportsProcessed.Where(bap => bap.Count > _config.AlertLevelGrow);
                    // Make above a class so that you can add it to the TrafficAlert class


                    // Regional Traffic
                    regionalFlights = CheckPilotDistances(regionalFlights);
                    if (_regionalAlert != null)
                    {
                        if (regionalFlights.Count() < _config.AlertLevelRegional - 1)
                        {
                            _regionalAlert = null;
                        }
                        else if (regionalFlights.Count() > _regionalAlert.AircraftCount + _config.AlertLevelGrow)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _regionalAlert.AircraftCount = regionalFlights.Count();
                            _regionalAlert.Update = true;
                            _regionalAlert.FirstArrivalTime = firstArrival.ToString("HH:mm");
                            _regionalAlert.FirstArrivalTimespan = firstArrivalString;
                            _regionalAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                            _regionalAlert.Planes = regionalFlights.ToList();
                            Helpers.TelegramHelper.SendUpdate(_regionalAlert, true);
                        }
                    }
                    else
                    {
                        if (regionalFlights.Count() > _config.AlertLevelRegional)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _regionalAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = regionalFlights.Count(),
                                Alert = AlertType.Regional.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                FirstArrivalTime = firstArrival.ToString("HH:mm"),
                                FirstArrivalTimespan = firstArrivalString,
                                FirstArrivalLocation = firstTimespan.ArrivalAirport,
                                Planes = regionalFlights.ToList()
                            };
                            Helpers.TelegramHelper.SendUpdate(_regionalAlert);
                        }
                    }

                    // Inbound Traffic
                    inboundFlights = CheckPilotDistances(inboundFlights);
                    if (_inboundAlert != null)
                    {
                        if (inboundFlights.Count() < _config.AlertLevelInbound - 1)
                        {
                            _inboundAlert = null;
                        }
                        else if (inboundFlights.Count() > _inboundAlert.AircraftCount + _config.AlertLevelGrow)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _inboundAlert.AircraftCount = inboundFlights.Count();
                            _inboundAlert.Update = true;
                            _inboundAlert.FirstArrivalTime = firstArrival.ToString("HH:mm");
                            _inboundAlert.FirstArrivalTimespan = firstArrivalString;
                            _inboundAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                            _inboundAlert.Planes = inboundFlights.ToList();
                            Helpers.TelegramHelper.SendUpdate(_inboundAlert, true);
                        }
                    }
                    else
                    {
                        if (inboundFlights.Count() > _config.AlertLevelInbound)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _inboundAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = inboundFlights.Count(),
                                Alert = AlertType.Inbound.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                FirstArrivalTime = firstArrival.ToString("HH:mm"),
                                FirstArrivalTimespan = firstArrivalString,
                                FirstArrivalLocation = firstTimespan.ArrivalAirport,
                                Planes = inboundFlights.ToList()
                            };
                            Helpers.TelegramHelper.SendUpdate(_inboundAlert);
                        }
                    }

                    // Outbound Traffic
                    outboundFlights = CheckPilotDistances(outboundFlights);
                    if (_outboundAlert != null)
                    {
                        if (outboundFlights.Count() < _config.AlertLevelOutbound - 1)
                        {
                            _outboundAlert = null;
                        }
                        else if (outboundFlights.Count() > _outboundAlert.AircraftCount + _config.AlertLevelGrow)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _outboundAlert.AircraftCount = outboundFlights.Count();
                            _outboundAlert.Update = true;
                            _outboundAlert.OutboundAirport = GetOutboundAirport(outboundFlights);
                            _outboundAlert.Planes = outboundFlights.ToList();
                            Helpers.TelegramHelper.SendUpdate(_outboundAlert, true);
                        }
                    }
                    else
                    {
                        if (outboundFlights.Count() > _config.AlertLevelOutbound)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _outboundAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = outboundFlights.Count(),
                                Alert = AlertType.Outbound.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                OutboundAirport = GetOutboundAirport(outboundFlights),
                                Planes = outboundFlights.ToList()
                            };
                            Helpers.TelegramHelper.SendUpdate(_outboundAlert);
                        }
                    }

                    // Airport Specific Inbound and Outbound
                    if (_highTrafficAlert != null)
                    {
                        if (!highTrafficList.Any())
                        {
                            _highTrafficAlert = null;
                        }
                        else if (highTrafficList.Any() &&
                            highTrafficList.First().Count < 1)
                        {
                            _highTrafficAlert = null;
                        }
                        else if (highTrafficList.Any() &&
                            highTrafficList.First().Count > 2)
                        {
                            _highTrafficAlert.Update = true;
                            _highTrafficAlert.OutboundAirport = GetOutboundAirport(outboundFlights);
                            _highTrafficAlert.Planes = outboundFlights.ToList();
                            _highTrafficAlert.BusyAirports = highTrafficList.Select(htl => new AirportInfo()
                            {
                                Icao = htl.Item,
                                Count = htl.Count
                            }).ToList();
                            Helpers.TelegramHelper.SendUpdate(_highTrafficAlert, true);
                        }
                    }
                    else
                    {
                        if (highTrafficList.Count() > 0)
                        {                            
                            _highTrafficAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = highTrafficList.Count(),
                                Alert = AlertType.High.ToString(),
                                Timestamp = DateTime.Now,
                                Update = true,
                                OutboundAirport = null,
                                Planes = null,
                                BusyAirports = highTrafficList.Select(htl => new AirportInfo()
                                {
                                    Icao = htl.Item,
                                    Count = htl.Count
                                }).ToList()
                        };
                            Helpers.TelegramHelper.SendUpdate(_highTrafficAlert);
                        }
                    }

                }
                catch (Exception ex)
                {
                }
                Thread.Sleep(10000);
            }
        }

        private static void RunNotifications()
        {
        }
    }
}