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
                    case "Inbound":
                    case "Regional":
                        var timeSpan = alert.FirstArrivalTimespan.Split(':');
                        var hourStr = int.Parse(timeSpan[0]) != 1 ? "hours" : "hour";
                        var minuteStr = int.Parse(timeSpan[1]) != 1 ? "minutes" : "minute";

                        message = $"<b>{growString}</b>{Environment.NewLine}" +
                            $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                            $"<i>First arrival at {alert.FirstArrivalLocation} in about {int.Parse(timeSpan[0])} {hourStr} and {int.Parse(timeSpan[1])} {minuteStr}. (Arriving at {alert.FirstArrivalTime}z)</i>";
                        break;
                    case "Outbound":
                        message = $"<b>{growString}</b>{Environment.NewLine}" +
                      $"Aircraft Count: {alert.AircraftCount}{Environment.NewLine}" +
                      $"<i>Most aircraft are departing from {alert.OutboundAirport}</i>";
                        break;
                    case "High":
                        var busyList = string.Empty;
                        alert.BusyAirports.ForEach(ba =>
                        {
                            if (busyList != "")
                            {
                                busyList += ",";
                            }
                            busyList += $"{ba.Icao}({ba.Count})";
                        });
                        message = $"<b>{growString}</b>{Environment.NewLine}" +
                            $"At {alert.AircraftCount} airport(s){Environment.NewLine}" +
                            $"<i>{busyList}</i>";
                        break;
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
                    SendViaDiscord(message, config);
            }
            catch (Exception ex)
            {
                TrafficNotify.SetError($"{DateTime.Now.ToString("yyyyMMdd hh:mm:ss")} - {ex.Message} - {ex.InnerException}");
            }

        }

        public static void SendViaTelegram(string message, Config config)
        {
            var bot = new TelegramBotClient(config.TelegramApi);
            var result = bot.SendTextMessageAsync(config.TelegramGroupId, message, Telegram.Bot.Types.Enums.ParseMode.Html).Result;
        }

        public static async void SendViaDiscord(string message, Config config)
        {
            if (_discordClient == null)
            {
                SetupDiscord(config);
            }
            SocketChannel channel = _discordClient.GetChannel(config.DiscordChannel);
            await (channel as IMessageChannel).SendMessageAsync(message);
        }

        public static async void SetupDiscord(Config config)
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

        public static async void StopDiscord()
        {
            if (_discordClient == null)
            {
                return;
            }
            await _discordClient.StopAsync();
        }

        public static async void UpdateDiscord(int planes, Config config)
        {
            await _discordClient.SetGameAsync($"Vatsim - {planes} aicraft in {config.RegionName}");
        }
    }
}