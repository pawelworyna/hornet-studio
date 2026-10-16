using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Domain.Models;

namespace HornetStudio.Core.Database.Abstractions;

public interface IDatabaseClient : IAsyncDisposable
{
    ConnectionType ConnectionType { get; }
    ConnectionConfig Config { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task<string> GetVersionAsync(CancellationToken ct = default);
    Task<SupportedFeatures> GetSupportedFeaturesAsync();

    // Metadane
    Task<IReadOnlyList<string>> ListDatabasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSchemasAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TableOrView>> ListTablesAsync(string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<TableOrView>> ListViewsAsync(string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<ExtendedTableColumn>> ListTableColumnsAsync(string table, string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<TableIndex>> ListTableIndexesAsync(string table, string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<TableKey>> GetTableKeysAsync(string table, string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<TableTrigger>> ListTableTriggersAsync(string table, string? schema = null, CancellationToken ct = default);
    Task<IReadOnlyList<Routine>> ListRoutinesAsync(CancellationToken ct = default);

    // Zapytania SQL
    Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default);
    Task<QueryResult> ExplainQueryAsync(string sql, bool analyze = false, CancellationToken ct = default);
    IAsyncEnumerable<object?[]> StreamQueryAsync(string sql, CancellationToken ct = default);

    // Przeglądanie i edycja tabel
    Task<TableResult> SelectTopAsync(SelectTopOptions options, CancellationToken ct = default);
    Task<TableUpdateResult> ApplyChangesAsync(TableChanges changes, CancellationToken ct = default);
    Task<string> GenerateCreateTableSqlAsync(string table, string? schema = null, CancellationToken ct = default);
    Task TruncateTableAsync(string table, string? schema = null, CancellationToken ct = default);
    Task DropTableAsync(string table, string? schema = null, CancellationToken ct = default);

    // Identyfikatory i ucieczka znaków
    string WrapIdentifier(string name);
    string EscapeLiteral(string value);
}

public interface IDatabaseClientFactory
{
    IDatabaseClient CreateClient(ConnectionConfig config);
}
