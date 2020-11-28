namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class JournalPluginConfig
    {
        public JournalPluginConfig(Configuration.Config config)
        {
            TagSeparator = config.GetString("tag-separator", ",");
            //TODO: FILL IN SANELY
            Dao = config.GetString("dao", "Akka.Persistence.Sql.Linq2Db.Journal.DAO.ByteArrayJournalDao ;Akka.Persistence.Sql.Linq2Db.Journal");
        }
        public string TagSeparator { get; protected set; }
        public string Dao { get; protected set; }
    }
}