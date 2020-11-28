namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class SnapshotPluginConfig
    {
        public SnapshotPluginConfig(Configuration.Config config)
        {
            Dao = config.GetString("dao",
                "akka.persistence.sql.linq2db.dao.bytea.snapshot.bytearraysnapshotdao");
        }

        public string Dao { get; protected set; }
    }
}