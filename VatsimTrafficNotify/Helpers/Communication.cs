using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VatsimATCInfo.Helpers
{
    public class Communication
    {
        private static string _dataUrl = "https://data.vatsim.net";
        private static string _metarUrl = "https://metar.vatsim.net";
        private static string _vatsimDataRequest = "v3/vatsim-data.json";
        private static string _vatsimTransceiverRequest = "v3/transceivers-data.json";
        private static string _metarRequest = "metar.php?id=";
        internal static T DoCall<T>()
        {
            RestClient client = null;
            RestRequest request = null;

            client = new RestClient(_dataUrl);
            request = new RestRequest(_vatsimDataRequest, (Method)DataFormat.Json);

            var response = client.GetAsync(request);
            return JsonConvert.DeserializeObject<T>(response.Result.Content);

        }
    }
}