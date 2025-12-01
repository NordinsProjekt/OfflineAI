-- ========================================================================
-- Migration Script: Add Weighted Embedding Columns
-- ========================================================================
-- Purpose: Adds CategoryEmbedding and ContentEmbedding columns to existing 
--          MemoryFragments table to support weighted similarity matching
--
-- Background:
--   The weighted embedding strategy uses three embeddings per fragment:
--   1. CategoryEmbedding (40% weight) - Domain/topic matching
--   2. ContentEmbedding (30% weight) - Detail matching  
--   3. Embedding (30% weight) - Combined category+content (legacy)
--
-- Run this script if you have an existing MemoryFragments table that was
-- created before the weighted embedding feature was added.
-- ========================================================================

USE [OfflineAI]; -- Change to your database name if different
GO

-- ========================================================================
-- DEBUG MODE CONFIGURATION
-- Set to 1 to enable verbose debug output, 0 for standard output only
-- ========================================================================
:setvar DebugMode 0
-- To enable debug output, change the line above to:
-- :setvar DebugMode 1
GO

-- Check if CategoryEmbedding column exists
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID('MemoryFragments') 
    AND name = 'CategoryEmbedding'
)
BEGIN
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] CategoryEmbedding column not found - adding column...';
    
    PRINT '[1/2] Adding CategoryEmbedding column...';
    ALTER TABLE [MemoryFragments] ADD CategoryEmbedding VARBINARY(MAX) NULL;
    PRINT '      ? CategoryEmbedding column added successfully';
    
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] Column definition: CategoryEmbedding VARBINARY(MAX) NULL';
END
ELSE
BEGIN
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] CategoryEmbedding column already exists - skipping';
    
    PRINT '[1/2] CategoryEmbedding column already exists - skipping';
END
GO

-- Check if ContentEmbedding column exists
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID('MemoryFragments') 
    AND name = 'ContentEmbedding'
)
BEGIN
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] ContentEmbedding column not found - adding column...';
    
    PRINT '[2/2] Adding ContentEmbedding column...';
    ALTER TABLE [MemoryFragments] ADD ContentEmbedding VARBINARY(MAX) NULL;
    PRINT '      ? ContentEmbedding column added successfully';
    
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] Column definition: ContentEmbedding VARBINARY(MAX) NULL';
END
ELSE
BEGIN
    IF '$(DebugMode)' = '1'
        PRINT '[DEBUG] ContentEmbedding column already exists - skipping';
    
    PRINT '[2/2] ContentEmbedding column already exists - skipping';
END
GO

-- Verify columns were added
IF '$(DebugMode)' = '1'
BEGIN
    PRINT '';
    PRINT '[DEBUG] ============================================================';
    PRINT '[DEBUG] Running verification query';
    PRINT '[DEBUG] Querying sys.columns for: Embedding, CategoryEmbedding, ContentEmbedding';
    PRINT '[DEBUG] ============================================================';
END

PRINT '';
PRINT 'Verification:';
PRINT '-------------';

SELECT 
    c.name as ColumnName,
    t.name as DataType,
    c.max_length as MaxLength,
    CASE WHEN c.is_nullable = 1 THEN 'YES' ELSE 'NO' END as IsNullable
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID('MemoryFragments')
AND c.name IN ('Embedding', 'CategoryEmbedding', 'ContentEmbedding')
ORDER BY c.name;

IF '$(DebugMode)' = '1'
BEGIN
    PRINT '';
    PRINT '[DEBUG] Verification complete - Expected 3 rows';
    PRINT '[DEBUG] If you see fewer rows, the columns may not exist in the table';
END

PRINT '';
PRINT '========================================================================';
PRINT ' Migration Complete!';
PRINT '========================================================================';
PRINT ' ';
PRINT ' Next Steps:';
PRINT '   1. Existing fragments will have NULL for new embedding columns';
PRINT '   2. To populate them, regenerate embeddings using:';
PRINT '      - Delete existing collection with: /regenerate command';
PRINT '      - Move files from archive back to inbox';
PRINT '      - Restart application to re-process files';
PRINT ' ';
PRINT ' Note: The weighted similarity will gracefully fall back to the';
PRINT '       combined embedding if category/content embeddings are NULL.';
PRINT '========================================================================';

IF '$(DebugMode)' = '1'
BEGIN
    PRINT '';
    PRINT '[DEBUG] ============================================================';
    PRINT '[DEBUG] Migration script execution complete';
    PRINT '[DEBUG] Session details:';
    PRINT '[DEBUG]   - Database: ' + DB_NAME();
    PRINT '[DEBUG]   - User: ' + SUSER_NAME();
    PRINT '[DEBUG]   - Execution time: ' + CONVERT(VARCHAR, GETDATE(), 120);
    PRINT '[DEBUG] ============================================================';
    PRINT '';
    PRINT '[DEBUG] To disable debug output, set: :setvar DebugMode 0';
END
GO
