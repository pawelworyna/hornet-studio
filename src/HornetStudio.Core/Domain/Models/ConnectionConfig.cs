using HornetStudio.Core.Domain.Enums;

namespace HornetStudio.Core.Domain.Models;

public class SshTunnelConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public SshMode Mode { get; set; } = SshMode.Password;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? Passphrase { get; set; }

    // Bastion / Jump Host
    public bool UseBastion { get; set; }
    public string? BastionHost { get; set; }
    public int BastionPort { get; set; } = 22;
    public string? BastionUsername { get; set; }
    public string? BastionPassword { get; set; }
    public string? BastionKeyPath { get; set; }
}

public class SslConfig
{
    public bool Enabled { get; set; }
    public string? CaCertPath { get; set; }
    public string? ClientCertPath { get; set; }
    public string? ClientKeyPath { get; set; }
    public bool RejectUnauthorized { get; set; } = true;
}

public class ConnectionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Sqlite;
    
    // Serwer & Baza
    public string Host { get; set; } = "localhost";
    public int? Port { get; set; }
    public string? DefaultDatabase { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    
    // SQLite specific: ścieżka do pliku
    public string? DatabasePath { get; set; }

    // Alternatywny connection string
    public bool UseConnectionString { get; set; }
    public string? ConnectionString { get; set; }

    // Bezpieczeństwo i sieć
    public SslConfig Ssl { get; set; } = new();
    public SshTunnelConfig Ssh { get; set; } = new();

    // Wygląd i organizacja
    public string? LabelColor { get; set; } // np. "#10B981"
    public Guid? FolderId { get; set; }
    public bool ReadOnlyMode { get; set; }
    public bool IsPinned { get; set; }

    public int GetEffectivePort()
    {
        if (Port.HasValue && Port.Value > 0) return Port.Value;
        return ConnectionType switch
        {
            ConnectionType.PostgreSql => 5432,
            ConnectionType.MySql => 3306,
            ConnectionType.MariaDB => 3306,
            ConnectionType.SqlServer => 1433,
            ConnectionType.CockroachDb => 26257,
            ConnectionType.Redshift => 5439,
            ConnectionType.Oracle => 1521,
            ConnectionType.ClickHouse => 8123,
            ConnectionType.MongoDb => 27017,
            ConnectionType.Supabase => 5432,
            ConnectionType.Redis => 6379,
            ConnectionType.PocketBase => 8090,
            ConnectionType.Appwrite => 3306,
            ConnectionType.Firebase => 8080,
            ConnectionType.AwsAmplify => 5432,
            ConnectionType.Db2 => 50000,
            ConnectionType.SapHana => 30015,
            ConnectionType.MsAccess => 0,
            _ => 0
        };
    }
}
