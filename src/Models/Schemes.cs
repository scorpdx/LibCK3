using System.Collections.Generic;

namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]

    public class Schemes
    {
        public Dictionary<long, ActiveScheme> active { get; set; }
    }
}
