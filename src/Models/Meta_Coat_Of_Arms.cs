namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]
    public class Meta_Coat_Of_Arms
    {
        public string pattern { get; set; }
        public string color1 { get; set; }
        public string color2 { get; set; }
        public string color3 { get; set; }
        public Colored_Emblem colored_emblem { get; set; }
    }
}
