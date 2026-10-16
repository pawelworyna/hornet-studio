namespace HornetStudio.Core.Domain.Enums;

public enum ConnectionType
{
    Sqlite,
    PostgreSql,
    MySql,
    MariaDB,
    SqlServer,
    CockroachDb,
    Redshift,
    LibSql,
    DuckDb,
    Oracle,
    ClickHouse,
    MongoDb,
    Supabase,
    Redis,
    PocketBase,
    Appwrite,
    Firebase,
    AwsAmplify,
    Db2,
    SapHana,
    MsAccess,
    Firebird,
    Other
}

public enum SshMode
{
    None,
    Password,
    KeyFile,
    Agent
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Like,
    NotLike,
    ILike,
    NotILike,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
    IsNull,
    IsNotNull
}

public enum RowIdentifierMode
{
    PrimaryKey,
    RowId,       // SQLite rowid
    Ctid,        // PostgreSQL ctid
    UniqueIndex, // MySQL/SQL Server unique index
    AllColumns   // Fallback: All column values match (optimistic concurrency)
}

public enum SortDirection
{
    Ascending,
    Descending
}
