using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StressTest
{
    internal class ApiResult
    {
        public decimal CallTime { get; set; }
        public int StatusCode { get; set; }
        public string EndPoint { get; set; }
    }
}
