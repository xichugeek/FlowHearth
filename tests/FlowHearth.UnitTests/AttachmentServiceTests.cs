using System.Security.Cryptography;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Common;
using FlowHearth.Domain.Attachments;
using FlowHearth.Infrastructure.Attachments;

namespace FlowHearth.UnitTests;

public sealed class AttachmentServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 12, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("report/secret.txt")]
    [InlineData("report?.txt")]
    public async Task UploadRejectsUntrustedFileNames(string fileName)
    {
        var repository = new FakeRepository();
        var storage = new FakeStorage();
        var service = CreateService(repository, storage);
        await using var content = new MemoryStream([1]);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.UploadAsync(
            new UploadAttachmentCommand(AttachmentEntityType.Customer, 1, fileName, "text/plain", 1, content, null),
            1,
            CancellationToken.None));
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task UploadRejectsDeclaredOversizeBeforeReading()
    {
        var storage = new FakeStorage { MaximumFileSizeBytes = 4 };
        var service = CreateService(new FakeRepository(), storage);
        await using var content = new MemoryStream([1, 2, 3, 4, 5]);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.UploadAsync(
            new UploadAttachmentCommand(AttachmentEntityType.Customer, 1, "report.txt", "text/plain", 5, content, null),
            1,
            CancellationToken.None));
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task UploadDeletesStoredFileWhenMetadataWriteFails()
    {
        var repository = new FakeRepository { FailCreate = true };
        var storage = new FakeStorage();
        var service = CreateService(repository, storage);
        await using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(
            new UploadAttachmentCommand(AttachmentEntityType.Customer, 1, "report.txt", "text/plain", 3, content, "说明"),
            1,
            CancellationToken.None));
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(1, storage.DeleteCount);
    }

    [Fact]
    public async Task ListRejectsMissingEntity()
    {
        var repository = new FakeRepository { EntityExists = false };
        var service = CreateService(repository, new FakeStorage());

        await Assert.ThrowsAsync<NotFoundException>(() => service.ListAsync(
            AttachmentEntityType.Equipment,
            99,
            CancellationToken.None));
    }

    [Fact]
    public async Task LocalStorageUsesGeneratedKeyHashesContentAndBlocksTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flowhearth-attachment-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(root, 16, new FixedTimeProvider(Now));
            var bytes = "安全附件"u8.ToArray();
            await using var content = new MemoryStream(bytes);

            var stored = await storage.SaveAsync(content, CancellationToken.None);

            Assert.Matches(@"^2026/08/[a-f0-9]{32}$", stored.StorageKey);
            Assert.Equal((ulong)bytes.Length, stored.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
            await using var opened = await storage.OpenReadAsync(stored.StorageKey, CancellationToken.None);
            using var copy = new MemoryStream();
            await opened.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../../secret", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task LocalStorageEnforcesStreamingLimitAndRemovesPartialFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flowhearth-attachment-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(root, 4, new FixedTimeProvider(Now));
            await using var content = new MemoryStream([1, 2, 3, 4, 5]);

            await Assert.ThrowsAsync<FlowHearthValidationException>(() => storage.SaveAsync(content, CancellationToken.None));
            Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : []);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static AttachmentService CreateService(FakeRepository repository, FakeStorage storage) =>
        new(repository, storage, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeStorage : IFileStorage
    {
        public long MaximumFileSizeBytes { get; set; } = 20 * 1024 * 1024;
        public int SaveCount { get; private set; }
        public int DeleteCount { get; private set; }

        public Task<StoredFile> SaveAsync(Stream content, CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(new StoredFile("2026/08/0123456789abcdef0123456789abcdef", 3, new string('a', 64)));
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IAttachmentRepository
    {
        public bool EntityExists { get; set; } = true;
        public bool FailCreate { get; set; }

        public Task<AttachmentEntityReference?> FindEntityAsync(AttachmentEntityType entityType, ulong entityId, CancellationToken cancellationToken) =>
            Task.FromResult<AttachmentEntityReference?>(EntityExists ? new(entityType, entityId, "CU-2026-0001") : null);

        public Task<IReadOnlyList<AttachmentSummary>> ListAsync(AttachmentEntityType entityType, ulong entityId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AttachmentSummary>>([]);

        public Task<AttachmentRecord?> FindAsync(ulong attachmentId, CancellationToken cancellationToken) =>
            Task.FromResult<AttachmentRecord?>(null);

        public Task<AttachmentSummary> CreateAsync(CreateAttachmentData data, CancellationToken cancellationToken)
        {
            if (FailCreate)
            {
                throw new InvalidOperationException("metadata failed");
            }

            return Task.FromResult(new AttachmentSummary(1, data.EntityType, data.EntityId, "CU-2026-0001", data.OriginalFileName, data.ContentType, data.SizeBytes, data.Sha256, data.Description, 1, data.NowUtc, data.ActorUserId, "Actor"));
        }

        public Task DeleteAsync(ulong attachmentId, DeleteAttachmentData data, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
