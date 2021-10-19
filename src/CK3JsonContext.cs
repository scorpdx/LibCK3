using LibCK3.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibCK3
{
    [JsonSerializable(typeof(Meta_Data))]
    [JsonSerializable(typeof(Gamestate))]

    [JsonSerializable(typeof(ActiveSchemeDerived))]
    internal partial class CK3JsonContext : JsonSerializerContext
    {
    }
}
