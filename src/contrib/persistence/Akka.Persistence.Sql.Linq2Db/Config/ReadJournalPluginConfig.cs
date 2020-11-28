namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class ReadJournalPluginConfig
    {
        public ReadJournalPluginConfig(Configuration.Config config)
        {
            TagSeparator = config.GetString("tag-separator", ",");
            Dao = config.GetString("dao",
                "akka.persistence.sql.linq2db.dao.bytea.readjournal.bytearrayreadjournaldao");
            
        }

        public string Dao { get; set; }

        public string TagSeparator { get; set; }
    }
}