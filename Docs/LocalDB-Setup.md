# SQL Server LocalDB Setup Guide

## What is LocalDB?

SQL Server LocalDB is a lightweight version of SQL Server designed for development. It's:
- ? **Installed with Visual Studio** (automatically)
- ? **Runs on-demand** (no service to manage)
- ? **Easy to use** (no configuration needed)
- ? **Perfect for development** (full SQL Server features)

## Your LocalDB Instance

Your SQL Server LocalDB instance name is: `(localdb)\mssqllocaldb`

## Connection String (Already Updated)

```csharp
Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;
```

## Quick Start

### Option 1: Let the App Create the Database (Easiest!)

Just run your app with option 3, and it will **auto-create** the database:

```
Select mode (1, 2, or 3): 3
```

The app will:
1. Connect to LocalDB
2. Create the database `VectorMemoryDB` (if it doesn't exist)
3. Create the `MemoryFragments` table with indexes
4. You're ready to go! ?

### Option 2: Create Database Manually (Optional)

If you want to create it manually first:

1. **Open SQL Server Object Explorer in Visual Studio:**
   - View ? SQL Server Object Explorer (Ctrl+\, Ctrl+S)

2. **Expand:**
   - SQL Server
   - (localdb)\mssqllocaldb
   - Databases

3. **Create Database:**
   - Right-click on "Databases" ? Add New Database
   - Name: `VectorMemoryDB`
   - Click OK

4. **Verify:**
   - Expand "Databases" and you should see `VectorMemoryDB`

### Option 3: Use Command Line (Alternative)

```cmd
# Connect to LocalDB
sqllocaldb start mssqllocaldb

# Create database using sqlcmd
sqlcmd -S "(localdb)\mssqllocaldb" -Q "CREATE DATABASE VectorMemoryDB"

# Verify
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases WHERE name = 'VectorMemoryDB'"
```

## Verify LocalDB is Running

```cmd
# Check LocalDB instances
sqllocaldb info

# Start LocalDB if needed
sqllocaldb start mssqllocaldb

# Get instance info
sqllocaldb info mssqllocaldb
```

## Using SQL Server Management Studio (SSMS)

If you have SSMS installed:

1. **Connect:**
   - Server name: `(localdb)\mssqllocaldb`
   - Authentication: Windows Authentication
   - Click Connect

2. **Create Database:**
   - Right-click Databases ? New Database
   - Name: `VectorMemoryDB`
   - Click OK

## Troubleshooting

### "LocalDB is not installed"

LocalDB comes with Visual Studio. If it's missing:

**Option 1: Repair Visual Studio**
- Open Visual Studio Installer
- Click "Modify"
- Ensure "SQL Server Express LocalDB" is checked under Individual Components

**Option 2: Install SQL Server Express**
- Download: https://www.microsoft.com/sql-server/sql-server-downloads
- Choose "Express" edition
- Install with default settings

### "Cannot connect to LocalDB"

1. **Start LocalDB:**
```cmd
sqllocaldb start mssqllocaldb
```

2. **Check it's running:**
```cmd
sqllocaldb info mssqllocaldb
```

You should see:
```
Name:               mssqllocaldb
Version:            ...
Shared name:
Owner:              ...
Auto-create:        Yes
State:              Running  ? Should say "Running"
```

3. **If stopped, start it:**
```cmd
sqllocaldb start mssqllocaldb
```

### "Database does not exist"

Don't worry! The app will create it automatically when you run it. Just make sure:
- ? `AutoInitializeDatabase = true` (already set)
- ? LocalDB is running
- ? Run the app and select option 3

### Connection String Issues

**Your updated connection string:**
```csharp
Server=(localdb)\mssqllocaldb;Database=VectorMemoryDB;Integrated Security=true;TrustServerCertificate=true;
```

**Common variations (if needed):**
```csharp
// Default instance (most common)
Server=(localdb)\mssqllocaldb;...

// Older Visual Studio versions
Server=(localdb)\v11.0;...

// Named instance
Server=(localdb)\MyInstance;...

// With explicit database file path
Server=(localdb)\mssqllocaldb;AttachDbFilename=C:\MyFolder\VectorMemoryDB.mdf;...
```

## Test Your Connection

### From Visual Studio

1. **Server Explorer:**
   - View ? Server Explorer
   - Right-click "Data Connections" ? Add Connection
   - Server name: `(localdb)\mssqllocaldb`
   - Database: `VectorMemoryDB`
   - Test Connection ? Should succeed!

2. **SQL Server Object Explorer:**
   - View ? SQL Server Object Explorer
   - Expand SQL Server ? (localdb)\mssqllocaldb ? Databases
   - You should see `VectorMemoryDB` (after first run)

### From Command Line

```cmd
# Test connection
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT @@VERSION"

# List databases
sqlcmd -S "(localdb)\mssqllocaldb" -Q "SELECT name FROM sys.databases"

# Query your table (after app runs)
sqlcmd -S "(localdb)\mssqllocaldb" -d VectorMemoryDB -Q "SELECT COUNT(*) FROM MemoryFragments"
```

## Database Location

LocalDB databases are stored in:
```
C:\Users\<YourUsername>\AppData\Local\Microsoft\Microsoft SQL Server Local DB\Instances\mssqllocaldb\
```

Or for user databases:
```
C:\Users\<YourUsername>\
```

## Running Your App

Now that the connection string is fixed:

```
Select mode (1, 2, or 3): 3

Initializing embedding service...
Initializing database schema...
? Database schema initialized (or already exists)

Existing collections in database: 0

Options:
1. Load from database (if exists)
2. Load from files and save to database
3. Use in-memory only (no database)

Select option (1-3): 2  ? First time, load from files
```

The app will:
1. ? Connect to LocalDB
2. ? Create database if needed
3. ? Create tables if needed
4. ? Load your files
5. ? Generate embeddings
6. ? Save to database
7. ? Ready for fast loading next time!

## Next Steps

1. ? **Connection string fixed** - Now uses `(localdb)\mssqllocaldb`
2. ? **Auto-initialization enabled** - Database created automatically
3. ? **Build successful** - Ready to run!

Just run your app and select option 3! The rest happens automatically. ??

## LocalDB Commands Reference

```cmd
# List all LocalDB instances
sqllocaldb info

# Create a new instance
sqllocaldb create MyInstance

# Start an instance
sqllocaldb start mssqllocaldb

# Stop an instance
sqllocaldb stop mssqllocaldb

# Delete an instance
sqllocaldb delete MyInstance

# Get detailed info
sqllocaldb info mssqllocaldb

# Trace LocalDB activity (for debugging)
sqllocaldb trace on
```

## Visual Studio SQL Server Object Explorer Quick Tips

**View Database:**
1. View ? SQL Server Object Explorer (Ctrl+\, Ctrl+S)
2. Expand: SQL Server ? (localdb)\mssqllocaldb ? Databases ? VectorMemoryDB

**View Table Data:**
1. Expand: Tables ? dbo.MemoryFragments
2. Right-click ? View Data

**Run Query:**
1. Right-click on VectorMemoryDB ? New Query
2. Type your SQL:
```sql
SELECT COUNT(*) as TotalFragments FROM MemoryFragments;
SELECT TOP 10 * FROM MemoryFragments ORDER BY CreatedAt DESC;
```
3. Click Execute (or Ctrl+Shift+E)

**View Statistics:**
```sql
-- View all collections
SELECT 
    CollectionName,
    COUNT(*) as FragmentCount,
    SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) as WithEmbeddings
FROM MemoryFragments
GROUP BY CollectionName;

-- Check database size
EXEC sp_spaceused;

-- Most recent fragments
SELECT TOP 5 CollectionName, Category, CreatedAt
FROM MemoryFragments
ORDER BY CreatedAt DESC;
```

That's it! You're all set to use SQL Server LocalDB with your vector memory system. ??
