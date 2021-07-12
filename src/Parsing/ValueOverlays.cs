using LibCK3.Tokens;
using System.Collections.Generic;
using System.Linq;

namespace LibCK3.Parsing
{
    public static class ValueOverlays
    {
        public static IReadOnlyDictionary<ushort, ValueOverlayFlags> Overlays { get; } = new Dictionary<string, ValueOverlayFlags>()
        {
            { "date", ValueOverlayFlags.AsDate },
            { "meta_date", ValueOverlayFlags.AsDate },
            { "meta_real_date", ValueOverlayFlags.AsDate },
            { "bookmark_date", ValueOverlayFlags.AsDate },
            { "start_time", ValueOverlayFlags.AsDate },
            { "start_date", ValueOverlayFlags.AsDate },
            { "end_date", ValueOverlayFlags.AsDate },
            { "expiration_date", ValueOverlayFlags.AsDate },
            { "history", ValueOverlayFlags.AsDate | ValueOverlayFlags.Repeats | ValueOverlayFlags.KeepForChildren },
            { "reign_opinion_held_since", ValueOverlayFlags.AsDate },
            { "found_date", ValueOverlayFlags.AsDate },
            { "birth", ValueOverlayFlags.AsDate },
            { "became_ruler_date", ValueOverlayFlags.AsDate },
            { "pool_history", ValueOverlayFlags.AsDate },
            { "leave_court_date", ValueOverlayFlags.AsDate },
            { "decision_cooldowns", ValueOverlayFlags.AsDate | ValueOverlayFlags.Repeats | ValueOverlayFlags.KeepForChildren  },
            { "imprison_type_date", ValueOverlayFlags.AsDate },
            { "last_war_finish_date", ValueOverlayFlags.AsDate },
            { "arrival_date", ValueOverlayFlags.AsDate },
            { "spawn_date", ValueOverlayFlags.AsDate },
            { "scheme_cooldowns_0", ValueOverlayFlags.AsDate | ValueOverlayFlags.KeepForChildren },
            { "scheme_cooldowns_1", ValueOverlayFlags.AsDate | ValueOverlayFlags.KeepForChildren },
            { "cooldown_against_recipient_0", ValueOverlayFlags.AsDate | ValueOverlayFlags.KeepForChildren },
            { "cooldown_against_recipient_1", ValueOverlayFlags.AsDate | ValueOverlayFlags.KeepForChildren },
            { "cooldown", ValueOverlayFlags.AsDate },
            { "last_supply_date", ValueOverlayFlags.AsDate },
            { "gathered_date", ValueOverlayFlags.AsDate },
            { "last_councillor_change", ValueOverlayFlags.AsDate },
            { "hired_until", ValueOverlayFlags.AsDate },
            { "force", ValueOverlayFlags.AsDate },
            { "dates", ValueOverlayFlags.AsDate | ValueOverlayFlags.Repeats | ValueOverlayFlags.KeepForChildren },
            { "last_action", ValueOverlayFlags.AsDate },
            { "atrtition_date", ValueOverlayFlags.AsDate },
            //{ "", ValueOverlayFlags.AsDate },
            { "gold", ValueOverlayFlags.AsQ },
            //{ "", ValueOverlayFlags.AsQ },
        }
        .Join(CK3Tokens.TokenNames, okvp => okvp.Key, ikvp => ikvp.Key, (ok, ik) => (ik.Value, ok.Value))
        .ToDictionary(tup => tup.Item1, tup => tup.Item2);
    }
}
