using System.IO;
using Moq;
using PyRagix.Net.Config;
using PyRagix.Net.Core.Data;
using PyRagix.Net.Ingestion;
using PyRagix.Net.Ingestion.Vector;
using PyRagix.Net.Tests.TestInfrastructure;
using Xunit;

namespace PyRagix.Net.Tests;

/// <summary>
/// Validates that ingestion gracefully retries when persisting the FAISS index fails due to IO errors.
/// </summary>
public class IndexServiceRetryTests
{
    [Fact]
    public void SaveVectorIndex_WhenIoExceptionOccurs_RetriesOperation()
    {
        using var temp = new TempDirectory();
        var config = new PyRagixConfig
        {
            EmbeddingDimension = 4,
            DatabasePath = temp.Resolve("pyragix.db"),
            FaissIndexPath = temp.Resolve("faiss_index.bin"),
            LuceneIndexPath = temp.Resolve("lucene")
        };

        var vectorIndexMock = new Mock<IVectorIndex>(MockBehavior.Strict);
        vectorIndexMock.SetupGet(v => v.Count).Returns(0);
        vectorIndexMock.Setup(v => v.Dispose());
        vectorIndexMock.Setup(v => v.AddWithIds(It.IsAny<float[][]>(), It.IsAny<long[]>()));

        var saveAttempts = 0;
        vectorIndexMock
            .Setup(v => v.Save(It.IsAny<string>()))
            .Callback<string>(path =>
            {
                Assert.Equal(config.FaissIndexPath, path);
                saveAttempts++;
                if (saveAttempts == 1)
                {
                    throw new IOException("Disk busy");
                }
            });

        var factoryMock = new Mock<IVectorIndexFactory>();
        factoryMock.Setup(f => f.Create(config.EmbeddingDimension)).Returns(vectorIndexMock.Object);
        factoryMock.Setup(f => f.Load(It.IsAny<string>())).Returns(vectorIndexMock.Object);

        using var dbContext = new PyRagixDbContext(config.DatabasePath);
        dbContext.EnsureCreated();

        using var indexService = new IndexService(config, dbContext, factoryMock.Object);

        indexService.SaveVectorIndex();

        Assert.Equal(2, saveAttempts);
    }
}
