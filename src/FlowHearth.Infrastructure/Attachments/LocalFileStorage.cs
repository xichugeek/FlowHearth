using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Common;

namespace FlowHearth.Infrastructure.Attachments;

public sealed partial class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920;
    private readonly string _rootPath;
    private readonly TimeProvider _timeProvider;

    public LocalFileStorage(
        string rootPath,
        long maximumFileSizeBytes,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("FileStorage:RootPath is required.");
        }

        if (maximumFileSizeBytes is < 1 or > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "FileStorage:MaximumFileSizeBytes must be between 1 byte and 100 MiB.");
        }

        _rootPath = Path.GetFullPath(rootPath);
        MaximumFileSizeBytes = maximumFileSizeBytes;
        _timeProvider = timeProvider;
    }

    public long MaximumFileSizeBytes { get; }

    public async Task<StoredFile> SaveAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        if (!content.CanRead)
        {
            throw FlowHearthValidationException.For("file", "文件流不可读取。");
        }

        var now = _timeProvider.GetUtcNow();
        var storageKey = $"{now.Year:D4}/{now.Month:D2}/{Guid.NewGuid():N}";
        var finalPath = ResolveStoragePath(storageKey);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Could not determine the storage directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.uploading");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        ulong total = 0;

        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await content.ReadAsync(
                        buffer.AsMemory(0, BufferSize),
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += (uint)read;
                    if (total > (ulong)MaximumFileSizeBytes)
                    {
                        throw FlowHearthValidationException.For(
                            "file",
                            $"文件不能超过 {MaximumFileSizeBytes / 1024 / 1024} MiB。");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            if (total == 0)
            {
                throw FlowHearthValidationException.For("file", "不能上传空文件。");
            }

            File.Move(temporaryPath, finalPath);
            var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            return new StoredFile(storageKey, total, sha256);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(storageKey);
        if (!File.Exists(path))
        {
            throw new NotFoundException("附件文件不存在。");
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)
            || !GeneratedStorageKey().IsMatch(storageKey))
        {
            throw new InvalidOperationException("Invalid attachment storage key.");
        }

        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var requiredPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Attachment path escaped the storage root.");
        }

        return path;
    }

    [GeneratedRegex(@"^[0-9]{4}/(?:0[1-9]|1[0-2])/[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedStorageKey();
}
