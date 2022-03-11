using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Device.Location;
using System.IO;
using System.Linq;
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
                Error = _error
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

            ExternalComHelper.SendMessage("Restarted traffic monitoring", _config);
            
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
                        else if (groupFlights.Count() > _groupFlightTrafficAlert.BusyAirports.Count()
                            || groupFlights.Max(gf => gf.Count) >= _groupFlightTrafficAlert.BusyAirports.Max(ba => ba.Count) + _config.AlertLevelGrow)
                        {
                            _groupFlightTrafficAlert.Update = true;
                            _groupFlightTrafficAlert.BusyAirports = groupFlights;
                            ExternalComHelper.SendUpdate(_groupFlightTrafficAlert, _config, true);
                        }
                    }
                    else
                    {
                        if (groupFlights.Count() > 0)
                        {
                            _groupFlightTrafficAlert = new TrafficAlert()
                            {
                                Message = "Groupflights detected.",
                                AircraftCount = groupFlights.Count(),
                                Alert = AlertType.GroupFlight.ToString(),
                                Timestamp = DateTime.Now,
                                BusyAirports = groupFlights,
                                Update = true                                
                            };
                            Helpers.ExternalComHelper.SendUpdate(_groupFlightTrafficAlert, _config);
                        }
                    }

                    if (_config.NotifyDiscord)
                    {
                        ExternalComHelper.UpdateDiscord(allPlanesInRange.Count(), _config);
                    }


                    //var regionalFlights = vatsimData.pilots
                    //    .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                    //    && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    //var outboundFlights = vatsimData.pilots
                    //    .Where(p => _config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                    //    && !_config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    //var inboundFlights = vatsimData.pilots
                    //    .Where(p => !_config.RegionCodes.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                    //    && _config.RegionCodes.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));                            

                    //var processedOutbound = CheckPilotDistances(outboundFlights);
                    //var processedInbound = CheckPilotDistances(inboundFlights);

                    //var countOfPlanes = processedInbound.Count + processedInbound.Count;
                    //ExternalComHelper.UpdateDiscord(countOfPlanes, _config);

                    //List<string> busyAirports = processedOutbound
                    //    .Select(at => at.flight_plan.departure).ToList();
                    //busyAirports = busyAirports.Concat(processedInbound
                    //    .Select(at => at.flight_plan.arrival).ToList()).ToList();
                    //// Now with a List<string> of all arrival and departure airports at their counts
                    //// Calculate the count for each one
                    //var busyAirportsProcessed = busyAirports.GroupBy(ba => ba)
                    //    .Select(itemGroup => new { Item = itemGroup.Key, Count = itemGroup.Count() }).OrderByDescending(bap => bap.Count);
                    //var highTrafficList = busyAirportsProcessed.Where(bap => bap.Count > _config.AlertLevelGrow);
                    //// Make above a class so that you can add it to the TrafficAlert class


                    //// Regional Traffic
                    //regionalFlights = CheckPilotDistances(regionalFlights);
                    //if (_regionalAlert != null)
                    //{
                    //    if (regionalFlights.Count() < _config.AlertLevelRegional - 1)
                    //    {
                    //        _regionalAlert = null;
                    //    }
                    //    else if (regionalFlights.Count() > _regionalAlert.AircraftCount + _config.AlertLevelGrow)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _regionalAlert.AircraftCount = regionalFlights.Count();
                    //        _regionalAlert.Update = true;
                    //        _regionalAlert.FirstArrivalTime = firstArrival.ToString("HH:mm");
                    //        _regionalAlert.FirstArrivalTimespan = firstArrivalString;
                    //        _regionalAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                    //        _regionalAlert.Planes = regionalFlights.ToList();
                    //        Helpers.ExternalComHelper.SendUpdate(_regionalAlert, _config, true);
                    //    }
                    //}
                    //else
                    //{
                    //    if (regionalFlights.Count() > _config.AlertLevelRegional)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _regionalAlert = new TrafficAlert()
                    //        {
                    //            Message = "Traffic is increasing in region.",
                    //            AircraftCount = regionalFlights.Count(),
                    //            Alert = AlertType.Regional.ToString(),
                    //            Timestamp = DateTime.Now,
                    //            Update = true,
                    //            FirstArrivalTime = firstArrival.ToString("HH:mm"),
                    //            FirstArrivalTimespan = firstArrivalString,
                    //            FirstArrivalLocation = firstTimespan.ArrivalAirport,
                    //            Planes = regionalFlights.ToList()
                    //        };
                    //        Helpers.ExternalComHelper.SendUpdate(_regionalAlert, _config);
                    //    }
                    //}

                    //// Inbound Traffic
                    //inboundFlights = CheckPilotDistances(inboundFlights);
                    //if (_inboundAlert != null)
                    //{
                    //    if (inboundFlights.Count() < _config.AlertLevelInbound - 1)
                    //    {
                    //        _inboundAlert = null;
                    //    }
                    //    else if (inboundFlights.Count() > _inboundAlert.AircraftCount + _config.AlertLevelGrow)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _inboundAlert.AircraftCount = inboundFlights.Count();
                    //        _inboundAlert.Update = true;
                    //        _inboundAlert.FirstArrivalTime = firstArrival.ToString("HH:mm");
                    //        _inboundAlert.FirstArrivalTimespan = firstArrivalString;
                    //        _inboundAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                    //        _inboundAlert.Planes = inboundFlights.ToList();
                    //        Helpers.ExternalComHelper.SendUpdate(_inboundAlert, _config, true);
                    //    }
                    //}
                    //else
                    //{
                    //    if (inboundFlights.Count() > _config.AlertLevelInbound)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _inboundAlert = new TrafficAlert()
                    //        {
                    //            Message = "Traffic is increasing in region.",
                    //            AircraftCount = inboundFlights.Count(),
                    //            Alert = AlertType.Inbound.ToString(),
                    //            Timestamp = DateTime.Now,
                    //            Update = true,
                    //            FirstArrivalTime = firstArrival.ToString("HH:mm"),
                    //            FirstArrivalTimespan = firstArrivalString,
                    //            FirstArrivalLocation = firstTimespan.ArrivalAirport,
                    //            Planes = inboundFlights.ToList()
                    //        };
                    //        Helpers.ExternalComHelper.SendUpdate(_inboundAlert, _config);
                    //    }
                    //}

                    //// Outbound Traffic
                    //outboundFlights = CheckPilotDistances(outboundFlights);
                    //if (_outboundAlert != null)
                    //{
                    //    if (outboundFlights.Count() < _config.AlertLevelOutbound - 1)
                    //    {
                    //        _outboundAlert = null;
                    //    }
                    //    else if (outboundFlights.Count() > _outboundAlert.AircraftCount + _config.AlertLevelGrow)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _outboundAlert.AircraftCount = outboundFlights.Count();
                    //        _outboundAlert.Update = true;
                    //        _outboundAlert.OutboundAirport = GetOutboundAirport(outboundFlights);
                    //        _outboundAlert.Planes = outboundFlights.ToList();
                    //        Helpers.ExternalComHelper.SendUpdate(_outboundAlert, _config, true);
                    //    }
                    //}
                    //else
                    //{
                    //    if (outboundFlights.Count() > _config.AlertLevelOutbound)
                    //    {
                    //        var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                    //        var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                    //        var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                    //        _outboundAlert = new TrafficAlert()
                    //        {
                    //            Message = "Traffic is increasing in region.",
                    //            AircraftCount = outboundFlights.Count(),
                    //            Alert = AlertType.Outbound.ToString(),
                    //            Timestamp = DateTime.Now,
                    //            Update = true,
                    //            OutboundAirport = GetOutboundAirport(outboundFlights),
                    //            Planes = outboundFlights.ToList()
                    //        };
                    //        Helpers.ExternalComHelper.SendUpdate(_outboundAlert, _config);
                    //    }
                    //}

                    //// Airport Specific Inbound and Outbound
                    //if (_highTrafficAlert != null)
                    //{
                    //    if (!highTrafficList.Any())
                    //    {
                    //        _highTrafficAlert = null;
                    //    }
                    //    else if (highTrafficList.Any() &&
                    //        highTrafficList.First().Count < 1)
                    //    {
                    //        _highTrafficAlert = null;
                    //    }
                    //    else if (highTrafficList.Any() &&
                    //        highTrafficList.First().Count > _highTrafficAlert.BusyAirports.First().Count + _config.AlertLevelGrow)
                    //    {
                    //        _highTrafficAlert.Update = true;
                    //        _highTrafficAlert.OutboundAirport = GetOutboundAirport(outboundFlights);
                    //        _highTrafficAlert.Planes = outboundFlights.ToList();
                    //        _highTrafficAlert.BusyAirports = highTrafficList.Select(htl => new AirportInfo()
                    //        {
                    //            Icao = htl.Item,
                    //            Count = htl.Count
                    //        }).ToList();
                    //        Helpers.ExternalComHelper.SendUpdate(_highTrafficAlert, _config, true);
                    //    }
                    //}
                    //else
                    //{
                    //    if (highTrafficList.Count() > 0)
                    //    {                            
                    //        _highTrafficAlert = new TrafficAlert()
                    //        {
                    //            Message = "Traffic is increasing in region.",
                    //            AircraftCount = highTrafficList.Count(),
                    //            Alert = AlertType.High.ToString(),
                    //            Timestamp = DateTime.Now,
                    //            Update = true,
                    //            OutboundAirport = null,
                    //            Planes = null,
                    //            BusyAirports = highTrafficList.Select(htl => new AirportInfo()
                    //            {
                    //                Icao = htl.Item,
                    //                Count = htl.Count
                    //            }).ToList()
                    //    };
                    //        Helpers.ExternalComHelper.SendUpdate(_highTrafficAlert, _config);
                    //    }
                    //}

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
            var list = grouped.Where(g => g.Count >= _config.AlertLevelGroupflight).ToList();            
            foreach (var item in list)
            {
                var apName = item.Item.ToString().Split('-');
                var isInbound = _config.RegionCodes.Any(c => c == apName[1].ToUpper().Substring(0, 2));
                var info = new AirportInfo()
                {
                    Icao = (isInbound ? apName[1] : apName[0]),
                    Count = item.Count
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
                    .GetDistanceTo(airportLocation) * _toNM < 100)
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
            }
            return highTrafficList;
        }

        private static void RunNotifications()
        {
        }
    }
}