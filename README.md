<div align="center">

<img src="src/HornetStudio.Desktop/Assets/hornet.png" alt="HornetStudio Logo" width="128" height="128" />

# HornetStudio

**A fast, modern, and privacy-first database IDE for PostgreSQL, MongoDB, Supabase, MySQL, MariaDB, SQLite, DuckDB, Oracle, ClickHouse, SQL Server, IBM DB2, SAP HANA, Microsoft Access, Firebird, CockroachDB, Amazon Redshift, and LibSQL.**

[![.NET 11](https://img.shields.io/badge/.NET-11.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.2-8B5CF6?logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-3B82F6)](https://github.com)
[![Build & Test](https://github.com/pawelworyna/hornet-studio/actions/workflows/ci.yml/badge.svg)](https://github.com/pawelworyna/hornet-studio/actions)

</div>

---

## 📖 Overview

**HornetStudio** is a desktop database client designed for developers who want a responsive, distraction-free environment to query, inspect, and manage databases and backend-as-a-service platforms. Built from the ground up with **C#**, **.NET 11**, and **Avalonia UI**, HornetStudio provides the speed and low resource usage of a compiled native application with a sleek, polished aesthetic designed for modern developer workflows.

> [!TIP]
> 📚 **Looking for the complete documentation?** Check out the comprehensive [**HornetStudio User Manual**](docs/USER_MANUAL.md) covering installation, supported engines, data grid filtering, SQL editor, and troubleshooting.

---

## ✨ Features

### 🔌 Multi-Engine Database & BaaS Support
- **PostgreSQL & Supabase**: Full SSL/TLS support, schemas (`public`, `auth`, `storage`, `realtime`), direct (5432) and PgBouncer connection pooler (6543).
- **MongoDB**: NoSQL document database powered by the official `MongoDB.Driver`. Dynamic schema sampling for collections, DataGrid editing, and MQL/JSON query execution.
- **Redis Key-Value & Cache**: Real-time Redis key explorer, virtual `keys` table, pattern scanning, inline string/JSON value editor, TTL viewer, and command console.
- **Backend-as-a-Service (BaaS) Hub**: First-class support for **PocketBase** (dual-mode: direct SQLite file `pb_data/data.db` and HTTP REST & Document API on port 8090), **Appwrite** (MariaDB/MySQL), **Firebase Firestore** (Local Emulator & REST Document API with typed field unwrapping), and **AWS Amplify** (Aurora/PostgreSQL).
- **IBM DB2**: Enterprise DB2 LUW support with catalog metadata discovery (`SYSCAT.TABLES`, `SYSCAT.COLUMNS`), schema inspection, and DB2 SQL paging.
- **SAP HANA**: In-memory analytical and transactional database connectivity with column table introspection (`SYS.TABLES`, `SYS.TABLE_COLUMNS`) and instance routing.
- **Microsoft Access**: Single-file relational database engine (`.accdb`, `.mdb`) with table schema reflection, custom SQL pagination, and DataGrid editing.
- **Firebird**: Relational SQL database engine supporting `.fdb` files and network servers (default port 3050, `SYSDBA`), native `FirebirdSql.Data.FirebirdClient` provider, `RDB$` system catalog introspection, and `FIRST/SKIP / ROWS` paging.
- **MySQL & MariaDB**: Native high-performance drivers, connection pooling, and SSL modes.
- **SQLite**: Local single-file databases (`.db`, `.sqlite`, `.sqlite3`), auto-discovery, and in-memory execution.
- **DuckDB**: Fast in-process analytical (OLAP) database engine supporting `.duckdb` files and `:memory:`.
- **Oracle Database**: Enterprise Oracle support via managed ODP.NET driver with TNS/EZConnect and Service Name / SID resolution.
- **ClickHouse**: Ultra-fast column-oriented database for real-time analytics with native HTTP ADO client.
- **Microsoft SQL Server**: Windows authentication, SQL Server authentication, and custom instance routing.
- **CockroachDB**: Distributed SQL support with PostgreSQL wire protocol compatibility.
- **Amazon Redshift**: Cloud data warehouse connectivity with PostgreSQL wire compatibility.
- **LibSQL / Turso**: Local and edge-distributed SQLite-compatible databases.

### 🔍 Table Data Grid & Advanced Filtering
- Interactive tabular data browser with paginated scrolling and sorting.
- **Visual Multi-Filter Builder**:
  - Filter columns with operators: `=`, `!=`, `>`, `<`, `>=`, `<=`, `LIKE`, `ILIKE`, `IN`, `IS NULL`, and `IS NOT NULL`.
  - Column names stay visible and pinned when filters are applied.
  - Active filter badge with quick one-click clear.
- Quick row insertion, in-place editing, and deletion.

### 📝 SQL Query Editor & Session Persistence
- Tabbed multi-query workspace with query history and snippet organization.
- **Session & Draft Autosave**: Open tabs, active selections, and unsaved query drafts are automatically persisted to the local SQLite storage with 300ms debouncing, guaranteeing instant recovery on relaunch.
- **Quick Session Restore**: One-click `⚡ Restore Last Session` button on the connection launchpad.
- Execute entire scripts or highlight specific SQL statements (`Ctrl+Enter` / `F5`).
- Query execution duration and affected row count metrics.
- Export query results directly to CSV, JSON, or SQL INSERT scripts.

### 📦 Export & Backup
- **Table Data Export**: Export filtered or full table data to **CSV**, **JSON**, or formatted **SQL** `INSERT` statements with transaction wrapping (`BEGIN / COMMIT`).
- **Full Database Dump & Backup Wizard**:
  - Choose dump scope: Structure & Data, Structure Only (DDL), or Data Only.
  - Table selection checklist.
  - Options for `DROP TABLE IF EXISTS`, foreign key constraints, indexes, and views.
  - Database restore runner.

### 📥 Data Import Wizard
- Bulk import data from **CSV** or **JSON** files into any existing database table.
- Automatic delimiter detection and JSON structure analysis (supports JSON array of objects or wrapped records).
- Live preview data grid before executing the import.
- Batched transactional inserts for reliable, high-speed loading.

### 🛡️ Local Security Vault
- Optional master password protection using **AES-GCM-256** and **PBKDF2** (100,000 iterations).
- Encrypted storage for sensitive database credentials.
- All connections and data remain 100% local on your machine.

---

## 🏗️ Architecture

```
HornetStudio/
├── src/
│   ├── HornetStudio.Core/           # Core domain abstractions, database clients, services
│   │   ├── Database/                # SQLite, Postgres, MySQL, SqlServer implementations
│   │   ├── Domain/                  # Entities (ConnectionConfig, QueryResult, TableSchema)
│   │   ├── Services/                # Export, Import, Backup/Restore services
│   │   └── Storage/                 # EF Core SQLite storage & MasterPasswordVault (AES-GCM)
│   └── HornetStudio.Desktop/        # Avalonia UI application
│       ├── Assets/                  # High-res Green Hornet vector icons and logos
│       ├── Controls/                # Custom DataGrid and UI controls
│       ├── ViewModels/              # CommunityToolkit MVVM ViewModels
│       └── Views/                   # Avalonia XAML views
├── tests/
│   └── HornetStudio.Tests/          # Unit & integration tests (xUnit, FluentAssertions)
├── samples/                         # Sample datasets (.csv, .json) for testing import
├── .github/workflows/ci.yml         # GitHub Actions CI workflow
├── HornetStudio.slnx                # Modern solution file (.NET 9+)
└── Directory.Build.props            # Central project properties
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 11 SDK](https://dotnet.microsoft.com/download) (or latest .NET SDK supporting net11.0 preview)
- Git

### Build & Run
```bash
# Clone the repository
git clone https://github.com/pawelworyna/hornet-studio.git
cd hornet-studio

# Restore and build the solution
dotnet build

# Run the desktop application
dotnet run --project src/HornetStudio.Desktop/HornetStudio.Desktop.csproj
```

### Running Tests
```bash
dotnet test
```

### Packaging & Cross-Platform Installers

HornetStudio provides dedicated native installers and portable archives across Windows, macOS, and Linux:

#### 🪟 Windows (x64 & ARM64)
Supports dual-mode installation (Program Files with admin elevation, or AppData per-user without admin rights):
```powershell
# Build self-contained installers (.exe) and portable archives (.zip)
.\scripts\build-installer.ps1 -Version 1.0.0 -Architecture all
```
Generated in `artifacts/installer/`:
- `HornetStudio-Setup-x64-v1.0.0.exe` & `HornetStudio-Setup-arm64-v1.0.0.exe` (Inno Setup wizard)
- `HornetStudio-Portable-win-x64-v1.0.0.zip` & `HornetStudio-Portable-win-arm64-v1.0.0.zip`

#### 🍎 macOS Apple Silicon (`osx-arm64`)
```bash
# Build .app bundle and native mountable disk image (.dmg)
chmod +x scripts/build-dmg.sh
./scripts/build-dmg.sh 1.0.0 osx-arm64
```
Generated in `artifacts/installer/`:
- `HornetStudio-v1.0.0-osx-arm64.dmg` (Drag-and-drop to `/Applications`)
- `HornetStudio-Portable-osx-arm64-v1.0.0.zip`

#### 🐧 Linux (`linux-x64` & `linux-arm64`)
```bash
# Build Debian package (.deb) and portable tarball (.tar.gz)
chmod +x scripts/build-deb.sh
./scripts/build-deb.sh 1.0.0 linux-x64
./scripts/build-deb.sh 1.0.0 linux-arm64
```
Generated in `artifacts/installer/`:
- `hornetstudio_1.0.0_amd64.deb` & `hornetstudio_1.0.0_arm64.deb` (`sudo dpkg -i ...`)
- `HornetStudio-linux-x64-v1.0.0.tar.gz` & `HornetStudio-linux-arm64-v1.0.0.tar.gz`

---

## 📄 License

This project is open-source software licensed under the [MIT License](LICENSE).
