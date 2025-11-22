-- =====================================================
-- Complete Database Setup for OfflineAI
-- =====================================================
-- This script creates the complete database schema including:
--   - LLMs table (stores LLM model information)
--   - Questions table (stores Q&A with LLM references)
--   - MemoryFragments table (stores vector embeddings)
--   - KnowledgeDomains table (stores domain information)
--   - DomainVariants table (stores domain name variants)
-- =====================================================

-- =====================================================
-- Step 1: Create Database (if needed)
-- =====================================================
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'VectorMemoryDB')
BEGIN
    PRINT 'Creating database: VectorMemoryDB';
    CREATE DATABASE VectorMemoryDB;
    PRINT '? Database created';
END
ELSE
BEGIN
    PRINT 'Database already exists: VectorMemoryDB';
END
GO

USE VectorMemoryDB;
GO

PRINT '';
PRINT '========================================';
PRINT 'Setting up OfflineAI Database Schema';
PRINT '========================================';
PRINT '';

-- =====================================================
-- Step 2: Create LLMs Table
-- =====================================================
PRINT '[1/5] LLMs Table';
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'LLMs')
BEGIN
    CREATE TABLE [LLMs] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        LlmName NVARCHAR(500) NOT NULL UNIQUE,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX IX_LLMs_LlmName ON [LLMs](LlmName);
    CREATE INDEX IX_LLMs_CreatedAt ON [LLMs](CreatedAt);
    
    PRINT '  ? Created';
END
ELSE
BEGIN
    PRINT '  ? Already exists';
END
GO

-- =====================================================
-- Step 3: Create Questions Table
-- =====================================================
PRINT '[2/5] Questions Table';
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Questions')
BEGIN
    CREATE TABLE [Questions] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Question NVARCHAR(MAX) NOT NULL,
        Answer NVARCHAR(MAX) NOT NULL,
        LlmId UNIQUEIDENTIFIER NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_Questions_LLMs FOREIGN KEY (LlmId) 
            REFERENCES [LLMs](Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Questions_LlmId ON [Questions](LlmId);
    CREATE INDEX IX_Questions_CreatedAt ON [Questions](CreatedAt);
    
    PRINT '  ? Created';
END
ELSE
BEGIN
    PRINT '  ? Already exists';
END
GO

-- =====================================================
-- Step 4: Create MemoryFragments Table
-- =====================================================
PRINT '[3/5] MemoryFragments Table';
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MemoryFragments')
BEGIN
    CREATE TABLE [MemoryFragments] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        CollectionName NVARCHAR(255) NOT NULL,
        Category NVARCHAR(500) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        ContentLength INT NOT NULL DEFAULT 0,
        Embedding VARBINARY(MAX) NULL,
        EmbeddingDimension INT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        SourceFile NVARCHAR(1000) NULL,
        ChunkIndex INT NULL
    );

    CREATE INDEX IX_MemoryFragments_CollectionName ON [MemoryFragments](CollectionName);
    CREATE INDEX IX_MemoryFragments_Category ON [MemoryFragments](Category);
    CREATE INDEX IX_MemoryFragments_CreatedAt ON [MemoryFragments](CreatedAt);
    CREATE INDEX IX_MemoryFragments_ContentLength ON [MemoryFragments](ContentLength);
    
    PRINT '  ? Created';
END
ELSE
BEGIN
    PRINT '  ? Already exists';
END
GO

-- =====================================================
-- Step 5: Create KnowledgeDomains Table
-- =====================================================
PRINT '[4/5] KnowledgeDomains Table';
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'KnowledgeDomains')
BEGIN
    CREATE TABLE [KnowledgeDomains] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        DomainId NVARCHAR(255) NOT NULL UNIQUE,
        DisplayName NVARCHAR(500) NOT NULL,
        Category NVARCHAR(100) NOT NULL DEFAULT 'general',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        Source NVARCHAR(100) NOT NULL DEFAULT 'manual'
    );

    CREATE INDEX IX_KnowledgeDomains_DomainId ON [KnowledgeDomains](DomainId);
    CREATE INDEX IX_KnowledgeDomains_Category ON [KnowledgeDomains](Category);
    CREATE INDEX IX_KnowledgeDomains_CreatedAt ON [KnowledgeDomains](CreatedAt);
    
    PRINT '  ? Created';
END
ELSE
BEGIN
    PRINT '  ? Already exists';
END
GO

-- =====================================================
-- Step 6: Create DomainVariants Table
-- =====================================================
PRINT '[5/5] DomainVariants Table';
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DomainVariants')
BEGIN
    CREATE TABLE [DomainVariants] (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        DomainId UNIQUEIDENTIFIER NOT NULL,
        VariantText NVARCHAR(500) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT FK_DomainVariants_KnowledgeDomains FOREIGN KEY (DomainId) 
            REFERENCES [KnowledgeDomains](Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_DomainVariants_DomainId ON [DomainVariants](DomainId);
    CREATE INDEX IX_DomainVariants_VariantText ON [DomainVariants](VariantText);
    
    PRINT '  ? Created';
END
ELSE
BEGIN
    PRINT '  ? Already exists';
END
GO

-- =====================================================
-- Display Complete Schema Summary
-- =====================================================
PRINT '';
PRINT '========================================';
PRINT 'Database Schema Summary';
PRINT '========================================';
PRINT '';

-- Count all tables
SELECT 
    t.name AS TableName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = t.object_id) AS ColumnCount,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id AND name IS NOT NULL) AS IndexCount,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = t.object_id) AS ForeignKeyCount,
    p.rows AS RecordCount
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
WHERE t.name IN ('LLMs', 'Questions', 'MemoryFragments', 'KnowledgeDomains', 'DomainVariants')
ORDER BY t.name;

PRINT '';
PRINT '========================================';
PRINT 'Setup Complete!';
PRINT '========================================';
PRINT '';
PRINT 'All tables are ready for use.';
PRINT 'You can now run the OfflineAI application.';
PRINT '';
GO
