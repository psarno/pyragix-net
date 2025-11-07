using PyRagix.Net.Core.Tokenization;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PyRagix.Net.Tests.Tokenization;

public class BertTokenizerTests
{
    [Fact]
    public void Encode_ReturnsExpectedIdsForSingleSequence()
    {
        var tokenizer = CreateTokenizer();
        var encoding = tokenizer.Encode("Hello, world!");
        var expected = new long[] { 2, 5, 9, 6, 10, 3 };

        Assert.Equal(6, encoding.SequenceLength);
        Assert.Equal(expected, encoding.InputIds.Take(expected.Length));
        Assert.True(encoding.AttentionMask.Take(expected.Length).All(m => m == 1));
        Assert.True(encoding.AttentionMask.Skip(expected.Length).All(m => m == 0));
        Assert.True(encoding.TokenTypeIds.All(t => t == 0));
    }

    [Fact]
    public void EncodePair_AssignsSegmentIds()
    {
        var tokenizer = CreateTokenizer();
        var encoding = tokenizer.EncodePair("test", "testing");
        var expectedIds = new long[] { 2, 7, 3, 7, 8, 3 };
        var expectedTypes = new long[] { 0, 0, 0, 1, 1, 1 };

        Assert.Equal(expectedIds, encoding.InputIds.Take(expectedIds.Length));
        Assert.Equal(expectedTypes, encoding.TokenTypeIds.Take(expectedTypes.Length));
        Assert.True(encoding.AttentionMask.Take(expectedIds.Length).All(m => m == 1));
    }

    [Fact]
    public void Encode_StripsAccentsAndUsesWordPieces()
    {
        var tokenizer = CreateTokenizer();
        var encoding = tokenizer.Encode("Café testing");
        var expected = new long[] { 2, 11, 7, 8, 3 };

        Assert.Equal(expected, encoding.InputIds.Take(expected.Length));
        Assert.Equal(expected.Length, encoding.SequenceLength);
    }

    private static BertTokenizer CreateTokenizer(int? maxSequenceLength = null)
    {
        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Tokenizers", "Sample");
        Assert.True(Directory.Exists(assetsDirectory), $"Tokenizer test assets missing at {assetsDirectory}");
        return new BertTokenizer(assetsDirectory, maxSequenceLength);
    }
}
