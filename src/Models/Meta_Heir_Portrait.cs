namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]

    public class Meta_Heir_Portrait
    {
        public string type { get; set; }
        public int id { get; set; }
        public float age { get; set; }
        public Genes genes { get; set; }
        public int[] entity { get; set; }
    }
}
