namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]
    public class Gamestate
    {
        public Meta_Data meta_data { get; set; }
        public Schemes schemes { get; set; }
    }
}
