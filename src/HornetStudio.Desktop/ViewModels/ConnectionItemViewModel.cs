using System;
using CommunityToolkit.Mvvm.ComponentModel;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Storage.Entities;

namespace HornetStudio.Desktop.ViewModels;

public sealed class ConnectionItemViewModel : ObservableObject
{
    public SavedConnectionEntity Entity { get; }

    public Guid Id => Entity.Id;
    public string Name => Entity.Name;
    public ConnectionType ConnectionType => Entity.ConnectionType;
    public string Host => Entity.Host;
    public int? Port => Entity.Port;
    public string? Database => Entity.DefaultDatabase;
    public string? Username => Entity.Username;
    public string LabelColor => Entity.LabelColor ?? "#10B981";

    public string Subtitle
    {
        get
        {
            if (ConnectionType is ConnectionType.Sqlite or ConnectionType.DuckDb or ConnectionType.LibSql or ConnectionType.PocketBase or ConnectionType.MsAccess)
            {
                return string.IsNullOrWhiteSpace(Entity.DatabasePath) ? (Entity.Host ?? $"{ConnectionType}") : Entity.DatabasePath;
            }
            return $"{Username}@{Host}:{Port}/{Database}";
        }
    }

    public string DriverBadge => ConnectionType switch
    {
        ConnectionType.Sqlite => "SQLite",
        ConnectionType.PostgreSql => "PostgreSQL",
        ConnectionType.MySql => "MySQL",
        ConnectionType.MariaDB => "MariaDB",
        ConnectionType.SqlServer => "SQL Server",
        ConnectionType.CockroachDb => "CockroachDB",
        ConnectionType.Redshift => "Redshift",
        ConnectionType.LibSql => "LibSQL",
        ConnectionType.DuckDb => "DuckDB",
        ConnectionType.Oracle => "Oracle",
        ConnectionType.ClickHouse => "ClickHouse",
        ConnectionType.MongoDb => "MongoDB",
        ConnectionType.Supabase => "Supabase",
        ConnectionType.Redis => "Redis",
        ConnectionType.PocketBase => "PocketBase",
        ConnectionType.Appwrite => "Appwrite",
        ConnectionType.Firebase => "Firebase",
        ConnectionType.AwsAmplify => "AWS Amplify",
        ConnectionType.Db2 => "IBM DB2",
        ConnectionType.SapHana => "SAP HANA",
        ConnectionType.MsAccess => "MS Access",
        ConnectionType.Firebird => "Firebird",
        _ => ConnectionType.ToString()
    };

    public ConnectionItemViewModel(SavedConnectionEntity entity)
    {
        Entity = entity;
    }
}
