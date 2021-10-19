namespace LibCK3.Models
{
    [CK3SaveGameVersion("1.4.4")]
    public class Meta_Data
    {
        public int save_game_version { get; set; }
        public string version { get; set; }
        public int portraits_version { get; set; }
        public string meta_date { get; set; }
        public string meta_player_name { get; set; }
        public string meta_title_name { get; set; }
        public Meta_Coat_Of_Arms meta_coat_of_arms { get; set; }
        public int meta_player_tier { get; set; }
        public string meta_house_name { get; set; }
        public Meta_House_Coat_Of_Arms meta_house_coat_of_arms { get; set; }
        public int meta_dynasty_frame { get; set; }
        public Meta_Main_Portrait meta_main_portrait { get; set; }
        public Meta_Heir_Portrait meta_heir_portrait { get; set; }
        public Meta_Secondary_Portrait meta_secondary_portrait { get; set; }
        public string meta_front_end_background { get; set; }
        public string meta_government { get; set; }
        public string meta_real_date { get; set; }
        public int meta_number_of_players { get; set; }
        public string[] dlcs { get; set; }
        public Game_Rules game_rules { get; set; }
        public bool can_get_achievements { get; set; }
        public bool ironman { get; set; }
    }
}
