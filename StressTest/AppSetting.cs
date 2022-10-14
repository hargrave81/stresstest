using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StressTest
{
    public class AppSettingReader
    {
        [JsonPropertyName("tests")]
        public Test[] Tests { get; set; }
        [JsonPropertyName("threads")]
        public int Threads { get; set; }
        [JsonPropertyName("paralell")]
        public bool Paralell { get; set; }

        [JsonPropertyName("exclude404")]
        public bool Exclude404 { get; set; }
    }
    public class Test
    {
        [JsonPropertyName("endPoint")]
        public string EndPoint { get; set; }

        [JsonPropertyName("payLoadObject")]
        public object[] PayLoadObjects { get; set; }
        
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("tokenUri")]
        public string TokenUri { get; set; }
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; }
        [JsonPropertyName("secret")]
        public string Secret { get; set; }
        [JsonPropertyName("scope")]
        public string Scope { get; set; }

        [JsonPropertyName("payLoadQuery")]
        public string[] PayLoadQuery { get; set; }
    }
}
