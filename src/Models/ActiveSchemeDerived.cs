using LibCK3.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]
    [JsonConverter(typeof(ActiveSchemeJsonConverter))]
    public class ActiveScheme
    {
        public string type { get; set; }
        public long owner { get; set; }
        public long target { get; set; }
        public int progress { get; set; }
        public int update_days { get; set; }
        public bool scheme_exposed { get; set; }
        public double owner_influence { get; set; }
        public int secrecy { get; set; }
        public int chance { get; set; }
        public int scheme_freeze_days { get; set; }
        public string date { get; set; }
    }

    public class ActiveSchemeDerived : ActiveScheme
    {
    }
}
