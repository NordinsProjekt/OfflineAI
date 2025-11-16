using Application.AI.Extensions;

namespace Application.AI.Tests.Extensions;

/// <summary>
/// Unit tests for EmbeddingExtensions class.
/// Tests cover all extension methods including conversion, magnitude calculation,
/// cosine similarity, normalization checking, and edge cases.
/// </summary>
public class EmbeddingExtensionsTests
{
    #region AsReadOnlyMemory Tests

    [Fact]
    public void AsReadOnlyMemory_WithValidArray_ReturnsReadOnlyMemory()
    {
        // Arrange
        var array = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = array.AsReadOnlyMemory();

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(1.0f, result.Span[0]);
        Assert.Equal(2.0f, result.Span[1]);
        Assert.Equal(3.0f, result.Span[2]);
    }

    [Fact]
    public void AsReadOnlyMemory_WithEmptyArray_ReturnsEmptyMemory()
    {
        // Arrange
        var array = Array.Empty<float>();

        // Act
        var result = array.AsReadOnlyMemory();

        // Assert
        Assert.Equal(0, result.Length);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void AsReadOnlyMemory_WithNullArray_ThrowsArgumentNullException()
    {
        // Arrange
        float[] array = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => array.AsReadOnlyMemory());
    }

    [Fact]
    public void AsReadOnlyMemory_WithLargeArray_ReturnsCorrectMemory()
    {
        // Arrange
        var array = new float[1000];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i * 0.1f;
        }

        // Act
        var result = array.AsReadOnlyMemory();

        // Assert
        Assert.Equal(1000, result.Length);
        Assert.Equal(0f, result.Span[0]);
        Assert.Equal(99.9f, result.Span[999], precision: 5);
    }

    #endregion

    #region GetMagnitude Tests

    [Fact]
    public void GetMagnitude_WithUnitVector_ReturnsOne()
    {
        // Arrange - unit vector [1, 0, 0]
        var embedding = new float[] { 1.0f, 0.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(1.0f, magnitude, precision: 5);
    }

    [Fact]
    public void GetMagnitude_WithSimpleVector_ReturnsCorrectValue()
    {
        // Arrange - vector [3, 4] with magnitude 5
        var embedding = new float[] { 3.0f, 4.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(5.0f, magnitude, precision: 5);
    }

    [Fact]
    public void GetMagnitude_WithZeroVector_ReturnsZero()
    {
        // Arrange
        var embedding = new float[] { 0.0f, 0.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(0.0f, magnitude);
    }

    [Fact]
    public void GetMagnitude_WithNegativeValues_ReturnsCorrectValue()
    {
        // Arrange - vector [-3, -4] with magnitude 5
        var embedding = new float[] { -3.0f, -4.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(5.0f, magnitude, precision: 5);
    }

    [Fact]
    public void GetMagnitude_WithMixedValues_ReturnsCorrectValue()
    {
        // Arrange - vector [1, -2, 2] with magnitude 3
        var embedding = new float[] { 1.0f, -2.0f, 2.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(3.0f, magnitude, precision: 5);
    }

    [Fact]
    public void GetMagnitude_WithEmptyVector_ReturnsZero()
    {
        // Arrange
        var embedding = Array.Empty<float>().AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.Equal(0.0f, magnitude);
    }

    [Fact]
    public void GetMagnitude_WithHighDimensionalVector_ReturnsCorrectValue()
    {
        // Arrange - vector with all 1s, magnitude = sqrt(768)
        var array = new float[768];
        Array.Fill(array, 1.0f);
        var embedding = array.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        var expectedMagnitude = (float)Math.Sqrt(768);
        Assert.Equal(expectedMagnitude, magnitude, precision: 5);
    }

    #endregion

    #region CosineSimilarity Tests

    [Fact]
    public void CosineSimilarity_WithIdenticalNormalizedVectors_ReturnsOne()
    {
        // Arrange - two identical normalized vectors
        var embedding1 = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();
        var embedding2 = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert
        Assert.Equal(1.0f, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithOrthogonalVectors_ReturnsZero()
    {
        // Arrange - perpendicular vectors [1, 0] and [0, 1]
        var embedding1 = new float[] { 1.0f, 0.0f }.AsReadOnlyMemory();
        var embedding2 = new float[] { 0.0f, 1.0f }.AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert
        Assert.Equal(0.0f, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithOppositeVectors_ReturnsNegativeOne()
    {
        // Arrange - opposite vectors [1, 0] and [-1, 0]
        var embedding1 = new float[] { 1.0f, 0.0f }.AsReadOnlyMemory();
        var embedding2 = new float[] { -1.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert
        Assert.Equal(-1.0f, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithDifferentLengthVectors_ThrowsArgumentException()
    {
        // Arrange
        var embedding1 = new float[] { 1.0f, 2.0f, 3.0f }.AsReadOnlyMemory();
        var embedding2 = new float[] { 1.0f, 2.0f }.AsReadOnlyMemory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            embedding1.CosineSimilarity(embedding2));
        Assert.Contains("same dimension", exception.Message);
    }

    [Fact]
    public void CosineSimilarity_WithSimilarVectors_ReturnsHighValue()
    {
        // Arrange - similar but not identical normalized vectors
        var embedding1 = new float[] { 0.7071f, 0.7071f }.AsReadOnlyMemory();
        var embedding2 = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert
        Assert.True(similarity > 0.9f, $"Expected similarity > 0.9, got {similarity}");
    }

    [Fact]
    public void CosineSimilarity_WithEmptyVectors_ReturnsZero()
    {
        // Arrange
        var embedding1 = Array.Empty<float>().AsReadOnlyMemory();
        var embedding2 = Array.Empty<float>().AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert
        Assert.Equal(0.0f, similarity);
    }

    [Fact]
    public void CosineSimilarity_WithHighDimensionalVectors_ReturnsCorrectValue()
    {
        // Arrange
        var array1 = new float[768];
        var array2 = new float[768];
        for (int i = 0; i < 768; i++)
        {
            array1[i] = 1.0f / (float)Math.Sqrt(768); // normalized
            array2[i] = 1.0f / (float)Math.Sqrt(768); // normalized
        }
        var embedding1 = array1.AsReadOnlyMemory();
        var embedding2 = array2.AsReadOnlyMemory();

        // Act
        var similarity = embedding1.CosineSimilarity(embedding2);

        // Assert - use 4 decimal places for high-dimensional vectors due to floating-point precision
        Assert.Equal(1.0f, similarity, precision: 4);
    }

    #endregion

    #region CosineSimilarityWithNormalization Tests

    [Fact]
    public void CosineSimilarityWithNormalization_WithIdenticalVectors_ReturnsOne()
    {
        // Arrange - identical non-normalized vectors
        var vector1 = new float[] { 3.0f, 4.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 3.0f, 4.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithOrthogonalVectors_ReturnsZero()
    {
        // Arrange - perpendicular non-normalized vectors [3, 0] and [0, 4]
        var vector1 = new float[] { 3.0f, 0.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 0.0f, 4.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(0.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithOppositeVectors_ReturnsNegativeOne()
    {
        // Arrange - opposite non-normalized vectors [3, 4] and [-3, -4]
        var vector1 = new float[] { 3.0f, 4.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { -3.0f, -4.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(-1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithZeroVector_ReturnsZero()
    {
        // Arrange
        var vector1 = new float[] { 0.0f, 0.0f, 0.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 1.0f, 2.0f, 3.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithBothZeroVectors_ReturnsZero()
    {
        // Arrange
        var vector1 = new float[] { 0.0f, 0.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 0.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithDifferentLengthVectors_ThrowsArgumentException()
    {
        // Arrange
        var vector1 = new float[] { 1.0f, 2.0f, 3.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 1.0f, 2.0f }.AsReadOnlyMemory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            vector1.CosineSimilarityWithNormalization(vector2));
        Assert.Contains("same length", exception.Message);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithScaledVectors_ReturnsSameAsOriginal()
    {
        // Arrange - scaled versions of the same vector should have similarity of 1
        var vector1 = new float[] { 1.0f, 2.0f, 3.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 10.0f, 20.0f, 30.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithNormalizedVectors_MatchesCosineSimilarity()
    {
        // Arrange - already normalized vectors
        var vector1 = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();
        var vector2 = new float[] { 0.7071f, 0.7071f }.AsReadOnlyMemory();

        // Act
        var similarityWithNormalization = vector1.CosineSimilarityWithNormalization(vector2);
        var similarityWithoutNormalization = vector1.CosineSimilarity(vector2);

        // Assert - both methods should give similar results for normalized vectors
        Assert.Equal(similarityWithoutNormalization, (float)similarityWithNormalization, precision: 4);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithEmptyVectors_ReturnsZero()
    {
        // Arrange
        var vector1 = Array.Empty<float>().AsReadOnlyMemory();
        var vector2 = Array.Empty<float>().AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithHighDimensionalVectors_ReturnsCorrectValue()
    {
        // Arrange
        var array1 = new float[768];
        var array2 = new float[768];
        for (int i = 0; i < 768; i++)
        {
            array1[i] = i * 0.01f; // non-normalized
            array2[i] = i * 0.01f; // identical non-normalized
        }
        var vector1 = array1.AsReadOnlyMemory();
        var vector2 = array2.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void CosineSimilarityWithNormalization_WithNegativeValues_HandlesCorrectly()
    {
        // Arrange
        var vector1 = new float[] { -1.0f, 2.0f, -3.0f }.AsReadOnlyMemory();
        var vector2 = new float[] { 1.0f, -2.0f, 3.0f }.AsReadOnlyMemory();

        // Act
        var similarity = vector1.CosineSimilarityWithNormalization(vector2);

        // Assert - should be -1 (opposite vectors)
        Assert.Equal(-1.0, similarity, precision: 5);
    }

    #endregion

    #region IsNormalized Tests

    [Fact]
    public void IsNormalized_WithNormalizedVector_ReturnsTrue()
    {
        // Arrange - vector [0.6, 0.8] with magnitude 1
        var embedding = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.True(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithNonNormalizedVector_ReturnsFalse()
    {
        // Arrange - vector [3, 4] with magnitude 5
        var embedding = new float[] { 3.0f, 4.0f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.False(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithUnitVector_ReturnsTrue()
    {
        // Arrange - unit vector [1, 0, 0]
        var embedding = new float[] { 1.0f, 0.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.True(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithZeroVector_ReturnsFalse()
    {
        // Arrange
        var embedding = new float[] { 0.0f, 0.0f, 0.0f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.False(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithCustomTolerance_RespectsToleranceParameter()
    {
        // Arrange - vector slightly off from normalized (magnitude ~1.005)
        var embedding = new float[] { 0.6f, 0.802f }.AsReadOnlyMemory();

        // Act
        var strictCheck = embedding.IsNormalized(tolerance: 0.001f);
        var lenientCheck = embedding.IsNormalized(tolerance: 0.01f);

        // Assert
        Assert.False(strictCheck);
        Assert.True(lenientCheck);
    }

    [Fact]
    public void IsNormalized_WithNegativeValues_WorksCorrectly()
    {
        // Arrange - normalized vector with negative values [-0.6, 0.8]
        var embedding = new float[] { -0.6f, 0.8f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.True(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithEmptyVector_ReturnsFalse()
    {
        // Arrange
        var embedding = Array.Empty<float>().AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.False(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithHighDimensionalNormalizedVector_ReturnsTrue()
    {
        // Arrange - normalized high-dimensional vector
        var array = new float[768];
        var value = 1.0f / (float)Math.Sqrt(768);
        Array.Fill(array, value);
        var embedding = array.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.True(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithHighDimensionalNonNormalizedVector_ReturnsFalse()
    {
        // Arrange - non-normalized high-dimensional vector
        var array = new float[768];
        Array.Fill(array, 1.0f); // magnitude = sqrt(768) ? 27.7
        var embedding = array.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized();

        // Assert
        Assert.False(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithVerySmallTolerance_WorksCorrectly()
    {
        // Arrange - perfectly normalized vector
        var embedding = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();

        // Act
        var isNormalized = embedding.IsNormalized(tolerance: 0.0001f);

        // Assert
        Assert.True(isNormalized);
    }

    [Fact]
    public void IsNormalized_WithLargeTolerance_AcceptsNearNormalizedVectors()
    {
        // Arrange - vector with magnitude ~1.1
        var embedding = new float[] { 0.7f, 0.8f }.AsReadOnlyMemory(); // magnitude ? 1.063

        // Act
        var isNormalized = embedding.IsNormalized(tolerance: 0.1f);

        // Assert
        Assert.True(isNormalized);
    }

    #endregion

    #region Integration/Edge Case Tests

    [Fact]
    public void EmbeddingWorkflow_ConvertCalculateNormalizeCheck_WorksCorrectly()
    {
        // Arrange
        var array = new float[] { 3.0f, 4.0f };

        // Act
        var embedding = array.AsReadOnlyMemory();
        var magnitude = embedding.GetMagnitude();
        var isNormalized = embedding.IsNormalized();

        // Normalize manually
        var normalizedArray = new float[] { array[0] / magnitude, array[1] / magnitude };
        var normalizedEmbedding = normalizedArray.AsReadOnlyMemory();
        var isNowNormalized = normalizedEmbedding.IsNormalized();

        // Assert
        Assert.Equal(5.0f, magnitude, precision: 5);
        Assert.False(isNormalized);
        Assert.True(isNowNormalized);
    }

    [Fact]
    public void CosineSimilarity_BetweenNormalizedAndNonNormalized_ProducesDifferentResults()
    {
        // Arrange
        var normalized1 = new float[] { 0.6f, 0.8f }.AsReadOnlyMemory();
        var normalized2 = new float[] { 0.8f, 0.6f }.AsReadOnlyMemory();
        
        var nonNormalized1 = new float[] { 6.0f, 8.0f }.AsReadOnlyMemory();
        var nonNormalized2 = new float[] { 8.0f, 6.0f }.AsReadOnlyMemory();

        // Act
        var similarityNormalized = normalized1.CosineSimilarity(normalized2);
        var similarityNonNormalized = nonNormalized1.CosineSimilarity(nonNormalized2);
        var similarityWithNormalization = nonNormalized1.CosineSimilarityWithNormalization(nonNormalized2);

        // Assert
        Assert.NotEqual(similarityNormalized, similarityNonNormalized);
        Assert.Equal((double)similarityNormalized, similarityWithNormalization, precision: 5);
    }

    [Fact]
    public void GetMagnitude_WithVerySmallValues_HandlesCorrectly()
    {
        // Arrange
        var embedding = new float[] { 0.0001f, 0.0002f, 0.0003f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        Assert.True(magnitude > 0);
        Assert.Equal((float)Math.Sqrt(0.0001*0.0001 + 0.0002*0.0002 + 0.0003*0.0003), magnitude, precision: 8);
    }

    [Fact]
    public void GetMagnitude_WithVeryLargeValues_HandlesCorrectly()
    {
        // Arrange
        var embedding = new float[] { 1000.0f, 2000.0f, 3000.0f }.AsReadOnlyMemory();

        // Act
        var magnitude = embedding.GetMagnitude();

        // Assert
        var expectedMagnitude = (float)Math.Sqrt(1000*1000 + 2000*2000 + 3000*3000);
        Assert.Equal(expectedMagnitude, magnitude, precision: 5);
    }

    #endregion
}
