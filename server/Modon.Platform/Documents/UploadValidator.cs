using FormCMS;
using Modon.Platform.Requests;

namespace Modon.Platform.Documents;

/// <summary>
/// Checks an uploaded file against its slot: size, extension, declared MIME type and the file's
/// leading bytes (signature). Reuses FormCMS's signature table for images and adds PDF and the Office
/// formats (Doc/Docx/XLS/XLSX of the building manual).
/// </summary>
public sealed class UploadValidator(SystemSettings formCmsSettings)
{
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private static readonly byte[][] PdfSignatures = ["%PDF-"u8.ToArray()];

    /// <summary>Old Office files are OLE2 compound documents; new ones are ZIP packages.</summary>
    private static readonly byte[][] Ole2Signatures = [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]];
    private static readonly byte[][] ZipSignatures = [[0x50, 0x4B, 0x03, 0x04]];

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

        var signatures = extension switch
        {
            ".pdf" => PdfSignatures,
            ".doc" or ".xls" => Ole2Signatures,
            ".docx" or ".xlsx" => ZipSignatures,
            _ => formCmsSettings.FileSignature.GetValueOrDefault(extension) ?? [],
        };
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
