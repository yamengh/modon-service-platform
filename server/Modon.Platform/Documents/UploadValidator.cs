using FormCMS;
using Modon.Platform.Requests;

namespace Modon.Platform.Documents;

/// <summary>
/// Checks an uploaded file against its slot: size, extension, declared MIME type and the file's
/// leading bytes (signature). Reuses FormCMS's signature table and adds PDF, which FormCMS lacks.
/// </summary>
public sealed class UploadValidator(SystemSettings formCmsSettings)
{
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".pdf"] = "application/pdf",
    };

    private static readonly byte[][] PdfSignatures = ["%PDF-"u8.ToArray()];

    public sealed record Accepted(string Extension, string ContentType);

    /// <summary>Returns the accepted file type, or an error code from <see cref="ErrorCodes"/>.</summary>
    public async Task<(Accepted? Ok, string? Error)> ValidateAsync(IFormFile file, AttachmentSlot slot, CancellationToken ct)
    {
        if (file.Length == 0) return (null, ErrorCodes.FileEmpty);
        if (file.Length > slot.MaxSizeKb * 1024L) return (null, ErrorCodes.FileTooLarge);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!slot.Extensions.Contains(extension) || !ContentTypes.TryGetValue(extension, out var contentType))
            return (null, ErrorCodes.FileTypeNotAllowed);

        // Browsers send the MIME type; it must agree with the extension when present.
        var declared = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(declared) && declared != "application/octet-stream" && declared != contentType)
            return (null, ErrorCodes.FileTypeNotAllowed);

        var signatures = extension == ".pdf"
            ? PdfSignatures
            : formCmsSettings.FileSignature.GetValueOrDefault(extension) ?? [];
        if (!await StartsWithAnyAsync(file, signatures, ct)) return (null, ErrorCodes.FileContentMismatch);

        return (new Accepted(extension, contentType), null);
    }

    private static async Task<bool> StartsWithAnyAsync(IFormFile file, byte[][] signatures, CancellationToken ct)
    {
        if (signatures.Length == 0) return false;
        var header = new byte[signatures.Max(s => s.Length)];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return signatures.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig));
    }
}
