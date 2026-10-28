using System.Buffers.Binary;

namespace ETranslate.Documents.Api.Documents;

public static class DocumentImagePolicy
{
    // Bound decoded size using PNG IHDR or JPEG SOF, without executing a codec during upload.
    public static bool HasSafeDimensions(ReadOnlySpan<byte> bytes, string type)
    {
        int width = 0, height = 0;
        if (!SourceFilePolicy.HasValidSignature(bytes[..Math.Min(bytes.Length, SourceFilePolicy.MaximumSignatureLength)], type)) return false;
        if (type == "image/png")
        {
            if (bytes.Length < 33 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadInt32BigEndian(bytes[8..]) != 13) return false;
            width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]); height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        }
        else if (type == "image/jpeg")
        {
            var offset = 2;
            while (offset + 3 < bytes.Length)
            {
                if (bytes[offset++] != 0xff) return false;
                while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
                if (offset >= bytes.Length) return false;
                var marker = bytes[offset++];
                if (marker is 0xd9 or 0xda) return false;
                if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
                if (offset + 2 > bytes.Length) return false;
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                if (length < 2 || offset + length > bytes.Length) return false;
                if (marker is 0xc0 or 0xc1 or 0xc2)
                {
                    if (length < 8) return false;
                    height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]); width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]); break;
                }
                offset += length;
            }
        }
        return width is > 0 and <= 10000 && height is > 0 and <= 10000 && (long)width * height <= 16_000_000;
    }
}
