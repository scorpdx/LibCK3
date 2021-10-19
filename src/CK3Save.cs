using LibCK3.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibCK3
{
    public record class CK3Save(JsonDocument Json)
    {
        public string Checksum { get; } = Json.RootElement.GetProperty("checksum").GetString();

        protected JsonElement json_meta => Json.RootElement.GetProperty("meta_data");

        protected JsonElement json_gamestate => Json.RootElement.GetProperty("gamestate");

        //

        public Meta_Data meta_data => json_meta.Deserialize<Meta_Data>(CK3JsonContext.Default.Meta_Data);

        public Gamestate gamestate => json_gamestate.Deserialize<Gamestate>(CK3JsonContext.Default.Gamestate);
    }
}
