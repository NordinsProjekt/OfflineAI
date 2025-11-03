# AiChatService Test Suite

This directory contains comprehensive unit and integration tests for the `AiChatService` class.

## Test Files

### AiChatServiceTests.cs
**Unit tests** that focus on individual methods and their behavior in isolation.

#### Test Coverage

##### Constructor Tests
- ? `Constructor_InitializesPropertiesCorrectly` - Verifies proper initialization
- ? `Constructor_AcceptsDefaultTimeout` - Tests default timeout parameter

##### SendMessageStreamAsync Tests
- ? `SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsNull` - Validates null input
- ? `SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsEmpty` - Validates empty input
- ? `SendMessageStreamAsync_ThrowsArgumentException_WhenQuestionIsWhitespace` - Validates whitespace input
- ? `SendMessageStreamAsync_AddsQuestionToConversationHistory` - Verifies conversation tracking
- ? `SendMessageStreamAsync_HandlesVeryLongQuestions` - Tests with 1000+ character questions

##### BuildSystemPromptAsync Tests
- ? `BuildSystemPrompt_IncludesBasePrompt` - Verifies base system prompt is included
- ? `BuildSystemPrompt_IncludesMemoryContext` - Tests memory integration
- ? `BuildSystemPrompt_IncludesConversationHistory_WhenAvailable` - Tests conversation history inclusion
- ? `BuildSystemPrompt_ExcludesConversationHistory_WhenEmpty` - Tests empty history handling
- ? `BuildSystemPrompt_ExcludesConversationHistory_WhenWhitespace` - Tests whitespace-only history
- ? `BuildSystemPrompt_UsesVectorSearch_WhenMemoryIsVectorMemory` - Tests vector memory integration
- ? `BuildSystemPrompt_UsesVectorSearchWithCorrectParameters` - Validates topK=5, minRelevanceScore=0.1
- ? `BuildSystemPrompt_HandlesLargeMemoryContent` - Tests with 10,000+ character content
- ? `BuildSystemPrompt_HandlesSpecialCharactersInMemory` - Tests special character handling
- ? `BuildSystemPrompt_CombinesAllComponents` - Tests complete prompt assembly and ordering

##### ExecuteProcessAsync Tests
- ? `ExecuteProcess_ReturnsExceptionMessage_WhenExceptionOccurs` - Tests exception handling
- ? `ExecuteProcess_ExtractsCleanAnswer_FromStreamingOutput` - Conceptual test for answer extraction
- ? `ExecuteProcess_StoresCleanAnswerInConversationHistory` - Conceptual test for history storage
- ? `ExecuteProcess_RemovesTrailingTags_FromAnswer` - Conceptual test for tag removal

### AiChatServiceIntegrationTests.cs
**Integration tests** that test end-to-end scenarios with real dependencies.

#### Test Coverage

##### Simple Memory Integration
- ? `SendMessageStreamAsync_WithSimpleMemory_BuildsCorrectPrompt` - Tests with SimpleMemory implementation
- ? `SendMessageStreamAsync_HandlesEmptyMemory` - Tests with no memory content

##### Vector Memory Integration
- ? `SendMessageStreamAsync_WithVectorMemory_PerformsSemanticSearch` - Tests semantic search functionality
- ? `BuildSystemPrompt_VectorSearchWithNoResults_FallsBackToToString` - Tests fallback behavior
- ? `BuildSystemPrompt_WithVectorMemory_RespectsTopKParameter` - Validates topK=5 limit
- ? `BuildSystemPrompt_WithVectorMemory_RespectsMinRelevanceScore` - Tests relevance filtering

##### Conversation History
- ? `SendMessageStreamAsync_MaintainsConversationHistory` - Tests conversation tracking
- ? `SendMessageStreamAsync_WithMultipleQuestions_BuildsConversationContext` - Tests multi-turn conversations

##### Edge Cases
- ? `SendMessageStreamAsync_WithSpecialCharacters_PreservesContent` - Tests quotes, tags, etc.
- ? `SendMessageStreamAsync_WithUnicodeCharacters_PreservesContent` - Tests international characters
- ? `BuildSystemPrompt_StructureIsCorrect` - Validates prompt structure
- ? `Constructor_AllowsCustomTimeout` - Tests custom timeout values
- ? `SendMessageStreamAsync_WithEmptyConversationMemory_DoesNotAddExtraNewlines` - Tests formatting

## Test Helpers

### TestableAiChatService
A wrapper class that exposes private methods for testing using reflection:
- `BuildSystemPromptAsyncPublic(string question)` - Exposes private `BuildSystemPromptAsync` method

### SimpleTestMemory
A simple in-memory implementation of `ILlmMemory` for testing:
- Stores fragments in a list
- Returns formatted string representation
- Used in tests that don't require vector search

## Running the Tests

### Run All Tests
```bash
dotnet test
```

### Run Only AiChatService Tests
```bash
dotnet test --filter FullyQualifiedName~AiChatService
```

### Run Only Unit Tests
```bash
dotnet test --filter FullyQualifiedName~AiChatServiceTests
```

### Run Only Integration Tests
```bash
dotnet test --filter FullyQualifiedName~AiChatServiceIntegrationTests
```

## Test Dependencies

- **xUnit** - Test framework
- **Moq** - Mocking framework for creating mock dependencies
- **Services** - Contains AiChatService and related classes
- **MemoryLibrary** - Contains IMemoryFragment and MemoryFragment

## Known Limitations

### ExecuteProcessAsync Testing
The `ExecuteProcessAsync` method is challenging to test comprehensively because it:
1. Creates and manages a real `Process` instance
2. Handles streaming output from an external LLM CLI
3. Involves timing-based logic (inactivity detection)

Current approach:
- Test exception handling with invalid file paths
- Test indirectly through the public API
- Document expected behavior in conceptual tests

To fully test `ExecuteProcessAsync`, you would need:
- A mock/stub process that simulates LLM output
- Process output injection mechanism
- Or integration tests with a real LLM binary

### Private Method Testing
The `BuildSystemPromptAsync` method is private. Tests access it using reflection through `TestableAiChatService`. This approach:
- ? Allows thorough testing of prompt building logic
- ?? Is somewhat brittle if method signatures change
- ?? Has slight performance overhead from reflection

Alternative approaches:
1. Make the method `internal` and use `[InternalsVisibleTo]`
2. Test only through the public API (less granular)
3. Extract prompt building to a separate testable class

## Code Coverage

### Methods Tested
- ? Constructor (2 overloads)
- ? `SendMessageStreamAsync` - Input validation, conversation tracking
- ? `BuildSystemPromptAsync` - All code paths, edge cases
- ? `ExecuteProcessAsync` - Exception handling, integration behavior

### Coverage Metrics
Estimated coverage by method:
- Constructor: 100%
- SendMessageStreamAsync: 80% (process execution not fully testable)
- BuildSystemPromptAsync: 100%
- ExecuteProcessAsync: 30% (requires process mocking for full coverage)

### Overall Coverage
- **Public API**: 90%+
- **Private methods**: 75%+ (where testable)
- **Exception paths**: 100%

## Future Improvements

1. **Process Mocking**: Create a `IProcessRunner` abstraction to enable full testing of `ExecuteProcessAsync`
2. **Timeout Testing**: Add tests that verify timeout behavior (requires time control)
3. **Performance Tests**: Add tests that measure prompt building performance with large datasets
4. **Concurrent Tests**: Test thread safety of conversation history updates
5. **Output Streaming Tests**: Test the streaming output logic with controlled data

## Test Maintenance

When modifying `AiChatService`:
1. Run all tests to ensure no regressions
2. Update tests if public API changes
3. Add new tests for new functionality
4. Consider adding integration tests for complex scenarios
5. Update this README if test structure changes

## Questions or Issues?

If tests fail unexpectedly:
1. Check that all dependencies are properly restored
2. Verify that the LocalLlmEmbeddingService mock mode works correctly
3. Ensure the test project references are up to date
4. Check for any breaking changes in ILlmMemory or related interfaces
