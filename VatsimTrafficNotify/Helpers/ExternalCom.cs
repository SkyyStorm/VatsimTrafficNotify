using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Telegram.Bot;
using VatsimTrafficNotify.Models;
using VatsimTrafficNotify.Process;

namespace VatsimTrafficNotify.Helpers
{
    public class ExternalComHelper
    {
        public static DiscordSocketClient _discordClient = null;       
        
        public static void SendUpdate(TrafficAlert alert, Config config, bool isGrow = false)
        {
            try
            {
                var bot = new TelegramBotClient(config.TelegramApi);
                var message = string.Empty;
                var growString = isGrow ? $"{alert.Alert} Traffic Update" : $"{alert.Alert} Traffic Alert";

                switch (alert.Alert)
                {
                    case "Area":
                        growString = isGrow ? $"Update: Traffic further increasing in {config.RegionName}" : $"Alert: Traffic increasing in {config.RegionName}";
                        message = $"**{growString}**{Environment.NewLine}" +
                            $"Aircraft Count: {alert.AircraftCount} ({alert.Inbounds.Count()} inbound, {alert.Outbounds.Count} outbound, {(alert.Planes.Count - alert.Outbounds.Count - alert.Inbounds.Count)} regional)";
                        break;
                    case "Airport":
                        growString = isGrow ? $"Update: Traffic further increasing around airports" : $"Alert: Traffic increasing around airports";
                        message = $"**{growString}**{Environment.NewLine}" +
                            $"Airports: {Environment.NewLine}";
                        foreach (var airport in alert.BusyAirports)
                        {
                            message += $"*{airport.Icao}: {airport.Count} ({airport.InboundsCount} inbound, {airport.OutboundsCount} outbound)* {Environment.NewLine}";
                        }
                        break;

                    case "GroupFlight":
                        growString = isGrow ? $"Update: More group flights detected" : $"Alert: Group flight detected";
                        message = $"**{growString}**{Environment.NewLine}" +
                            $"Group flights:{Environment.NewLine}";
                        foreach (var airport in alert.BusyAirports)
                        {
                            if (airport.FirstArrivalTime == null)
                            {
                                message += $"*{airport.Route}: {airport.Count}* {Environment.NewLine}";
                            }
                            else
                            {
                                var timeSpan = airport.FirstArrivalTimespan.Split(':');
                                var hourStr = int.Parse(timeSpan[0]) != 1 ? "hours" : "hour";
                                var minuteStr = int.Parse(timeSpan[1]) != 1 ? "minutes" : "minute";
                                message += $"*{airport.Route}: {airport.Count}, first arriving at {airport.FirstArrivalTime}z (in about {int.Parse(timeSpan[0])} {hourStr} and {int.Parse(timeSpan[1])} {minuteStr})* {Environment.NewLine}";
                            }
                        }
                        break;

                        //case "Inbound":
                        //case "Regional":
                        //    var timeSpan = alert.FirstArrivalTimespan.Split(':');
                        //    var hourStr = int.Parse(timeSpan[0]) != 1 ? "hours" : "hour";
                        //    var minuteStr = int.Parse(timeSpan[1]) != 1 ? "minutes" : "minute";

                        //    message = $"<b>{growString}</b>{Environment.NewLine}" +
                        //        $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                        //        $"<i>First arrival at {alert.FirstArrivalLocation} in about {int.Parse(timeSpan[0])} {hourStr} and {int.Parse(timeSpan[1])} {minuteStr}. (Arriving at {alert.FirstArrivalTime}z)</i>";
                        //    break;
                        //case "Outbound":
                        //    message = $"<b>{growString}</b>{Environment.NewLine}" +
                        //  $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                        //  $"<i>Most aircraft are departing from {alert.OutboundAirport}</i>";
                        //    break;
                        //case "High":
                        //    var busyList = string.Empty;
                        //    alert.BusyAirports.ForEach(ba =>
                        //    {
                        //        if (busyList != "")
                        //        {
                        //            busyList += ",";
                        //        }
                        //        busyList += $"{ba.Icao}({ba.Count})";
                        //    });
                        //    message = $"<b>{growString}</b>{Environment.NewLine}" +
                        //        $"At {alert.AircraftCount} airport(s){Environment.NewLine}" +
                        //        $"<i>{busyList}</i>";
                        //    break;
                }
                if (config.NotifyTelegram)
                    SendViaTelegram(message, config);
                if (config.NotifyDiscord)
                    SendViaDiscord(message, config);
            }
            catch (Exception ex)
            {
                TrafficNotify.SetError($"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} - {ex.Message} - {ex.InnerException}");
            }
        }

        public static void SendMessage(string message,Config config)
        {
            try 
            {
                if (config.NotifyTelegram)
                    SendViaTelegram(message, config);
                if (config.NotifyDiscord)
                    SendViaDiscord(message, config, true);
            }
            catch (Exception ex)
            {
                TrafficNotify.SetError($"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} - {ex.Message} - {ex.InnerException}");
            }

        }

        public static void SendViaTelegram(string message, Config config)
        {
            try
            {
                var bot = new TelegramBotClient(config.TelegramApi);
                var result = bot.SendTextMessageAsync(config.TelegramGroupId, message, Telegram.Bot.Types.Enums.ParseMode.Markdown).Result;
            }
            catch (Exception ex)
            {
                // nothing for now
                throw;
            }
        }

        public static async void SendViaDiscord(string message, Config config, bool normal = false)
        {
            try
            {
                if (_discordClient == null)
                {
                    SetupDiscord(config);
                }
                SocketChannel channel = _discordClient.GetChannel(config.DiscordChannel);
                await (channel as IMessageChannel).SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                // nothing for now                
            }
        }

        public static async void SetupDiscord(Config config)
        {
            try
            {
                if (_discordClient != null)
                {
                    await _discordClient.StopAsync();
                }
                _discordClient = new DiscordSocketClient();
                await _discordClient.LoginAsync(TokenType.Bot, config.DiscordToken);
                await _discordClient.StartAsync();
                await _discordClient.SetStatusAsync(UserStatus.Online);
                await _discordClient.SetGameAsync("Vatsim");
            }
            catch (Exception ex)
            {
                // nothing for now
            }
        }

        public static async void StopDiscord()
        {
            try
            {
                if (_discordClient == null)
                {
                    return;
                }
                await _discordClient.StopAsync();
            }
            catch (Exception ex)
            {
                // nothing for now
            }
        }

        public static async void UpdateDiscord(int planes, Config config)
        {
            try
            {
                await _discordClient.SetGameAsync($"Vatsim - {planes} aicraft in {config.RegionName}");
            }
            catch (Exception ex)
            {
                // nothing for now
            }
        }
    }
}