using LibCK3.Models;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibCK3.Serialization
{
    internal class ActiveSchemeJsonConverter : JsonConverter<ActiveScheme>
    {
        private static readonly JsonEncodedText None = JsonEncodedText.Encode("none");

        //deliberately ignore derived types
        //public override bool CanConvert(Type typeToConvert) => typeof(ActiveScheme).IsAssignableFrom(typeToConvert);

        public override ActiveScheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.StartObject => JsonSerializer.Deserialize<ActiveSchemeDerived>(JsonDocument.ParseValue(ref reader), CK3JsonContext.Default.ActiveSchemeDerived),
                JsonTokenType.String when reader.ValueTextEquals(None.EncodedUtf8Bytes) => null,
                _ => throw new NotImplementedException()
            };
        }

        public override void Write(Utf8JsonWriter writer, ActiveScheme value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
