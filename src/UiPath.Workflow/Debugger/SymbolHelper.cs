// This file is part of Core WF which is licensed under the MIT license.
// See LICENSE file in the project root for full license information.

using System.Activities.Runtime;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;

namespace System.Activities.Debugger.Symbol;

internal static class SymbolHelper
{
    private static readonly Guid s_md5IdentifierGuid = new("406ea660-64cf-4c82-b6f0-42d48172a799");
    private static readonly Guid s_sha1IdentifierGuid = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");

    public static Guid ChecksumProviderId =>
        LocalAppContextSwitches.UseMD5ForWFDebugger ? s_md5IdentifierGuid : s_sha1IdentifierGuid;

    // This is the same Encode/Decode logic as the WCF FramingEncoder
    public static int ReadEncodedInt32(BinaryReader reader)
    {
        var value = 0;
        var bytesConsumed = 0;
        while (true)
        {
            int next = reader.ReadByte();
            value |= (next & 0x7F) << (bytesConsumed * 7);
            bytesConsumed++;
            if ((next & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    // This is the same Encode/Decode logic as the WCF FramingEncoder
    public static void WriteEncodedInt32(BinaryWriter writer, int value)
    {
        Fx.Assert(value >= 0, "Must be non-negative");

        while ((value & 0xFFFFFF80) != 0)
        {
            writer.Write((byte) ((value & 0x7F) | 0x80));
            value >>= 7;
        }

        writer.Write((byte) value);
    }

    public static int GetEncodedSize(int value)
    {
        Fx.Assert(value >= 0, "Must be non-negative");

        var count = 1;
        while ((value & 0xFFFFFF80) != 0)
        {
            count++;
            value >>= 7;
        }

        return count;
    }

    public static byte[] CalculateChecksum(string fileName)
    {
        Fx.Assert(!string.IsNullOrEmpty(fileName), "fileName should not be empty or null");
        byte[] checksum;
        try
        {
            using var streamReader = new StreamReader(fileName!);
            using var hashAlgorithm = CreateHashProvider();
            checksum = hashAlgorithm.ComputeHash(streamReader.BaseStream);
        }
        catch (IOException)
        {
            // DirectoryNotFoundException and FileNotFoundException are expected
            checksum = null;
        }
        catch (UnauthorizedAccessException)
        {
            // UnauthorizedAccessException is expected
            checksum = null;
        }
        catch (SecurityException)
        {
            // Must not have had enough permissions to access the file.
            checksum = null;
        }

        return checksum;
    }

    [Fx.Tag.SecurityNoteAttribute(
        Critical = "Used to get a string from checksum that is provided by the user/from a file.",
        Safe = "We not exposing any critical data. Just converting the byte array to a hex string.")]
    [SecuritySafeCritical]
    public static string GetHexStringFromChecksum(byte[] checksum)
    {
        return checksum == null
            ? string.Empty
            : string.Join(string.Empty, checksum.Select(x => x.ToString("X2")).ToArray());
    }

    [Fx.Tag.SecurityNoteAttribute(Critical = "Used to validate checksum that is provided by the user/from a file.",
        Safe = "We are not exposing any critical data. Just validating that the provided checksum meets the format for the checksums we produce.")]
    [SecuritySafeCritical]
    internal static bool ValidateChecksum(byte[] checksumToValidate) =>
        // We are using MD5.ComputeHash, which will return a 16 byte array.
        LocalAppContextSwitches.UseMD5ForWFDebugger
            ? checksumToValidate.Length == 16
            : checksumToValidate.Length == 20;

    //[SuppressMessage("Microsoft.Cryptographic.Standard", "CA5350:MD5CannotBeUsed",
    //    Justification = "Design has been approved.  We are not using MD5 for any security or cryptography purposes but rather as a hash.")]
    private static HashAlgorithm CreateHashProvider() =>
        LocalAppContextSwitches.UseMD5ForWFDebugger ? MD5.Create() : SHA1.Create();
}
