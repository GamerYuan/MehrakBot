#region

using System.Text;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;

#endregion

namespace MehrakCore.Tests.Repositories;

[Parallelizable(ParallelScope.Fixtures)]
public class ImageRepositoryTests
{
    private ImageRepository m_ImageRepository;
    private Mock<ILogger<ImageRepository>> m_LoggerMock;

    [SetUp]
    public void Setup()
    {
        m_LoggerMock = new Mock<ILogger<ImageRepository>>();
        m_ImageRepository = new ImageRepository(MongoTestHelper.Instance.MongoDbService, m_LoggerMock.Object);
    }

    [Test]
    public async Task UploadFileAsync_ShouldReturnObjectId()
    {
        // Arrange
        var fileName = "test-file.txt";
        var content = "This is a test file content";
        var contentType = "text/plain";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Act
        var objectId = await m_ImageRepository.UploadFileAsync(fileName, stream, contentType);

        // Assert
        Assert.That(objectId, Is.Not.EqualTo(ObjectId.Empty));
    }

    [Test]
    public async Task DownloadFileToStreamAsync_ShouldReturnFileContent()
    {
        // Arrange
        var fileName = "test-download.txt";
        var content = "This is a test file content for download";
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await m_ImageRepository.UploadFileAsync(fileName, contentStream, "text/plain");

        // Act
        var downloadedStream = await m_ImageRepository.DownloadFileToStreamAsync(fileName);

        // Assert
        using var reader = new StreamReader(downloadedStream);
        var downloadedContent = await reader.ReadToEndAsync();
        Assert.That(downloadedContent, Is.EqualTo(content));
    }

    [Test]
    public async Task FileExistsAsync_ShouldReturnTrue_WhenFileExists()
    {
        // Arrange
        var fileName = "test-exists.txt";
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("File content"));
        await m_ImageRepository.UploadFileAsync(fileName, contentStream);

        // Act
        var exists = await m_ImageRepository.FileExistsAsync(fileName);

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task FileExistsAsync_ShouldReturnFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var fileName = "non-existent-file.txt";

        // Act
        var exists = await m_ImageRepository.FileExistsAsync(fileName);

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task DeleteFileAsync_ShouldRemoveFile()
    {
        // Arrange
        var fileName = "test-delete.txt";
        var contentStream = new MemoryStream(Encoding.UTF8.GetBytes("File to delete"));
        await m_ImageRepository.UploadFileAsync(fileName, contentStream);

        // Verify file exists before deletion
        var existsBeforeDelete = await m_ImageRepository.FileExistsAsync(fileName);
        Assert.That(existsBeforeDelete, Is.True);

        // Act
        await m_ImageRepository.DeleteFileAsync(fileName);

        // Assert
        var existsAfterDelete = await m_ImageRepository.FileExistsAsync(fileName);
        Assert.That(existsAfterDelete, Is.False);
    }

    [Test]
    public void DeleteFileAsync_ShouldNotThrowException_WhenFileDoesNotExist()
    {
        // Arrange
        var fileName = "non-existent-file-for-deletion.txt";

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await m_ImageRepository.DeleteFileAsync(fileName));
    }
}
