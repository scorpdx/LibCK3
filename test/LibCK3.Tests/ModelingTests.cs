using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace LibCK3.Tests
{
    public class ModelingTests
    {
        private const string JSON_PATH = "assets/save.json";

        private readonly ITestOutputHelper output;

        public ModelingTests(ITestOutputHelper output)
        {
            this.output = output;
        }


        [Fact]
        public async Task ParseSchemeModel()
        {
            await using var jsonFile = File.OpenRead(JSON_PATH);
            using var json = await JsonDocument.ParseAsync(jsonFile);

            var ck3save = new CK3Save(json);
            var meta = ck3save.meta_data;
            var gamestate = ck3save.gamestate;

            //Assert.Equivalent(meta, gamestate.meta_data);

            var playerId = meta.meta_main_portrait.id;
            var allSchemesTargetingMe = gamestate.schemes.active.Where(kvp => kvp.Value?.target == playerId);
            Assert.NotEmpty(allSchemesTargetingMe);
            foreach(var (schemer, scheme) in allSchemesTargetingMe)
            {
                output.WriteLine($"Schemer {schemer} is a{(schemer == scheme.owner ? " plotter" : "n agent")} targetting me with a {scheme.type} scheme!");
            }
        }
    }
}
