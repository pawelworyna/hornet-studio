# 📖 HornetStudio User Manual & Comprehensive Guide

Welcome to the **HornetStudio** User Manual! This guide covers everything from initial setup and connecting to your databases, to advanced features like visual table building, query execution plans, session persistence, and data import/export.

---

## 📑 Table of Contents

1. [Introduction & Architecture](#1-introduction--architecture)
2. [Installation & Getting Started](#2-installation--getting-started)
   - [Windows (x64 & ARM64)](#windows-x64--arm64)
   - [macOS Apple Silicon (`osx-arm64`)](#macos-apple-silicon-osx-arm64)
   - [Linux (`amd64` & `arm64`)](#linux-amd64--arm64)
3. [Connection Manager & Launchpad](#3-connection-manager--launchpad)
   - [Creating & Managing Connections](#creating--managing-connections)
   - [Local Security Vault (AES-GCM-256)](#local-security-vault-aes-gcm-256)
   - [One-Click Session Recovery](#one-click-session-recovery)
   - [External Connection Profile Importer](#external-connection-profile-importer)
4. [Multi-Engine Database & BaaS Hub](#4-multi-engine-database--baas-hub)
   - [Relational Databases](#relational-databases)
   - [Enterprise Engines (IBM DB2, SAP HANA, Firebird, MS Access, Oracle)](#enterprise-engines)
   - [Analytical Engines (DuckDB, ClickHouse)](#analytical-engines)
   - [Embedded & Distributed Engines (SQLite, LibSQL, CockroachDB, Redshift)](#embedded--distributed-engines)
   - [NoSQL & Cache (MongoDB, Redis)](#nosql--cache)
   - [Backend-as-a-Service (PocketBase, Firebase, Appwrite, AWS Amplify)](#backend-as-a-service-baas)
   - [SSH Tunneling (Bastion / Jump Host)](#ssh-tunneling)
5. [Interactive Table Data Grid](#5-interactive-table-data-grid)
   - [Data Browsing & Pagination](#data-browsing--pagination)
   - [Visual Multi-Filter Builder](#visual-multi-filter-builder)
   - [In-Place Row Editing & Optimistic Concurrency](#in-place-row-editing--optimistic-concurrency)
   - [Cell Inspector & JSON Viewer](#cell-inspector--json-viewer)
6. [SQL Query Editor & Execution Plans](#6-sql-query-editor--execution-plans)
   - [Multi-Tab Workspace](#multi-tab-workspace)
   - [Executing Queries (`Ctrl+Enter` / `F5`)](#executing-queries)
   - [Visual Explain Query Plan (`Ctrl+E`)](#visual-explain-query-plan)
   - [Code Prettifier & SQL Formatter (`Ctrl+B`)](#code-prettifier--sql-formatter)
   - [Query Draft Autosave & Tab Persistence](#query-draft-autosave--tab-persistence)
   - [History & Favorites](#history--favorites)
7. [Sidebar & Navigation](#7-sidebar--navigation)
   - [Database & Schema Switcher](#database--schema-switcher)
   - [Pinned & Starred Tables](#pinned--starred-tables)
   - [Table Context Menu (DDL & SQL Templates)](#table-context-menu)
   - [Quick Search Omnibox (`Ctrl+K` / `Ctrl+P`)](#quick-search-omnibox)
8. [Visual Table Builder GUI](#8-visual-table-builder-gui)
9. [Data Import, Export & Backup Wizards](#9-data-import-export--backup-wizards)
   - [Exporting Data (CSV, JSON, SQL, Excel .xlsx)](#exporting-data)
   - [Data Import Wizard](#data-import-wizard)
   - [Database Backup & Restore Wizard](#database-backup--restore-wizard)
10. [Keyboard Shortcuts Cheatsheet](#10-keyboard-shortcuts-cheatsheet)
11. [Troubleshooting & FAQ](#11-troubleshooting--faq)

---

## 1. Introduction & Architecture

**HornetStudio** is an open-source, native desktop database client engineered for speed, privacy, and low resource overhead.

- **Stack**: Compiled C# on **.NET 11** with **Avalonia UI 11.2** delivering crisp high-DPI rendering and instant startup.
- **Privacy-First**: No telemetry, no remote cloud accounts. All database profiles, connection credentials, and query drafts are stored locally on your machine.
- **Security**: Optional Master Password Vault protecting credentials using **PBKDF2** (100,000 iterations) and **AES-GCM-256** encryption.
- **Multi-Engine**: Over 20 database engines and BaaS platforms supported out-of-the-box through native ADO.NET and REST document adapters.

---

## 2. Installation & Getting Started

### Windows (x64 & ARM64)
- **Installer Wizard (`.exe`)**: Dual-mode Inno Setup installer supporting standard installation into `Program Files` (with admin elevation) or per-user installation into `AppData\Local` (without admin rights).
- **Portable (`.zip`)**: Self-contained archive. Extract anywhere and launch `HornetStudio.Desktop.exe`.

```powershell
# Build Windows installers from source:
.\scripts\build-installer.ps1 -Version 1.0.0 -Architecture all
```

### macOS Apple Silicon (`osx-arm64`)
- **Mountable Disk Image (`.dmg`)**: Native Apple Silicon disk image. Double-click the `.dmg` and drag `HornetStudio.app` into your `/Applications` directory.
- **Portable (`.zip`)**: Direct `.app` bundle archive.

```bash
# Build macOS .dmg from source:
chmod +x scripts/build-dmg.sh
./scripts/build-dmg.sh 1.0.0 osx-arm64
```

### Linux (`amd64` & `arm64`)
- **Debian / Ubuntu (`.deb`)**:
  ```bash
  sudo dpkg -i hornetstudio_1.0.0_amd64.deb
  ```
- **Portable Tarball (`.tar.gz`)**: Extract and run `./HornetStudio.Desktop`.

```bash
# Build Linux packages from source:
chmod +x scripts/build-deb.sh
./scripts/build-deb.sh 1.0.0 linux-x64
./scripts/build-deb.sh 1.0.0 linux-arm64
```

---

## 3. Connection Manager & Launchpad

Upon opening HornetStudio, you are greeted by the **Connection Manager**:

### Creating & Managing Connections
1. Click **✨ New Connection** on the left sidebar.
2. Select your database engine from the **Database Type** dropdown (PostgreSQL, Firebird, DB2, SQLite, MongoDB, etc.).
3. HornetStudio automatically populates sensible defaults (e.g. Port 3050 and `SYSDBA` for Firebird; Port 5432 and `postgres` for PostgreSQL).
4. Fill in connection details (Host, Database, User, Password).
5. Choose a **Label Color** to easily identify environments (e.g., Red for Production, Green for Development).
6. Click **🔌 Test Connection** to verify network reachability and credentials.
7. Click **💾 Save Connection** or **⚡ Connect Now**.

### Local Security Vault (AES-GCM-256)
- Click **🔐 Security Vault** in the Connection Manager to set or change your Master Password.
- Once configured, all stored database passwords and SSH keys are encrypted using **AES-256-GCM** with a key derived via **PBKDF2** with 100,000 SHA-256 iterations.

### One-Click Session Recovery
- HornetStudio keeps track of your active workspace connection.
- When launching the application, click **⚡ Restore Last Session** at the top of the launchpad to immediately reconnect to your last database and restore all previously opened tabs, scroll positions, and query drafts.

### External Connection Profile Importer
- Migrating from another tool or SQLite database configuration?
- Click **📥 Import Connections** to automatically detect and import connection profiles, folders, and saved queries into HornetStudio.

---

## 4. Multi-Engine Database & BaaS Hub

### Relational Databases
- **PostgreSQL & Supabase**:
  - Full schema browsing (`public`, `auth`, `storage`, `realtime`, custom schemas).
  - Direct connection (Port 5432) or PgBouncer connection pooler (Port 6543).
  - SSL/TLS encryption modes.
- **MySQL & MariaDB**:
  - High-performance connection pooling.
  - Native support for MySQL 8+ `caching_sha2_password` and `AllowPublicKeyRetrieval`.
- **Microsoft SQL Server**:
  - Standard SQL authentication or Windows Integrated Authentication (`UseWindowsAuth`).
  - Trust Server Certificate toggle for self-signed development certificates.

### Enterprise Engines
- **Firebird SQL**:
  - Native ADO.NET connection via official `FirebirdSql.Data.FirebirdClient`.
  - Default Port: `3050`, default user: `SYSDBA`. Supports local `.fdb` files or remote servers.
  - System catalog reflection using `RDB$RELATIONS`, `RDB$RELATION_FIELDS`, and `RDB$INDICES`.
  - ANSI SQL-2008 paging with `OFFSET n ROWS FETCH NEXT m ROWS ONLY`.
- **IBM DB2 LUW**:
  - Enterprise DB2 metadata discovery via `SYSCAT.TABLES` and `SYSCAT.COLUMNS`.
  - Standard DB2 SQL paging (`FETCH FIRST n ROWS ONLY`).
- **SAP HANA**:
  - Column table introspection via `SYS.TABLES` and `SYS.TABLE_COLUMNS`.
  - Analytical and transactional in-memory routing.
- **Microsoft Access**:
  - Relational `.accdb` and `.mdb` local file databases.
  - Schema reflection and custom SQL paging.
- **Oracle Database**:
  - Official managed ODP.NET driver.
  - Supports EZConnect format, TNS aliases, and Service Name / SID resolution.

### Analytical Engines
- **DuckDB**: Fast in-process columnar database. Open local `.duckdb` files or run in-memory `:memory:`.
- **ClickHouse**: Ultra-fast analytical column store via native HTTP protocol.

### Embedded & Distributed Engines
- **SQLite & LibSQL / Turso**: Single-file `.db`, `.sqlite`, `.sqlite3` files or in-memory execution.
- **CockroachDB & Amazon Redshift**: Cloud distributed SQL databases with PostgreSQL protocol compatibility.

### NoSQL & Cache
- **MongoDB**: Connect to MongoDB clusters (Port 27017). Collections are represented as tables with dynamic schema sampling and MQL/JSON query execution.
- **Redis**: Connect to Redis instances (Port 6379). Browse keys, inspect TTLs, view and edit JSON/string values, and execute Redis commands.

### Backend-as-a-Service (BaaS)
- **PocketBase**: Dual-mode client supporting direct SQLite file mode (`pb_data/data.db`) or REST & Document Client mode via HTTP API (`http://127.0.0.1:8090`).
- **Firebase Firestore**: Dedicated REST & Document Client connecting to Firestore REST API or local emulator (Port 8080). Handles wrapped Firestore JSON fields (`stringValue`, `integerValue`, `timestampValue`, `mapValue`, `arrayValue`).
- **Appwrite**: Direct database client configured for Appwrite MariaDB/MySQL storage.
- **AWS Amplify**: Direct Aurora/PostgreSQL connectivity with default SSL/TLS encryption.

### SSH Tunneling
Connect securely to databases located inside private VPCs or behind firewalls:
1. In the Connection Form, expand **SSH Tunnel Settings**.
2. Check **Use SSH Tunnel**.
3. Provide the bastion host, port (default: 22), username, and authentication method (Password or Private Key file).

---

## 5. Interactive Table Data Grid

### Data Browsing & Pagination
- **Double-click** any table or view in the sidebar (or right-click → **📄 View Data**) to open the **Table Data Tab**.
- **Right-click** any table or view for immediate access to **⚙️ View Structure & DDL**, quick SQL generation (SELECT, INSERT, DDL, TRUNCATE, DROP), qualified name copying, and pinning.
- Records are loaded in configurable batches (default: 50 rows per page) with swift pagination controls at the bottom bar.
- Click any column header to sort ascending or descending.

### Visual Multi-Filter Builder
- Click **Filter** above the data grid to open the multi-condition filter bar.
- Supported operators: `=`, `!=`, `>`, `<`, `>=`, `<=`, `LIKE`, `ILIKE`, `IN`, `IS NULL`, and `IS NOT NULL`.
- Add multiple filter rules simultaneously. Column names remain visible when filters are applied.
- Active filters display a status badge with one-click clearing.

### In-Place Row Editing & Optimistic Concurrency
- Double-click any cell to edit its value directly in the grid.
- Click **➕ Insert Row** to append new records.
- Select rows and click **🗑️ Delete Selected** to stage deletions.
- Click **💾 Save Changes** to commit all staged inserts, updates, and deletes in a single transaction.
- **Smart Concurrency**: HornetStudio automatically uses Primary Keys, SQLite `rowid`, or PostgreSQL `ctid` for safe record identification. For tables without primary keys, optimistic concurrency matches all original column values.

### Cell Inspector & JSON Viewer
- Right-click any cell (or click the inspect button) to open the **Cell Inspector Drawer**.
- Formats multi-line text and beautifies embedded JSON structures with syntax highlighting and one-click copy to clipboard.

---

## 6. SQL Query Editor & Execution Plans

### Multi-Tab Workspace
- Press `Ctrl+T` or click **➕ New Query** to open independent query tabs.
- Rename tabs by double-clicking the tab title.

### Executing Queries
- **Execute Entire Script**: Press `F5`.
- **Execute Current / Selected Statement**: Highlight any SQL text and press `Ctrl+Enter`.
- Query execution duration and affected row counts appear immediately in the status bar.

### Visual Explain Query Plan (`Ctrl+E`)
- Want to inspect how the database optimizer executes your query?
- Press `Ctrl+E` or click **🔍 Explain**.
- HornetStudio executes an execution plan (`EXPLAIN` / `EXPLAIN ANALYZE` / Firebird `PLAN`) and formats the operations into an interactive tree displaying cost, rows, and scan types.

### Code Prettifier & SQL Formatter (`Ctrl+B`)
- Press `Ctrl+B` (or `Ctrl+Shift+F`) to instantly format messy SQL into clean, indented code with uppercase keywords.

### Query Draft Autosave & Tab Persistence
- Never lose your work: As you type in any query tab, HornetStudio automatically saves your draft to the local SQLite database with a 300ms debounce timer.
- If you close the application, restart your computer, or experience a power failure, relaunching HornetStudio immediately restores all open query tabs, titles, and draft queries.

### History & Favorites
- In the sidebar, switch to the **History** tab to view your past executed queries with timestamps and durations.
- Click the **⭐ Star** icon next to any query to save it to your **Favorites** collection for quick reuse.

---

## 7. Sidebar & Navigation

### Database & Schema Switcher
- At the top of the sidebar, select between databases and schemas (e.g. `public`, `auth`, `storage` in PostgreSQL/Supabase) without disconnecting.

### Pinned & Starred Tables
- Hover over any table or view in the sidebar and click the **📌 Pin** icon.
- Pinned items stay grouped at the very top of the list for instant access.

### Table Context Menu
Right-click any table in the sidebar to access:
- **Open Data**: Browse rows in the DataGrid.
- **Table Properties**: View columns, data types, indexes, and foreign keys.
- **Generate DDL (CREATE TABLE)**: View and copy the exact DDL script.
- **Generate SELECT / INSERT Template**: Populates a new query tab with prepared SQL.
- **Copy Table Name**: Copies the escaped identifier to clipboard.

### Quick Search Omnibox (`Ctrl+K` / `Ctrl+P`)
- Press `Ctrl+K` or `Ctrl+P` anywhere in the app to open the omnibox modal.
- Type any part of a table name, view name, saved query, or history item to navigate to it instantly.

---

## 8. Visual Table Builder GUI

Need to design a new table without remembering exact DDL syntax?
1. Click **➕ New Table** at the top of the sidebar.
2. Enter your new table name.
3. Add columns visually:
   - Column Name
   - Data Type (dropdown adapted to your active database engine)
   - Primary Key checkbox
   - Allow NULL checkbox
   - Default Value
4. As you add or edit columns, the **Live DDL Preview** updates in real time.
5. Click **Create Table** to execute the DDL directly.

---

## 9. Data Import, Export & Backup Wizards

### Exporting Data
- From any DataGrid or Query Result tab, click **Export**:
  - **CSV**: Configurable delimiter (comma, semicolon, tab) and quote escaping.
  - **JSON**: Formatted JSON array of records.
  - **SQL**: Formatted `INSERT INTO` statements wrapped in a transaction (`BEGIN / COMMIT`).
  - **Excel (`.xlsx`)**: Native OpenXML spreadsheet format with styled headers and auto-column widths.

### Data Import Wizard
- Click **📥 Import Data** from the workspace toolbar:
  1. Select a target database table.
  2. Select your source **CSV** or **JSON** file.
  3. HornetStudio auto-detects column delimiters and shows a live preview grid.
  4. Click **Start Import** to execute batched, transactional inserts.

### Database Backup & Restore Wizard
- Click **📦 Backup / Dump**:
  - Choose scope: **Structure & Data**, **Structure Only (DDL)**, or **Data Only**.
  - Select tables via checklist.
  - Toggle options: `DROP TABLE IF EXISTS`, Foreign Keys, Indexes, Views.
  - Export full backup file.
- Use the **Restore Wizard** to execute SQL backup dumps safely into the target database.

---

## 10. Keyboard Shortcuts Cheatsheet

| Shortcut | Action | Scope |
| :--- | :--- | :--- |
| `Ctrl + Enter` | Execute current statement / selection | SQL Query Editor |
| `F5` | Execute entire script | SQL Query Editor |
| `Ctrl + E` | Explain Query Plan | SQL Query Editor |
| `Ctrl + B` / `Ctrl + Shift + F` | Prettify & Format SQL Code | SQL Query Editor |
| `Ctrl + T` | Open New Query Tab | Workspace |
| `Ctrl + W` | Close Active Tab | Workspace |
| `Ctrl + K` / `Ctrl + P` | Open Quick Search Omnibox | Global |
| `Ctrl + S` | Save Open Tabs & Staged DataGrid Changes | Workspace |
| `F6` | Refresh Active Table / Sidebar | Workspace |

---

## 11. Troubleshooting & FAQ

### Q: Why does Microsoft Access show a driver error on Windows?
**A**: Microsoft Access requires the Microsoft Access Database Engine (ACE/ODBC driver). Install the free Microsoft Access Database Engine Redistributable from Microsoft if it is not present on your system.

### Q: Why does Firebird fail to connect on localhost:3050?
**A**: Ensure the Firebird server service (`FirebirdServerDefaultInstance`) is running. If connecting to a local database file, ensure the file path is accessible to the Firebird server process. Default credentials are `SYSDBA` / `masterkey`.

### Q: How do I connect to PocketBase?
**A**:
- **Direct Mode**: Select `PocketBase` and specify the file path `pb_data/data.db`.
- **REST Mode**: Select `PocketBase`, set Host to `http://127.0.0.1:8090`, and HornetStudio will use the PocketBase REST & Document API.

### Q: Where are my saved connections stored?
**A**: All settings and connection profiles are stored locally in SQLite at `%APPDATA%\HornetStudio\hornet_studio.db` (Windows) or `~/.config/hornetstudio/` (Linux/macOS).

---

*HornetStudio — Fast, Native, and Private Database Management.*
