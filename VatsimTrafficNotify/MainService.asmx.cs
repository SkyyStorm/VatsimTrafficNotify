using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using VatsimATCInfo.Helpers;
using VatsimTrafficNotify.Models;
using VatsimTrafficNotify.Process;

namespace VatsimTrafficNotify
{
    /// <summary>
    /// Summary description for MainService
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    [System.Web.Script.Services.ScriptService]
    public class MainService : WebService
    {

        [WebMethod]
        [ScriptMethod(UseHttpGet = true, ResponseFormat = ResponseFormat.Json)]
        public object GetData()
        {
            return new
            {
                Alerts = TrafficNotify.GetAlerts()
            };
        }
    }
}
