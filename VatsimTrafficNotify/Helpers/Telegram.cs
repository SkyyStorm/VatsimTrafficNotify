using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Telegram.Bot;
using VatsimTrafficNotify.Process;

namespace VatsimTrafficNotify.Helpers
{
    public class TelegramHelper
    {
        private static string _api = "5240174442:AAEUgtv1MlDQjSrBnHq3BpB-_IsuM-DCK9E";
        public static void SendUpdate(TrafficAlert alert, bool isGrow = false)
        {
            var bot = new TelegramBotClient(_api);
            var message = string.Empty;
            var growString = isGrow ? $"{alert.Alert} Traffic Update" : $"{alert.Alert} Traffic Alert";
            if (alert.Alert != "Outbound")
            {
                var timeSpan = alert.FirstArrivalTimespan.Split(':');
                var hourStr = int.Parse(timeSpan[0]) != 1 ? "hours" : "hour";
                var minuteStr = int.Parse(timeSpan[1]) != 1 ? "minutes" : "minute";

                message = $"<b>{growString}</b>{Environment.NewLine}" +
                    $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                    $"<i>First arrival at {alert.FirstArrivalLocation} in about {int.Parse(timeSpan[0])} {hourStr} and {int.Parse(timeSpan[1])} {minuteStr}. (Arriving at {alert.FirstArrivalTime}z)</i>";
            }
            else
            {
                message = $"<b>{growString}</b>{Environment.NewLine}" +
                   $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                   $"<i>Most aircraft is departing from {alert.OutboundAirport}</i>";
            }
            var result = bot.SendTextMessageAsync(-744399034, message,Telegram.Bot.Types.Enums.ParseMode.Html).Result;            
        }

        public static void SendMessage(string message)
        {
            var bot = new TelegramBotClient(_api);
            var result = bot.SendTextMessageAsync(-744399034, message).Result;

        }
    }
}