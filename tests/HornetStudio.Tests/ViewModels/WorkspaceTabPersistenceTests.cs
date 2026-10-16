using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Domain.Models;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using HornetStudio.Desktop.ViewModels;
using HornetStudio.Desktop.ViewModels.Tabs;

namespace HornetStudio.Tests.ViewModels;

public class WorkspaceTabPersistenceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly HornetDbContext _dbContext;
    private readonly Mock<IDatabaseClient> _clientMock;
    private readonly Mock<IExportImportService> _exportImportServiceMock;
    private readonly Mock<IBackupRestoreService> _backupRestoreServiceMock;
    private readonly ConnectionConfig _config;

    public WorkspaceTabPersistenceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var options = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new HornetDbContext(options);
        _dbContext.Database.EnsureCreated();

        _config = new ConnectionConfig
        {
            Id = Guid.NewGuid(),
            Name = "Test Persistence DB"
        };

        _clientMock = new Mock<IDatabaseClient>();
        _clientMock.Setup(c => c.Config).Returns(_config);
        _clientMock.Setup(c => c.GetVersionAsync(default)).ReturnsAsync("SQLite 3.40.0");
        _clientMock.Setup(c => c.ListTablesAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(new[] { new TableOrView("users", "main") });
        _clientMock.Setup(c => c.ListViewsAsync(It.IsAny<string?>(), default))
            .ReturnsAsync(Array.Empty<TableOrView>());
        _clientMock.Setup(c => c.ListSchemasAsync(default))
            .ReturnsAsync(new[] { "main" });

        _exportImportServiceMock = new Mock<IExportImportService>();
        _backupRestoreServiceMock = new Mock<IBackupRestoreService>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _sqliteConnection.Dispose();
    }

    private WorkspaceViewModel CreateWorkspace()
    {
        return new WorkspaceViewModel(
            _clientMock.Object,
            _dbContext,
            _exportImportServiceMock.Object,
            _backupRestoreServiceMock.Object,
            onDisconnect: () => { });
    }

    [Fact]
    public async Task RestoreOpenTabsAsync_WhenNoTabsSaved_OpensSingleDefaultQueryTab()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreOpenTabsAsync();

        workspace.Tabs.Tabs.Should().HaveCount(1);
        workspace.Tabs.Tabs[0].Should().BeOfType<QueryEditorViewModel>();
        workspace.Tabs.SelectedTab.Should().Be(workspace.Tabs.Tabs[0]);
    }

    [Fact]
    public async Task SaveOpenTabsAsync_PersistsTabsToDatabase()
    {
        var workspace = CreateWorkspace();
        var queryTab = new QueryEditorViewModel(
            _clientMock.Object,
            _dbContext,
            _exportImportServiceMock.Object,
            initialQuery: "SELECT * FROM users WHERE active = 1;",
            title: "Active Users");

        var tableTab = new TableDataViewModel(
            _clientMock.Object,
            _exportImportServiceMock.Object,
            new TableOrView("users", "main"));

        workspace.Tabs.OpenTab(queryTab);
        workspace.Tabs.OpenTab(tableTab);
        workspace.Tabs.SelectTab(tableTab);

        await workspace.SaveOpenTabsAsync();

        var savedInDb = await _dbContext.OpenTabs
            .Where(t => t.ConnectionId == _config.Id)
            .OrderBy(t => t.Position)
            .ToListAsync();

        savedInDb.Should().HaveCount(2);

        savedInDb[0].TabType.Should().Be("query");
        savedInDb[0].Title.Should().Be("Active Users");
        savedInDb[0].QueryText.Should().Be("SELECT * FROM users WHERE active = 1;");
        savedInDb[0].IsActive.Should().BeFalse();

        savedInDb[1].TabType.Should().Be("table");
        savedInDb[1].TableName.Should().Be("users");
        savedInDb[1].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreOpenTabsAsync_RestoresPreviouslySavedTabsAndActiveSelection()
    {
        // 1. Seed existing open tabs for this connection in DB
        _dbContext.OpenTabs.AddRange(
            new OpenTabEntity
            {
                ConnectionId = _config.Id,
                Position = 0,
                TabType = "query",
                Title = "Report Query",
                QueryText = "SELECT COUNT(*) FROM sales;",
                IsActive = false
            },
            new OpenTabEntity
            {
                ConnectionId = _config.Id,
                Position = 1,
                TabType = "table",
                Title = "users",
                TableName = "users",
                SchemaName = "main",
                IsActive = true
            }
        );
        await _dbContext.SaveChangesAsync();

        // 2. Initialize fresh workspace
        var workspace = CreateWorkspace();
        await workspace.RestoreOpenTabsAsync();

        workspace.Tabs.Tabs.Should().HaveCount(2);

        var firstTab = workspace.Tabs.Tabs[0].Should().BeOfType<QueryEditorViewModel>().Subject;
        firstTab.Title.Should().Be("Report Query");
        firstTab.QueryText.Should().Be("SELECT COUNT(*) FROM sales;");

        var secondTab = workspace.Tabs.Tabs[1].Should().BeOfType<TableDataViewModel>().Subject;
        secondTab.Table.Name.Should().Be("users");

        workspace.Tabs.SelectedTab.Should().Be(secondTab);
    }

    [Fact]
    public async Task DisconnectAsync_SavesTabsBeforeDisconnecting()
    {
        var disconnected = false;
        var workspace = new WorkspaceViewModel(
            _clientMock.Object,
            _dbContext,
            _exportImportServiceMock.Object,
            _backupRestoreServiceMock.Object,
            onDisconnect: () => disconnected = true);

        var qTab = new QueryEditorViewModel(
            _clientMock.Object,
            _dbContext,
            _exportImportServiceMock.Object,
            initialQuery: "SELECT 42;",
            title: "Quick Test");

        workspace.Tabs.OpenTab(qTab);

        await workspace.DisconnectAsync();

        disconnected.Should().BeTrue();

        var saved = await _dbContext.OpenTabs.Where(t => t.ConnectionId == _config.Id).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].Title.Should().Be("Quick Test");
        saved[0].QueryText.Should().Be("SELECT 42;");
    }

    [Fact]
    public async Task RestoreOpenTabsAsync_PersistsLastActiveConnectionIdInUserSettings()
    {
        var workspace = CreateWorkspace();
        await workspace.RestoreOpenTabsAsync();

        var setting = await _dbContext.UserSettings.FirstOrDefaultAsync(s => s.Key == "session:last_active_connection_id");
        setting.Should().NotBeNull();
        setting!.Value.Should().Be(_config.Id.ToString());
    }

    [Fact]
    public async Task QueryDraftChange_UpdatesPersistedTabContent()
    {
        var workspace = CreateWorkspace();
        var qTab = new QueryEditorViewModel(
            _clientMock.Object,
            _dbContext,
            _exportImportServiceMock.Object,
            initialQuery: "SELECT initial;",
            title: "Draft Tab");

        workspace.Tabs.OpenTab(qTab);
        await workspace.SaveOpenTabsAsync();

        // Simulate user typing a new draft query
        qTab.QueryText = "SELECT modified_query_text;";

        // Wait a short time for debounced save or call SaveOpenTabsAsync
        await Task.Delay(400);

        var saved = await _dbContext.OpenTabs
            .Where(t => t.ConnectionId == _config.Id && t.Title == "Draft Tab")
            .FirstOrDefaultAsync();

        saved.Should().NotBeNull();
        saved!.QueryText.Should().Be("SELECT modified_query_text;");
    }
}
