using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using HornetStudio.Core.Domain.Enums;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HornetStudio.Tests.SampleData;

public class SampleDatabaseGeneratorTests
{
    [Fact]
    public async Task GenerateSampleDatabase_AndRegisterConnection()
    {
        // 1. Target database path in project root
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleDbPath = Path.Combine(projectDir, "sample_hornet.db");
        var sampleCsvPath = Path.Combine(projectDir, "samples", "sample_import_customers.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(sampleCsvPath)!);

        if (File.Exists(sampleDbPath))
        {
            File.Delete(sampleDbPath);
        }

        // 2. Create and populate SQLite database
        using (var connection = new SqliteConnection($"Data Source={sampleDbPath}"))
        {
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = ON;

                -- 1. Categories (PK)
                CREATE TABLE categories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    description TEXT
                );

                INSERT INTO categories (name, description) VALUES
                    ('Elektronika', 'Smartfony, laptopy i akcesoria'),
                    ('Oprogramowanie', 'Licencje deweloperskie i systemowe'),
                    ('Bazy Danych', 'Książki, kursy i narzędzia bazodanowe'),
                    ('Serwery & Chmura', 'Hardware serwerowy i infrastruktura'),
                    ('Akcesoria Hornet', 'Oficjalny merch HornetStudio');

                -- 2. Products (PK + FK)
                CREATE TABLE products (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
                    name TEXT NOT NULL,
                    sku TEXT UNIQUE NOT NULL,
                    price REAL NOT NULL,
                    stock INTEGER NOT NULL DEFAULT 0,
                    is_available INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );

                INSERT INTO products (category_id, name, sku, price, stock, is_available) VALUES
                    (1, 'Laptop Hornet Ultra 15', 'HNT-LPT-01', 5499.00, 12, 1),
                    (1, 'Monitor 4K 27 IPS', 'HNT-MON-27', 1899.50, 25, 1),
                    (1, 'Klawiatura Mechaniczna RGB', 'HNT-KEY-RGB', 399.00, 48, 1),
                    (1, 'Mysz Bezprzewodowa 16k DPI', 'HNT-MOU-WL', 249.99, 60, 1),
                    (2, 'HornetStudio Pro Lifetime', 'SW-HNT-PRO', 799.00, 999, 1),
                    (2, 'Licencja PostgreSQL Enterprise Tools', 'SW-PG-ENT', 1200.00, 50, 1),
                    (3, 'Podręcznik High-Performance SQL', 'BK-SQL-HP', 129.90, 80, 1),
                    (3, 'Architektura Baz Danych w .NET 11', 'BK-NET11-DB', 149.00, 40, 1),
                    (4, 'Serwer Rack 1U Xeon Gold', 'SRV-1U-XG', 12500.00, 4, 1),
                    (4, 'Switch Zarządzalny 24x 10GbE', 'NET-SW-24X', 3800.00, 8, 1),
                    (5, 'Koszulka Green Hornet (Rozmiar L)', 'TSH-HNT-GRN-L', 89.00, 100, 1),
                    (5, 'Kubek Termiczny Hornet Black/Green', 'MUG-HNT-01', 59.00, 150, 1);

                -- 3. Users (PK)
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT UNIQUE NOT NULL,
                    email TEXT UNIQUE NOT NULL,
                    full_name TEXT NOT NULL,
                    role TEXT NOT NULL DEFAULT 'user',
                    balance REAL NOT NULL DEFAULT 0.0,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );

                INSERT INTO users (username, email, full_name, role, balance, is_active) VALUES
                    ('admin_pawel', 'pawel@hornetstudio.io', 'Paweł Administrator', 'admin', 5000.00, 1),
                    ('jan_kowalski', 'jan.kowalski@example.com', 'Jan Kowalski', 'user', 1450.50, 1),
                    ('anna_nowak', 'anna.nowak@company.pl', 'Anna Nowak', 'user', 3200.00, 1),
                    ('marek_zielinski', 'marek.z@techdev.eu', 'Marek Zieliński', 'developer', 800.00, 1),
                    ('ewa_wisniewska', 'ewa.w@finanse.pl', 'Ewa Wiśniewska', 'analyst', 4120.75, 1),
                    ('tomasz_wojcik', 't.wojcik@soft.org', 'Tomasz Wójcik', 'user', 0.00, 0),
                    ('kasia_dabrowska', 'kasia.d@design.io', 'Katarzyna Dąbrowska', 'user', 650.20, 1);

                -- 4. Orders (PK + FK)
                CREATE TABLE orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    order_number TEXT UNIQUE NOT NULL,
                    total_amount REAL NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    created_at TEXT NOT NULL DEFAULT (datetime('now'))
                );

                INSERT INTO orders (user_id, order_number, total_amount, status) VALUES
                    (1, 'ORD-2026-001', 5898.00, 'completed'),
                    (2, 'ORD-2026-002', 799.00, 'completed'),
                    (3, 'ORD-2026-003', 2149.49, 'shipped'),
                    (4, 'ORD-2026-004', 129.90, 'pending'),
                    (5, 'ORD-2026-005', 13389.00, 'processing'),
                    (7, 'ORD-2026-006', 148.00, 'completed');

                -- 5. Order Items (PK + FK)
                CREATE TABLE order_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
                    product_id INTEGER NOT NULL REFERENCES products(id),
                    quantity INTEGER NOT NULL DEFAULT 1,
                    unit_price REAL NOT NULL
                );

                INSERT INTO order_items (order_id, product_id, quantity, unit_price) VALUES
                    (1, 1, 1, 5499.00),
                    (1, 3, 1, 399.00),
                    (2, 5, 1, 799.00),
                    (3, 2, 1, 1899.50),
                    (3, 4, 1, 249.99),
                    (4, 7, 1, 129.90),
                    (5, 9, 1, 12500.00),
                    (5, 11, 1, 89.00),
                    (6, 11, 1, 89.00),
                    (6, 12, 1, 59.00);

                -- ==============================================================
                -- 6. TABELA BEZ KLUCZA GŁÓWNEGO: audit_logs (NO PRIMARY KEY)
                -- Cel: Demonstracja i weryfikacja funkcji edycji tabel bez PK!
                -- HornetStudio identyfikuje i edytuje wiersze za pomocą ukrytego rowid!
                -- ==============================================================
                CREATE TABLE audit_logs (
                    event_time TEXT NOT NULL,
                    user_email TEXT,
                    action TEXT NOT NULL,
                    details TEXT,
                    ip_address TEXT
                );

                INSERT INTO audit_logs (event_time, user_email, action, details, ip_address) VALUES
                    ('2026-10-15 08:30:12', 'pawel@hornetstudio.io', 'USER_LOGIN', 'Pomyślne logowanie administratora', '192.168.1.10'),
                    ('2026-10-15 08:35:45', 'pawel@hornetstudio.io', 'CONFIG_UPDATE', 'Zaktualizowano konfigurację', '192.168.1.10'),
                    ('2026-10-15 09:12:00', 'jan.kowalski@example.com', 'ORDER_CREATED', 'Utworzono zamówienie ORD-2026-002', '83.24.112.5'),
                    ('2026-10-15 10:04:19', 'anna.nowak@company.pl', 'PASSWORD_RESET', 'Wysłano link resetujący hasło', '188.146.3.22'),
                    ('2026-10-15 11:20:33', 'marek.z@techdev.eu', 'QUERY_EXEC', 'Uruchomiono raport analityczny produktów', '10.0.4.88'),
                    ('2026-10-15 12:45:10', 'ewa.w@finanse.pl', 'PAYMENT_RECEIVED', 'Zaksięgowano wpłatę 4120.75 PLN', '195.117.8.9'),
                    ('2026-10-15 13:15:00', 'system', 'BACKUP_COMPLETED', 'Automatyczny backup bazy danych do pliku', '127.0.0.1'),
                    ('2026-10-15 14:02:44', 'pawel@hornetstudio.io', 'MODULE_ENABLED', 'Włączono moduł kopii zapasowej', '192.168.1.10');

                -- ==============================================================
                -- 7. TABELA BEZ KLUCZA GŁÓWNEGO: sensor_telemetry (NO PRIMARY KEY)
                -- ==============================================================
                CREATE TABLE sensor_telemetry (
                    device_code TEXT NOT NULL,
                    temperature REAL NOT NULL,
                    humidity REAL NOT NULL,
                    voltage REAL NOT NULL,
                    recorded_at TEXT NOT NULL
                );

                INSERT INTO sensor_telemetry (device_code, temperature, humidity, voltage, recorded_at) VALUES
                    ('SRV-ROOM-A1', 21.4, 45.2, 230.1, '2026-10-15 12:00:00'),
                    ('SRV-ROOM-A1', 22.1, 46.0, 229.8, '2026-10-15 12:15:00'),
                    ('SRV-ROOM-A1', 22.8, 44.8, 230.4, '2026-10-15 12:30:00'),
                    ('RACK-B2-SWITCH', 34.2, 38.5, 12.05, '2026-10-15 12:00:00'),
                    ('RACK-B2-SWITCH', 35.0, 37.9, 11.98, '2026-10-15 12:15:00'),
                    ('UPS-MAIN-01', 26.5, 41.0, 231.2, '2026-10-15 12:00:00'),
                    ('UPS-MAIN-01', 26.8, 41.2, 230.9, '2026-10-15 12:30:00');

                -- 8. Widoki (Views)
                CREATE VIEW v_active_users AS
                    SELECT id, username, email, full_name, role, balance, created_at
                    FROM users
                    WHERE is_active = 1;

                CREATE VIEW v_order_details AS
                    SELECT 
                        o.id AS order_id,
                        o.order_number,
                        u.full_name AS customer,
                        u.email,
                        p.name AS product_name,
                        c.name AS category_name,
                        oi.quantity,
                        oi.unit_price,
                        (oi.quantity * oi.unit_price) AS line_total,
                        o.status,
                        o.created_at
                    FROM orders o
                    JOIN users u ON o.user_id = u.id
                    JOIN order_items oi ON o.id = oi.order_id
                    JOIN products p ON oi.product_id = p.id
                    LEFT JOIN categories c ON p.category_id = c.id;

                -- 9. Indeksy
                CREATE INDEX idx_products_sku ON products(sku);
                CREATE INDEX idx_products_category ON products(category_id);
                CREATE INDEX idx_orders_user ON orders(user_id);
                CREATE INDEX idx_users_email ON users(email);

                -- 10. Trigger
                CREATE TRIGGER trg_audit_order_insert
                AFTER INSERT ON orders
                BEGIN
                    INSERT INTO audit_logs (event_time, user_email, action, details, ip_address)
                    VALUES (
                        datetime('now'),
                        'system',
                        'ORDER_INSERTED',
                        'Nowe zamówienie: ' || NEW.order_number || ', Kwota: ' || NEW.total_amount,
                        '127.0.0.1'
                    );
                END;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        File.Exists(sampleDbPath).Should().BeTrue();

        // 3. Create Sample CSV file for testing CSV Import Wizard
        var csvContent = @"first_name,last_name,email,company,city,credit_limit
Michał,Kaczmarek,m.kaczmarek@cloudnet.pl,CloudNet Solutions,Warszawa,15000.00
Monika,Adamska,monika.adamska@fintech.io,FinTech Polska,Kraków,25000.00
Piotr,Zieliński,p.zielinski@devcorp.com,DevCorp,Wrocław,8000.00
Karolina,Wójcik,k.wojcik@agencja-karo.pl,Agencja Karo,Poznań,12500.00
Robert,Nowicki,robert@nowicki-it.pl,Nowicki IT Consulting,Gdańsk,18000.00";

        await File.WriteAllTextAsync(sampleCsvPath, csvContent.Trim());
        File.Exists(sampleCsvPath).Should().BeTrue();

        // 4. Pre-register this connection in HornetStudio's local database!
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HornetStudio");
        Directory.CreateDirectory(appDataDir);
        var appDbPath = Path.Combine(appDataDir, "hornet_studio.db");

        var dbOpts = new DbContextOptionsBuilder<HornetDbContext>()
            .UseSqlite($"Data Source={appDbPath}")
            .Options;

        using (var appDb = new HornetDbContext(dbOpts))
        {
            await appDb.Database.EnsureCreatedAsync();

            var existingList = await appDb.SavedConnections
                .Where(c => c.Name.Contains("Test Database") || c.Name.Contains("Sample Hornet DB") || c.Name.Contains("Sample Database"))
                .ToListAsync();

            if (existingList.Count > 0)
            {
                var keep = existingList[0];
                keep.Name = "🎯 Test Database (Sample Hornet DB)";
                keep.Host = sampleDbPath;
                keep.DatabasePath = sampleDbPath;
                keep.UpdatedAt = DateTime.UtcNow;

                // Usuń nadmiarowe duplikaty
                for (int i = 1; i < existingList.Count; i++)
                {
                    appDb.SavedConnections.Remove(existingList[i]);
                }
            }
            else
            {
                var conn = new SavedConnectionEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "🎯 Test Database (Sample Hornet DB)",
                    ConnectionType = ConnectionType.Sqlite,
                    Host = sampleDbPath,
                    DatabasePath = sampleDbPath,
                    LabelColor = "#10B981",
                    IsPinned = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                appDb.SavedConnections.Add(conn);
            }

            // Also add a sample favorite query
            var queryExisting = await appDb.FavoriteQueries.FirstOrDefaultAsync(q => q.Title.Contains("Przegląd"));
            if (queryExisting == null)
            {
                appDb.FavoriteQueries.Add(new FavoriteQueryEntity
                {
                    Id = Guid.NewGuid(),
                    Title = "⭐ Przegląd Sprzedaży (Sample Query)",
                    QueryText = @"-- Zestawienie sprzedaży z podziałem na kategorie
SELECT 
    c.name AS Kategoria,
    COUNT(DISTINCT o.id) AS Liczba_Zamowien,
    SUM(oi.quantity) AS Sprzedanych_Sztuk,
    ROUND(SUM(oi.quantity * oi.unit_price), 2) AS Przychod_PLN
FROM categories c
JOIN products p ON c.id = p.category_id
JOIN order_items oi ON p.id = oi.product_id
JOIN orders o ON oi.order_id = o.id
GROUP BY c.id
ORDER BY Przychod_PLN DESC;",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await appDb.SaveChangesAsync();
        }
    }
}
