using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HornetStudio.Core.Domain.Enums;

namespace HornetStudio.Core.Storage.Entities;

[Table("saved_connections")]
public class SavedConnectionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public ConnectionType ConnectionType { get; set; }

    [MaxLength(500)]
    public string Host { get; set; } = "localhost";

    public int? Port { get; set; }

    [MaxLength(200)]
    public string? DefaultDatabase { get; set; }

    [MaxLength(200)]
    public string? Username { get; set; }

    public string? EncryptedPassword { get; set; }

    public string? DatabasePath { get; set; }

    public bool UseConnectionString { get; set; }

    public string? EncryptedConnectionString { get; set; }

    public string? SslConfigJson { get; set; }

    public string? SshConfigJson { get; set; }

    [MaxLength(20)]
    public string? LabelColor { get; set; }

    public Guid? FolderId { get; set; }

    public bool ReadOnlyMode { get; set; }

    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(FolderId))]
    public ConnectionFolderEntity? Folder { get; set; }
}

[Table("connection_folders")]
public class ConnectionFolderEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    public int Position { get; set; }

    public List<SavedConnectionEntity> Connections { get; set; } = [];
}

[Table("favorite_queries")]
public class FavoriteQueryEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string QueryText { get; set; } = string.Empty;

    public ConnectionType? DatabaseType { get; set; }

    public Guid? FolderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(FolderId))]
    public QueryFolderEntity? Folder { get; set; }
}

[Table("query_folders")]
public class QueryFolderEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int Position { get; set; }

    public List<FavoriteQueryEntity> Queries { get; set; } = [];
}

[Table("query_history")]
public class QueryHistoryEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string QueryText { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ConnectionName { get; set; }

    [MaxLength(200)]
    public string? DatabaseName { get; set; }

    public long DurationMs { get; set; }

    public long AffectedRows { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Completed";

    public string? ErrorMessage { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

[Table("open_tabs")]
public class OpenTabEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConnectionId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(50)]
    public string TabType { get; set; } = "query"; // "query", "table", "table-properties", "backup", "import"

    public string? QueryText { get; set; }

    [MaxLength(200)]
    public string? TableName { get; set; }

    [MaxLength(200)]
    public string? SchemaName { get; set; }

    public int Position { get; set; }

    public bool IsActive { get; set; }
}

[Table("user_settings")]
public class UserSettingEntity
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
