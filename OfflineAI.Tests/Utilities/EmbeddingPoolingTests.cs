using Services.Utilities;
using Xunit;

namespace OfflineAI.Tests.Utilities;

public class EmbeddingPoolingTests
{
    [Fact]
    public void ApplyMeanPooling_WithAllRealTokens_AveragesAllValues()
    {
        // Arrange
        var outputTensor = new float[] 
        { 
            1.0f, 2.0f,  // Token 1: [1.0, 2.0]
            3.0f, 4.0f   // Token 2: [3.0, 4.0]
        };
        var attentionMask = new long[] { 1, 1 }; // Both tokens are real
        int embeddingDim = 2;
        
        // Act
        var result = EmbeddingPooling.ApplyMeanPooling(outputTensor, attentionMask, embeddingDim);
        
        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(2.0f, result[0]); // (1.0 + 3.0) / 2
        Assert.Equal(3.0f, result[1]); // (2.0 + 4.0) / 2
    }
    
    [Fact]
    public void ApplyMeanPooling_WithPaddingTokens_IgnoresPadding()
    {
        // Arrange
        var outputTensor = new float[] 
        { 
            1.0f, 2.0f,  // Token 1 (real): [1.0, 2.0]
            3.0f, 4.0f,  // Token 2 (real): [3.0, 4.0]
            0.0f, 0.0f   // Token 3 (padding): [0.0, 0.0]
        };
        var attentionMask = new long[] { 1, 1, 0 }; // First 2 real, last is padding
        int embeddingDim = 2;
        
        // Act
        var result = EmbeddingPooling.ApplyMeanPooling(outputTensor, attentionMask, embeddingDim);
        
        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(2.0f, result[0]); // (1.0 + 3.0) / 2, ignoring 0.0
        Assert.Equal(3.0f, result[1]); // (2.0 + 4.0) / 2, ignoring 0.0
    }
    
    [Fact]
    public void NormalizeToUnitLength_NormalizesVector()
    {
        // Arrange
        var embedding = new float[] { 3.0f, 4.0f }; // Magnitude = 5.0
        
        // Act
        var result = EmbeddingPooling.NormalizeToUnitLength(embedding);
        
        // Assert
        Assert.Equal(0.6f, result[0], precision: 5); // 3.0 / 5.0
        Assert.Equal(0.8f, result[1], precision: 5); // 4.0 / 5.0
        
        // Verify magnitude is 1.0
        var magnitude = EmbeddingPooling.CalculateMagnitude(result);
        Assert.Equal(1.0f, magnitude, precision: 5);
    }
    
    [Fact]
    public void NormalizeToUnitLength_WithZeroVector_ReturnsZeroVector()
    {
        // Arrange
        var embedding = new float[] { 0.0f, 0.0f, 0.0f };
        
        // Act
        var result = EmbeddingPooling.NormalizeToUnitLength(embedding);
        
        // Assert
        Assert.All(result, val => Assert.Equal(0.0f, val));
    }
    
    [Fact]
    public void PoolAndNormalize_CombinesBothOperations()
    {
        // Arrange
        var outputTensor = new float[] 
        { 
            3.0f, 4.0f,  // Token 1: [3.0, 4.0]
            3.0f, 4.0f   // Token 2: [3.0, 4.0]
        };
        var attentionMask = new long[] { 1, 1 };
        int embeddingDim = 2;
        
        // Act
        var result = EmbeddingPooling.PoolAndNormalize(outputTensor, attentionMask, embeddingDim);
        
        // Assert
        // After pooling: [3.0, 4.0] (average of identical vectors)
        // After normalization: [0.6, 0.8] (magnitude 5.0)
        Assert.Equal(0.6f, result[0], precision: 5);
        Assert.Equal(0.8f, result[1], precision: 5);
        
        var magnitude = EmbeddingPooling.CalculateMagnitude(result);
        Assert.Equal(1.0f, magnitude, precision: 5);
    }
    
    [Fact]
    public void CountActualTokens_CountsOnlyRealTokens()
    {
        // Arrange
        var attentionMask = new long[] { 1, 1, 1, 0, 0, 0 };
        
        // Act
        var count = EmbeddingPooling.CountActualTokens(attentionMask);
        
        // Assert
        Assert.Equal(3, count);
    }
    
    [Fact]
    public void CountActualTokens_WithMaxLength_CountsUpToLimit()
    {
        // Arrange
        var attentionMask = new long[] { 1, 1, 1, 1, 1 };
        
        // Act
        var count = EmbeddingPooling.CountActualTokens(attentionMask, maxLength: 3);
        
        // Assert
        Assert.Equal(3, count);
    }
    
    [Fact]
    public void CalculateMagnitude_ComputesL2Norm()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f }; // sqrt(9 + 16) = 5.0
        
        // Act
        var magnitude = EmbeddingPooling.CalculateMagnitude(vector);
        
        // Assert
        Assert.Equal(5.0f, magnitude, precision: 5);
    }
    
    [Fact]
    public void CalculateMagnitude_WithEmptyVector_ReturnsZero()
    {
        // Arrange
        var vector = Array.Empty<float>();
        
        // Act
        var magnitude = EmbeddingPooling.CalculateMagnitude(vector);
        
        // Assert
        Assert.Equal(0.0f, magnitude);
    }
    
    [Fact]
    public void CosineSimilarity_WithIdenticalNormalizedVectors_ReturnsOne()
    {
        // Arrange
        var embedding1 = new float[] { 0.6f, 0.8f };
        var embedding2 = new float[] { 0.6f, 0.8f };
        
        // Act
        var similarity = EmbeddingPooling.CosineSimilarity(embedding1, embedding2);
        
        // Assert
        Assert.Equal(1.0f, similarity, precision: 5);
    }
    
    [Fact]
    public void CosineSimilarity_WithOrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 0.0f };
        var embedding2 = new float[] { 0.0f, 1.0f };
        
        // Act
        var similarity = EmbeddingPooling.CosineSimilarity(embedding1, embedding2);
        
        // Assert
        Assert.Equal(0.0f, similarity, precision: 5);
    }
    
    [Fact]
    public void CosineSimilarity_WithDifferentDimensions_ThrowsException()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 2.0f };
        var embedding2 = new float[] { 1.0f, 2.0f, 3.0f };
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            EmbeddingPooling.CosineSimilarity(embedding1, embedding2));
    }
    
    [Theory]
    [InlineData(null)]
    public void ApplyMeanPooling_WithNullParameters_ThrowsException(float[]? outputTensor)
    {
        // Arrange
        var attentionMask = new long[] { 1 };
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            EmbeddingPooling.ApplyMeanPooling(outputTensor!, attentionMask, 2));
    }
    
    [Fact]
    public void ApplyMeanPooling_WithInvalidDimension_ThrowsException()
    {
        // Arrange
        var outputTensor = new float[] { 1.0f, 2.0f };
        var attentionMask = new long[] { 1 };
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            EmbeddingPooling.ApplyMeanPooling(outputTensor, attentionMask, 0));
    }
}
