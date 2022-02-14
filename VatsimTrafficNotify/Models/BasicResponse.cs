using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace VatsimTrafficNotify.Models
{
    public class BasicResponse : IBasicResponse
    {
        public bool Result { get; set; }
        public string Message { get; set; }
    }
}