namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]

    public class Meta_Secondary_Portrait
    {
        public string type { get; set; }
        public int id { get; set; }
        public float age { get; set; }
        public Genes genes { get; set; }
        public long[] entity { get; set; }
    }
}
