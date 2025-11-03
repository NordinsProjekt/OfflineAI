-- Vector Memory Database Schema
-- SQL Server / Azure SQL Database
-- Creates the MemoryFragments table for storing vector embeddings

USE VectorMemoryDB;  -- Change to your database name
GO

-- Drop existing table if needed (CAUTION: This will delete all data!)
-- DROP TABLE IF EXISTS MemoryFragments;
-- GO

-- Create the main table for storing memory fragments with embeddings
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MemoryFragments')
BEGIN
    CREATE TABLE MemoryFragments (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        CollectionName NVARCHAR(255) NOT NULL,
        Category NVARCHAR(500) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        
        -- Vector embedding stored as binary
        -- For a 384-dimension embedding: 384 floats × 4 bytes = 1,536 bytes
        -- For a 768-dimension embedding: 768 floats × 4 bytes = 3,072 bytes
        -- For a 1536-dimension embedding: 1536 floats × 4 bytes = 6,144 bytes
        Embedding VARBINARY(MAX) NULL,
        EmbeddingDimension INT NULL,
        
        -- Metadata
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        SourceFile NVARCHAR(1000) NULL,
        ChunkIndex INT NULL
    );

    PRINT 'Table MemoryFragments created successfully.';
END
ELSE
BEGIN
    PRINT 'Table MemoryFragments already exists.';
END
GO

-- Create indexes for performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MemoryFragments_CollectionName')
BEGIN
    CREATE INDEX IX_MemoryFragments_CollectionName ON MemoryFragments(CollectionName);
    PRINT 'Index IX_MemoryFragments_CollectionName created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MemoryFragments_Category')
BEGIN
    CREATE INDEX IX_MemoryFragments_Category ON MemoryFragments(Category);
    PRINT 'Index IX_MemoryFragments_Category created.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MemoryFragments_CreatedAt')
BEGIN
    CREATE INDEX IX_MemoryFragments_CreatedAt ON MemoryFragments(CreatedAt);
    PRINT 'Index IX_MemoryFragments_CreatedAt created.';
END
GO

-- Optional: Create a composite index for common queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MemoryFragments_Collection_Chunk')
BEGIN
    CREATE INDEX IX_MemoryFragments_Collection_Chunk 
    ON MemoryFragments(CollectionName, ChunkIndex, CreatedAt);
    PRINT 'Index IX_MemoryFragments_Collection_Chunk created.';
END
GO

-- View to check collection statistics
IF NOT EXISTS (SELECT * FROM sys.views WHERE name = 'VW_CollectionStats')
BEGIN
    EXEC('
    CREATE VIEW VW_CollectionStats AS
    SELECT 
        CollectionName,
        COUNT(*) AS FragmentCount,
        SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) AS EmbeddingsCount,
        AVG(EmbeddingDimension) AS AvgEmbeddingDimension,
        MIN(CreatedAt) AS FirstCreated,
        MAX(UpdatedAt) AS LastUpdated,
        COUNT(DISTINCT SourceFile) AS SourceFileCount
    FROM MemoryFragments
    GROUP BY CollectionName
    ');
    PRINT 'View VW_CollectionStats created.';
END
GO

-- Stored procedure to get collection statistics
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetCollectionStats')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_GetCollectionStats
        @CollectionName NVARCHAR(255)
    AS
    BEGIN
        SET NOCOUNT ON;
        
        SELECT 
            CollectionName,
            COUNT(*) AS FragmentCount,
            SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) AS EmbeddingsCount,
            AVG(EmbeddingDimension) AS AvgEmbeddingDimension,
            MIN(CreatedAt) AS FirstCreated,
            MAX(UpdatedAt) AS LastUpdated,
            COUNT(DISTINCT SourceFile) AS SourceFileCount,
            SUM(DATALENGTH(Embedding)) / 1024.0 / 1024.0 AS TotalEmbeddingSizeMB
        FROM MemoryFragments
        WHERE CollectionName = @CollectionName
        GROUP BY CollectionName;
    END
    ');
    PRINT 'Stored procedure sp_GetCollectionStats created.';
END
GO

-- Stored procedure to clean up old collections
IF NOT EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_DeleteOldCollections')
BEGIN
    EXEC('
    CREATE PROCEDURE sp_DeleteOldCollections
        @DaysOld INT = 30
    AS
    BEGIN
        SET NOCOUNT ON;
        
        DECLARE @DeletedCount INT;
        
        DELETE FROM MemoryFragments
        WHERE UpdatedAt < DATEADD(DAY, -@DaysOld, GETUTCDATE());
        
        SET @DeletedCount = @@ROWCOUNT;
        
        SELECT @DeletedCount AS DeletedFragments;
    END
    ');
    PRINT 'Stored procedure sp_DeleteOldCollections created.';
END
GO

-- Sample queries

-- 1. View all collections
-- SELECT * FROM VW_CollectionStats ORDER BY CollectionName;

-- 2. Get specific collection stats
-- EXEC sp_GetCollectionStats @CollectionName = 'game-rules';

-- 3. Find fragments by keyword
-- SELECT Id, CollectionName, Category, LEFT(Content, 100) AS ContentPreview
-- FROM MemoryFragments
-- WHERE Content LIKE '%victory points%'
-- ORDER BY CreatedAt DESC;

-- 4. Check embedding coverage
-- SELECT 
--     CollectionName,
--     COUNT(*) AS TotalFragments,
--     SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) AS WithEmbeddings,
--     CAST(100.0 * SUM(CASE WHEN Embedding IS NOT NULL THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,2)) AS CoveragePercent
-- FROM MemoryFragments
-- GROUP BY CollectionName;

-- 5. Database size analysis
-- SELECT 
--     CollectionName,
--     COUNT(*) AS Fragments,
--     SUM(DATALENGTH(Content)) / 1024.0 / 1024.0 AS ContentSizeMB,
--     SUM(DATALENGTH(Embedding)) / 1024.0 / 1024.0 AS EmbeddingSizeMB,
--     (SUM(DATALENGTH(Content)) + SUM(DATALENGTH(Embedding))) / 1024.0 / 1024.0 AS TotalSizeMB
-- FROM MemoryFragments
-- GROUP BY CollectionName;

PRINT 'Database schema setup complete!';
GO
