using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Core.Storage.Security;
using Microsoft.EntityFrameworkCore;

namespace HornetStudio.Desktop.ViewModels;

public sealed partial class ConnectionFormViewModel : ObservableObject
{
    private readonly HornetDbContext _dbContext;
    private readonly MasterPasswordVault _vault;
    private readonly IDatabaseClientFactory _clientFactory;
    private readonly Action _onSavedOrCancelled;

    [ObservableProperty]
    private Guid? _id;

    [ObservableProperty]
    private string _name = "New Connection";

    [ObservableProperty]
    private ConnectionType _connectionType = ConnectionType.PostgreSql;

    [ObservableProperty]
    private string _host = "localhost";

    [ObservableProperty]
    private int _port = 5432;

    [ObservableProperty]
    private string _database = "postgres";

    [ObservableProperty]
    private string _username = "postgres";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _ssl = false;

    // SQL Server specific
    [ObservableProperty]
    private bool _useWindowsAuth = false;

    [ObservableProperty]
    private bool _trustServerCertificate = true;

    // MySQL specific
    [ObservableProperty]
    private bool _allowPublicKeyRetrieval = true;

    // SSH Tunnel
    [ObservableProperty]
    private bool _sshEnabled = false;

    [ObservableProperty]
    private string _sshHost = string.Empty;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private string _sshUser = string.Empty;

    [ObservableProperty]
    private string _sshPassword = string.Empty;

    [ObservableProperty]
    private string _sshKeyPath = string.Empty;

    [ObservableProperty]
    private string _labelColor = "#10B981";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string? _testResult;

    [ObservableProperty]
    private bool _isTestSuccess;

    public IReadOnlyList<ConnectionType> AvailableDatabaseTypes { get; } =
    [
        ConnectionType.PostgreSql,
        ConnectionType.Supabase,
        ConnectionType.PocketBase,
        ConnectionType.Appwrite,
        ConnectionType.Firebase,
        ConnectionType.AwsAmplify,
        ConnectionType.MongoDb,
        ConnectionType.Redis,
        ConnectionType.MySql,
        ConnectionType.MariaDB,
        ConnectionType.SqlServer,
        ConnectionType.Sqlite,
        ConnectionType.DuckDb,
        ConnectionType.Oracle,
        ConnectionType.ClickHouse,
        ConnectionType.CockroachDb,
        ConnectionType.Redshift,
        ConnectionType.LibSql,
        ConnectionType.Db2,
        ConnectionType.SapHana,
        ConnectionType.MsAccess,
        ConnectionType.Firebird
    ];

    public bool IsPostgres
    {
        get => ConnectionType is ConnectionType.PostgreSql or ConnectionType.CockroachDb or ConnectionType.Redshift or ConnectionType.Supabase or ConnectionType.AwsAmplify;
        set { if (value) ConnectionType = ConnectionType.PostgreSql; }
    }

    public bool IsSupabase
    {
        get => ConnectionType == ConnectionType.Supabase;
        set { if (value) ConnectionType = ConnectionType.Supabase; }
    }

    public bool IsPocketBase
    {
        get => ConnectionType == ConnectionType.PocketBase;
        set { if (value) ConnectionType = ConnectionType.PocketBase; }
    }

    public bool IsAppwrite
    {
        get => ConnectionType == ConnectionType.Appwrite;
        set { if (value) ConnectionType = ConnectionType.Appwrite; }
    }

    public bool IsFirebase
    {
        get => ConnectionType == ConnectionType.Firebase;
        set { if (value) ConnectionType = ConnectionType.Firebase; }
    }

    public bool IsAwsAmplify
    {
        get => ConnectionType == ConnectionType.AwsAmplify;
        set { if (value) ConnectionType = ConnectionType.AwsAmplify; }
    }

    public bool IsMongoDb
    {
        get => ConnectionType == ConnectionType.MongoDb;
        set { if (value) ConnectionType = ConnectionType.MongoDb; }
    }

    public bool IsRedis
    {
        get => ConnectionType == ConnectionType.Redis;
        set { if (value) ConnectionType = ConnectionType.Redis; }
    }

    public bool IsMySql
    {
        get => ConnectionType == ConnectionType.MySql;
        set { if (value) ConnectionType = ConnectionType.MySql; }
    }

    public bool IsMySqlOrMariaDb => ConnectionType is ConnectionType.MySql or ConnectionType.MariaDB or ConnectionType.Appwrite;

    public bool IsSqlServer
    {
        get => ConnectionType == ConnectionType.SqlServer;
        set { if (value) ConnectionType = ConnectionType.SqlServer; }
    }

    public bool IsSqlite
    {
        get => ConnectionType == ConnectionType.Sqlite;
        set { if (value) ConnectionType = ConnectionType.Sqlite; }
    }

    public bool IsNotSqlite => !IsSqlite;

    public bool IsOracle
    {
        get => ConnectionType == ConnectionType.Oracle;
        set { if (value) ConnectionType = ConnectionType.Oracle; }
    }

    public bool IsClickHouse
    {
        get => ConnectionType == ConnectionType.ClickHouse;
        set { if (value) ConnectionType = ConnectionType.ClickHouse; }
    }

    public bool IsDb2
    {
        get => ConnectionType == ConnectionType.Db2;
        set { if (value) ConnectionType = ConnectionType.Db2; }
    }

    public bool IsSapHana
    {
        get => ConnectionType == ConnectionType.SapHana;
        set { if (value) ConnectionType = ConnectionType.SapHana; }
    }

    public bool IsMsAccess
    {
        get => ConnectionType == ConnectionType.MsAccess;
        set { if (value) ConnectionType = ConnectionType.MsAccess; }
    }

    public bool IsFirebird
    {
        get => ConnectionType == ConnectionType.Firebird;
        set { if (value) ConnectionType = ConnectionType.Firebird; }
    }

    public bool IsFileDatabase => ConnectionType is ConnectionType.Sqlite or ConnectionType.DuckDb or ConnectionType.LibSql or ConnectionType.PocketBase or ConnectionType.MsAccess;
    public bool IsNetworkDatabase => !IsFileDatabase;

    public string DatabaseFieldLabel => ConnectionType switch
    {
        ConnectionType.Oracle => "Service Name / SID (e.g. XE, ORCL)",
        ConnectionType.MongoDb => "Database (Auth Source)",
        ConnectionType.Supabase => "Database (default: postgres)",
        ConnectionType.Redis => "Database Index (0 - 15)",
        ConnectionType.Appwrite => "Database (default: appwrite)",
        ConnectionType.Firebase => "Project ID / Database (default: (default))",
        ConnectionType.AwsAmplify => "Database (default: amplify)",
        ConnectionType.Db2 => "Database Name (e.g. SAMPLE)",
        ConnectionType.SapHana => "Database / Tenant / Instance (e.g. HDB)",
        ConnectionType.Firebird => "Database / File Path (e.g. employee.fdb or C:\\db\\data.fdb)",
        _ => "Default Database"
    };
    public string FileDatabaseHeader => ConnectionType switch
    {
        ConnectionType.DuckDb => "DuckDB Database File",
        ConnectionType.LibSql => "LibSQL / Turso Database File",
        ConnectionType.PocketBase => "PocketBase Database File (pb_data/data.db)",
        ConnectionType.MsAccess => "Microsoft Access Database File (.accdb, .mdb)",
        _ => "SQLite Database File"
    };

    public ConnectionFormViewModel(
        HornetDbContext dbContext,
        MasterPasswordVault vault,
        IDatabaseClientFactory clientFactory,
        Action onSavedOrCancelled,
        SavedConnectionEntity? existing = null)
    {
        _dbContext = dbContext;
        _vault = vault;
        _clientFactory = clientFactory;
        _onSavedOrCancelled = onSavedOrCancelled;

        if (existing != null)
        {
            Id = existing.Id;
            Name = existing.Name;
            ConnectionType = existing.ConnectionType;
            Host = existing.Host;
            Port = existing.Port ?? (ConnectionType == ConnectionType.PostgreSql ? 5432 : ConnectionType == ConnectionType.MySql ? 3306 : 1433);
            Database = existing.DefaultDatabase ?? string.Empty;
            Username = existing.Username ?? string.Empty;
            LabelColor = existing.LabelColor ?? "#10B981";

            if (ConnectionType == ConnectionType.SqlServer && string.IsNullOrWhiteSpace(existing.Username))
            {
                UseWindowsAuth = true;
            }

            if (!string.IsNullOrEmpty(existing.EncryptedPassword))
            {
                try
                {
                    Password = _vault.Decrypt(existing.EncryptedPassword);
                }
                catch
                {
                    Password = string.Empty;
                }
            }

            if (!string.IsNullOrEmpty(existing.SslConfigJson))
            {
                try
                {
                    var sslConfig = JsonSerializer.Deserialize<SslConfig>(existing.SslConfigJson);
                    Ssl = sslConfig?.Enabled ?? false;
                    TrustServerCertificate = !(sslConfig?.RejectUnauthorized ?? true);
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(existing.SshConfigJson))
            {
                try
                {
                    var sshConfig = JsonSerializer.Deserialize<SshTunnelConfig>(existing.SshConfigJson);
                    if (sshConfig != null)
                    {
                        SshEnabled = sshConfig.Enabled;
                        SshHost = sshConfig.Host;
                        SshPort = sshConfig.Port;
                        SshUser = sshConfig.Username;
                        SshKeyPath = sshConfig.PrivateKeyPath ?? string.Empty;
                        SshPassword = sshConfig.Password ?? string.Empty;
                    }
                }
                catch { }
            }
        }
        else
        {
            // Set initial defaults for PostgreSQL
            ApplyDefaultsForType(ConnectionType.PostgreSql);
        }
    }

    partial void OnConnectionTypeChanged(ConnectionType value)
    {
        OnPropertyChanged(nameof(IsPostgres));
        OnPropertyChanged(nameof(IsSupabase));
        OnPropertyChanged(nameof(IsPocketBase));
        OnPropertyChanged(nameof(IsAppwrite));
        OnPropertyChanged(nameof(IsFirebase));
        OnPropertyChanged(nameof(IsAwsAmplify));
        OnPropertyChanged(nameof(IsMongoDb));
        OnPropertyChanged(nameof(IsRedis));
        OnPropertyChanged(nameof(IsMySql));
        OnPropertyChanged(nameof(IsMySqlOrMariaDb));
        OnPropertyChanged(nameof(IsSqlServer));
        OnPropertyChanged(nameof(IsSqlite));
        OnPropertyChanged(nameof(IsNotSqlite));
        OnPropertyChanged(nameof(IsOracle));
        OnPropertyChanged(nameof(IsClickHouse));
        OnPropertyChanged(nameof(IsDb2));
        OnPropertyChanged(nameof(IsSapHana));
        OnPropertyChanged(nameof(IsMsAccess));
        OnPropertyChanged(nameof(IsFileDatabase));
        OnPropertyChanged(nameof(IsNetworkDatabase));
        OnPropertyChanged(nameof(DatabaseFieldLabel));
        OnPropertyChanged(nameof(FileDatabaseHeader));

        ApplyDefaultsForType(value);
    }

    private void ApplyDefaultsForType(ConnectionType type)
    {
        switch (type)
        {
            case ConnectionType.PostgreSql:
                Port = 5432;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb") || Host.EndsWith(".accdb") || Host.Contains("supabase")) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database) || Database == "mysql" || Database == "master" || Database == "data.db" || Database == "admin") Database = "postgres";
                if (string.IsNullOrWhiteSpace(Username) || Username == "root" || Username == "sa" || Username == "system") Username = "postgres";
                if (Id == null && (string.IsNullOrWhiteSpace(Name) || Name.Contains("Database") || Name.Contains("Connection") || Name.Contains("Local") || Name.Contains("Project"))) Name = "PostgreSQL Local";
                break;

            case ConnectionType.Supabase:
                Port = 5432;
                if (string.IsNullOrWhiteSpace(Host) || Host == "localhost" || Host.EndsWith(".db") || Host.EndsWith(".duckdb")) Host = "db.xxxxxx.supabase.co";
                if (string.IsNullOrWhiteSpace(Database)) Database = "postgres";
                if (string.IsNullOrWhiteSpace(Username) || Username == "root" || Username == "sa" || Username == "system") Username = "postgres";
                Ssl = true;
                TrustServerCertificate = true;
                LabelColor = "#3ECF8E";
                if (Id == null) Name = "Supabase Project";
                break;

            case ConnectionType.PocketBase:
                Host = "pb_data/data.db";
                Database = "pb_data/data.db";
                LabelColor = "#B8272C";
                if (Id == null) Name = "PocketBase Project";
                break;

            case ConnectionType.Appwrite:
                Port = 3306;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                Database = "appwrite";
                Username = "root";
                LabelColor = "#FD366E";
                if (Id == null) Name = "Appwrite Project";
                break;

            case ConnectionType.Firebase:
                Port = 8080;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                Database = "(default)";
                LabelColor = "#FFCA28";
                if (Id == null) Name = "Firebase Firestore";
                break;

            case ConnectionType.AwsAmplify:
                Port = 5432;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                Database = "amplify";
                Username = "postgres";
                Ssl = true;
                TrustServerCertificate = true;
                LabelColor = "#FF9900";
                if (Id == null) Name = "AWS Amplify Project";
                break;

            case ConnectionType.MongoDb:
                Port = 27017;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb") || Host.Contains("supabase")) Host = "localhost";
                Database = "admin";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa" || Username == "root") Username = string.Empty;
                Password = string.Empty;
                LabelColor = "#00ED64";
                if (Id == null) Name = "MongoDB Local";
                break;

            case ConnectionType.Redis:
                Port = 6379;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb") || Host.Contains("supabase")) Host = "localhost";
                Database = "0";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa" || Username == "root") Username = string.Empty;
                Password = string.Empty;
                LabelColor = "#DC2626";
                if (Id == null) Name = "Redis Cache";
                break;

            case ConnectionType.CockroachDb:
                Port = 26257;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database)) Database = "defaultdb";
                if (string.IsNullOrWhiteSpace(Username)) Username = "root";
                if (Id == null) Name = "CockroachDB Local";
                break;

            case ConnectionType.Redshift:
                Port = 5439;
                if (string.IsNullOrWhiteSpace(Host)) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database)) Database = "dev";
                if (string.IsNullOrWhiteSpace(Username)) Username = "awsuser";
                if (Id == null) Name = "Amazon Redshift";
                break;

            case ConnectionType.MySql:
                Port = 3306;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database) || Database == "postgres" || Database == "master" || Database == "data.db") Database = "mysql";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa") Username = "root";
                if (Id == null && (string.IsNullOrWhiteSpace(Name) || Name.Contains("Database") || Name.Contains("Connection") || Name.Contains("Local"))) Name = "MySQL Local";
                break;

            case ConnectionType.MariaDB:
                Port = 3306;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database)) Database = "mysql";
                if (string.IsNullOrWhiteSpace(Username)) Username = "root";
                if (Id == null) Name = "MariaDB Local";
                break;

            case ConnectionType.SqlServer:
                Port = 1433;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db")) Host = "localhost";
                if (string.IsNullOrWhiteSpace(Database) || Database == "postgres" || Database == "mysql" || Database == "data.db") Database = "master";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "root") Username = "sa";
                if (Id == null && (string.IsNullOrWhiteSpace(Name) || Name.Contains("Database") || Name.Contains("Connection") || Name.Contains("Local"))) Name = "SQL Server Local";
                break;

            case ConnectionType.Oracle:
                Port = 1521;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb")) Host = "localhost";
                Database = "XE";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa" || Username == "root") Username = "SYSTEM";
                if (Id == null) Name = "Oracle Local";
                break;

            case ConnectionType.ClickHouse:
                Port = 8123;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb")) Host = "localhost";
                Database = "default";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa" || Username == "root") Username = "default";
                if (Id == null) Name = "ClickHouse Local";
                break;

            case ConnectionType.Db2:
                Port = 50000;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".accdb")) Host = "localhost";
                Database = "SAMPLE";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "root") Username = "db2admin";
                LabelColor = "#0062FF";
                if (Id == null) Name = "IBM DB2";
                break;

            case ConnectionType.SapHana:
                Port = 30015;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".accdb")) Host = "localhost";
                Database = "HDB";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "root") Username = "SYSTEM";
                LabelColor = "#008FD3";
                if (Id == null) Name = "SAP HANA";
                break;

            case ConnectionType.MsAccess:
                Port = 0;
                Database = string.Empty;
                Username = string.Empty;
                Password = string.Empty;
                if (string.IsNullOrWhiteSpace(Host) || Host == "localhost") Host = "Database.accdb";
                LabelColor = "#A4373A";
                if (Id == null) Name = "Access Database";
                break;

            case ConnectionType.Firebird:
                Port = 3050;
                if (string.IsNullOrWhiteSpace(Host) || Host.EndsWith(".db") || Host.EndsWith(".duckdb") || Host.EndsWith(".accdb")) Host = "localhost";
                Database = "employee.fdb";
                if (string.IsNullOrWhiteSpace(Username) || Username == "postgres" || Username == "sa" || Username == "root") Username = "SYSDBA";
                if (string.IsNullOrWhiteSpace(Password)) Password = "masterkey";
                LabelColor = "#E25822";
                if (Id == null) Name = "Firebird SQL Server";
                break;

            case ConnectionType.Sqlite:
                Port = 0;
                Database = string.Empty;
                Username = string.Empty;
                Password = string.Empty;
                if (string.IsNullOrWhiteSpace(Host) || Host == "localhost") Host = "data.db";
                if (Id == null && (string.IsNullOrWhiteSpace(Name) || Name.Contains("Database") || Name.Contains("Connection") || Name.Contains("Local"))) Name = "SQLite Database";
                break;

            case ConnectionType.DuckDb:
                Port = 0;
                Database = string.Empty;
                Username = string.Empty;
                Password = string.Empty;
                if (string.IsNullOrWhiteSpace(Host) || Host == "localhost") Host = "analytics.duckdb";
                if (Id == null) Name = "DuckDB Analytics";
                break;

            case ConnectionType.LibSql:
                Port = 0;
                Database = string.Empty;
                Username = string.Empty;
                Password = string.Empty;
                if (string.IsNullOrWhiteSpace(Host) || Host == "localhost") Host = "turso.db";
                if (Id == null) Name = "LibSQL Database";
                break;
        }
    }

    public void SetFileDatabasePath(string path)
    {
        Host = path;
        Database = string.Empty;
        Username = string.Empty;
        Port = 0;

        if (path == ":memory:")
        {
            Name = $"{ConnectionType} (In-Memory)";
            return;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                Name = fileName;
            }
        }
        catch { }
    }

    public void SetSqliteDatabasePath(string path) => SetFileDatabasePath(path);

    public ConnectionConfig BuildConnectionConfig()
    {
        var config = new ConnectionConfig
        {
            Id = Id ?? Guid.NewGuid(),
            Name = Name,
            ConnectionType = ConnectionType,
            Host = Host,
            Port = Port,
            DefaultDatabase = Database,
            Username = (ConnectionType == ConnectionType.SqlServer && UseWindowsAuth) ? null : Username,
            Password = (ConnectionType == ConnectionType.SqlServer && UseWindowsAuth) ? null : Password,
            DatabasePath = IsFileDatabase ? Host : null,
            LabelColor = LabelColor,
            Ssl = new SslConfig
            {
                Enabled = Ssl,
                RejectUnauthorized = !TrustServerCertificate
            },
            Ssh = new SshTunnelConfig
            {
                Enabled = SshEnabled,
                Host = SshHost,
                Port = SshPort,
                Username = SshUser,
                Password = SshPassword,
                PrivateKeyPath = string.IsNullOrWhiteSpace(SshKeyPath) ? null : SshKeyPath
            }
        };

        return config;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResult = "Connecting...";
        IsTestSuccess = false;

        try
        {
            var config = BuildConnectionConfig();
            var client = _clientFactory.CreateClient(config);
            await using (client)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await client.ConnectAsync();
                sw.Stop();
                var version = await client.GetVersionAsync();

                IsTestSuccess = true;
                TestResult = $"✓ Success! Connected in {sw.ElapsedMilliseconds} ms. Server: {version}";
            }
        }
        catch (Exception ex)
        {
            IsTestSuccess = false;
            TestResult = $"✕ Connection Failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var entity = Id.HasValue
            ? await _dbContext.SavedConnections.FirstOrDefaultAsync(c => c.Id == Id.Value)
            : new SavedConnectionEntity { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };

        if (entity == null)
        {
            entity = new SavedConnectionEntity { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        }

        entity.Name = Name;
        entity.ConnectionType = ConnectionType;
        entity.Host = Host;
        entity.Port = Port;
        entity.DefaultDatabase = Database;
        entity.Username = (ConnectionType == ConnectionType.SqlServer && UseWindowsAuth) ? null : Username;
        entity.LabelColor = LabelColor;
        entity.DatabasePath = IsFileDatabase ? Host : null;
        entity.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(Password) && !(ConnectionType == ConnectionType.SqlServer && UseWindowsAuth))
        {
            entity.EncryptedPassword = _vault.Encrypt(Password);
        }
        else
        {
            entity.EncryptedPassword = null;
        }

        entity.SslConfigJson = JsonSerializer.Serialize(new SslConfig
        {
            Enabled = Ssl,
            RejectUnauthorized = !TrustServerCertificate
        });

        if (SshEnabled && !string.IsNullOrWhiteSpace(SshHost))
        {
            var ssh = new SshTunnelConfig
            {
                Enabled = true,
                Host = SshHost,
                Port = SshPort,
                Username = SshUser,
                Password = SshPassword,
                PrivateKeyPath = string.IsNullOrWhiteSpace(SshKeyPath) ? null : SshKeyPath
            };
            entity.SshConfigJson = JsonSerializer.Serialize(ssh);
        }
        else
        {
            entity.SshConfigJson = null;
        }

        if (!Id.HasValue || await _dbContext.SavedConnections.FindAsync(entity.Id) == null)
        {
            _dbContext.SavedConnections.Add(entity);
        }

        await _dbContext.SaveChangesAsync();
        _onSavedOrCancelled();
    }

    [RelayCommand]
    private void Cancel()
    {
        _onSavedOrCancelled();
    }
}
