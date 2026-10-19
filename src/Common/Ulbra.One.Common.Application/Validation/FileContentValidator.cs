using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Application.Validation;

public static class FileContentValidator
{
    private sealed record Signature(byte[] Bytes, int Offset = 0);

    private static readonly Dictionary<string, string[]> ContentTypesByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".webp"] = ["image/webp"],
        [".gif"] = ["image/gif"],
        [".pdf"] = ["application/pdf"],
        [".doc"] = ["application/msword"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [".xls"] = ["application/vnd.ms-excel"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        [".csv"] = ["text/csv", "application/csv", "application/vnd.ms-excel"],
        [".txt"] = ["text/plain"],
        [".xml"] = ["application/xml", "text/xml", "application/octet-stream"],
    };

    private static readonly Dictionary<string, Signature[]> SignaturesByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = [new Signature([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])],
        [".jpg"] = [new Signature([0xFF, 0xD8, 0xFF])],
        [".jpeg"] = [new Signature([0xFF, 0xD8, 0xFF])],
        // WEBP: all entries must match (AND). Byte 0-3 must be "RIFF" AND bytes 8-11 must be "WEBP".
        [".webp"] = [new Signature([0x52, 0x49, 0x46, 0x46]), new Signature([0x57, 0x45, 0x42, 0x50], Offset: 8)],
        [".gif"] = [new Signature([0x47, 0x49, 0x46, 0x38])],
        [".pdf"] = [new Signature([0x25, 0x50, 0x44, 0x46])],
        // Legacy Office (OLE compound)
        [".doc"] = [new Signature([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])],
        [".xls"] = [new Signature([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])],
        // OOXML (ZIP)
        [".docx"] = [new Signature([0x50, 0x4B, 0x03, 0x04])],
        [".xlsx"] = [new Signature([0x50, 0x4B, 0x03, 0x04])],
        // Text-like: no magic signature check
        [".csv"] = [],
        [".txt"] = [],
        [".xml"] = [],
    };

    /// <summary>
    /// Validates a file's extension, content-type, and magic-byte header against a whitelist.
    /// When an extension has multiple signatures, ALL must match (AND semantics).
    /// </summary>
    /// <param name="content">Must be a seekable stream; throws ArgumentException otherwise.</param>
    public static Result Validate(string fileName, string contentType, Stream content)
    {
        if (!content.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(content));

        if (content.Length == 0)
            return Result.Failure(Error.Validation(ErrorCodes.File.Empty, "File is empty."));

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !ContentTypesByExt.ContainsKey(ext))
            return Result.Failure(Error.Validation(ErrorCodes.File.ExtensionNotAllowed, $"Extension '{ext}' is not allowed."));

        var allowedContentTypes = ContentTypesByExt[ext];
        if (!allowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return Result.Failure(Error.Validation(ErrorCodes.File.ContentTypeMismatch, $"Content type '{contentType}' does not match extension '{ext}'."));

        var signatures = SignaturesByExt[ext];
        if (signatures.Length == 0) return Result.Success();

        var originalPosition = content.Position;
        try
        {
            var maxOffsetEnd = signatures.Max(s => s.Offset + s.Bytes.Length);
            var header = new byte[maxOffsetEnd];
            content.Position = 0;
            var read = content.Read(header, 0, header.Length);
            if (read < maxOffsetEnd)
                return Result.Failure(Error.Validation(ErrorCodes.File.ContentMismatch, "File header is too short to match expected signature."));

            foreach (var sig in signatures)
            {
                for (var i = 0; i < sig.Bytes.Length; i++)
                {
                    if (header[sig.Offset + i] != sig.Bytes[i])
                        return Result.Failure(Error.Validation(ErrorCodes.File.ContentMismatch, $"File content does not match expected signature for '{ext}'."));
                }
            }

            return Result.Success();
        }
        finally
        {
            content.Position = originalPosition;
        }
    }
}
