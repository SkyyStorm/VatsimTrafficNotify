using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Web;
using VatsimATCInfo.Helpers;
using VatsimTrafficNotify.Models;
using System.Device.Location;

namespace VatsimTrafficNotify.Process
{
    public class TrafficNotify
    {
        private static Thread _thread;
        private static Thread _notificationThread;
        private static List<string> _code = new List<string>() { "FA", "FV", "FM", "FY" };
        private static bool _running = false;
        private static int _regionalAlertLevel = 2;
        private static int _outboundAlertLevel = 2;
        private static int _inboundAlertLevel = 2;
        private static int _growCount = 2;

        private static TrafficAlert _regionalAlert = null;
        private static TrafficAlert _inboundAlert = null;
        private static TrafficAlert _outboundAlert = null;

        private static List<TrafficAlert> _Alerts;


        public static bool StartProcess()
        {
            DataStore.Initialize();
            _thread = new Thread(() => Run());
            _thread.Start();
            //_notificationThread = new Thread(() => RunNotifications());
            //_notificationThread.Start();
            return true;
        }
        public static object GetAlerts()
        {
            return new List<TrafficAlert>()
            {
                _regionalAlert, 
                _inboundAlert,
                _outboundAlert
            };
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
                    vatsimData.prefiles = vatsimData.prefiles.Where(pf => pf.flight_plan != null).ToList();

                    var regionalFlights = vatsimData.pilots
                        .Where(p => _code.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && _code.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    var outboundFlights = vatsimData.pilots
                        .Where(p => _code.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && !_code.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));

                    var inboundFlights = vatsimData.pilots
                        .Where(p => !_code.Any(c => c == p.flight_plan.departure.ToUpper().Substring(0, 2))
                        && _code.Any(c => c == p.flight_plan.arrival.ToUpper().Substring(0, 2)));


                    // Regional Traffic
                    if (_regionalAlert != null)
                    {
                        if (regionalFlights.Count() < _regionalAlert.AircraftCount - 1)
                        {
                            _regionalAlert = null;
                        }
                        else if (regionalFlights.Count() > _regionalAlert.AircraftCount + _growCount)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _regionalAlert.AircraftCount = regionalFlights.Count();
                            _regionalAlert.Update = true;
                            _regionalAlert.FirstArrivalTime = firstArrival;
                            _regionalAlert.FirstArrivalTimespan = firstArrivalString;
                            _regionalAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                        }
                    }
                    else
                    {
                        if (regionalFlights.Count() > _regionalAlertLevel)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(regionalFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _regionalAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = regionalFlights.Count(),
                                Alert = AlertType.Regional,
                                Timestamp = DateTime.Now,
                                Update = true,
                                FirstArrivalTime = firstArrival,
                                FirstArrivalTimespan = firstArrivalString,
                                FirstArrivalLocation = firstTimespan.ArrivalAirport
                        };
                        }
                    }

                    // Inbound Traffic
                    if (_inboundAlert != null)
                    {
                        if (inboundFlights.Count() < _inboundAlert.AircraftCount - 1)
                        {
                            _inboundAlert = null;
                        }
                        else if (inboundFlights.Count() > _inboundAlert.AircraftCount + _growCount)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _inboundAlert.AircraftCount = inboundFlights.Count();
                            _inboundAlert.Update = true;
                            _inboundAlert.FirstArrivalTime = firstArrival;
                            _inboundAlert.FirstArrivalTimespan = firstArrivalString;
                            _inboundAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                        }
                    }
                    else
                    {
                        if (inboundFlights.Count() > _inboundAlertLevel)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(inboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _inboundAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = inboundFlights.Count(),
                                Alert = AlertType.Regional,
                                Timestamp = DateTime.Now,
                                Update = true,
                                FirstArrivalTime = firstArrival,
                                FirstArrivalTimespan = firstArrivalString,
                                FirstArrivalLocation = firstTimespan.ArrivalAirport
                        };
                        }
                    }

                    // Outbound Traffic
                    if (_outboundAlert != null)
                    {
                        if (outboundFlights.Count() < _outboundAlert.AircraftCount - 1)
                        {
                            _outboundAlert = null;
                        }
                        else if (outboundFlights.Count() > _outboundAlert.AircraftCount + _growCount)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _outboundAlert.AircraftCount = outboundFlights.Count();
                            _outboundAlert.Update = true;
                            //_outboundAlert.FirstArrivalTime = firstArrival;
                            //_outboundAlert.FirstArrivalTimespan = firstArrivalString;
                            //_outboundAlert.FirstArrivalLocation = firstTimespan.ArrivalAirport;
                        }
                    }
                    else
                    {
                        if (outboundFlights.Count() > _outboundAlertLevel)
                        {
                            var firstTimespan = CalculateFirstArrivalTime(outboundFlights);
                            var firstArrival = DateTime.UtcNow.Add(firstTimespan.ArrivalTime);
                            var firstArrivalString = firstTimespan.ArrivalTime.ToString(@"hh\:mm");
                            _outboundAlert = new TrafficAlert()
                            {
                                Message = "Traffic is increasing in region.",
                                AircraftCount = outboundFlights.Count(),
                                Alert = AlertType.Regional,
                                Timestamp = DateTime.Now,
                                Update = true,
                                //FirstArrivalTime = firstArrival,
                                //FirstArrivalTimespan = firstArrivalString,
                                //FirstArrivalLocation = firstTimespan.ArrivalAirport
                        };
                        }
                    }


                    // Regional Flightplans
                    // Outbound Traffic
                    // Outbound Flightplans
                    // Inbound Traffic
                    // Inbound Flightplans



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
                if (speed > -1)
                {
                    var nm = (double)planeCoord.GetDistanceTo(arrAirportCoord) * 0.000539957d;
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
    }

    public class TrafficAlert
    {
        public string Message { get; set; }
        public int AircraftCount { get; set; }
        public DateTime Timestamp { get; set; }
        public AlertType Alert { get; set; }
        public bool Update { get; set; }
        public DateTime FirstArrivalTime { get; set; }
        public string FirstArrivalTimespan { get; set; }
        public string FirstArrivalLocation { get; set; }
    }

    public class FirstArrival 
    {
        public string ArrivalAirport { get; set;} 
        public TimeSpan ArrivalTime { get; set; }
    }

    public enum AlertType
    {
        Regional,
        Inbound,
        Outbound
    }
}