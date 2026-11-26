// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Prowl.Editor.Importers;

/// <summary>
/// Unity gives model sub-objects deterministic file IDs: an XXH64 hash of a "Type:&lt;type&gt;-&gt;&lt;path&gt;0"
/// string, cast to a signed 64-bit integer. Computing those hashes lets a prefab's mesh reference be
/// matched to the mesh sub-asset Prowl's model importer created, without keeping a separate table.
/// </summary>
internal static class UnityFileId
{
    private const ulong P1 = 11400714785074694791UL;
    private const ulong P2 = 14029467366897019727UL;
    private const ulong P3 = 1609587929392839161UL;
    private const ulong P4 = 9650029242287828579UL;
    private const ulong P5 = 2870177450012600261UL;

    public static long Hash(string text) => unchecked((long)Xxh64(Encoding.UTF8.GetBytes(text)));

    /// <summary>Whether a UnityMesh file ID could have come from this sub-asset name.</summary>
    public static bool Matches(long fileId, string name)
    {
        if (fileId == 0 || string.IsNullOrEmpty(name)) return false;

        var variants = new HashSet<string>(StringComparer.Ordinal) { name, name.Replace('/', '.') };
        int slash = name.IndexOf('/');
        if (slash > 0)
        {
            string node = name[..slash];
            string slot = name[(slash + 1)..];
            variants.Add(node);
            variants.Add(node + slot);
            variants.Add(node + "_" + slot);
            variants.Add(node + "." + slot);
            int slotIndex = slot.IndexOf("slot", StringComparison.OrdinalIgnoreCase);
            if (slotIndex >= 0) variants.Add(node + slot[slotIndex..]);
        }

        foreach (string variant in variants)
        {
            for (int index = 0; index < 2; index++)
                if (Hash($"Type:Mesh->{variant}{index}") == fileId) return true;
            if (Hash($"Type:Mesh->{variant}") == fileId) return true;
        }
        return false;
    }

    private static ulong Xxh64(byte[] data)
    {
        int length = data.Length;
        int offset = 0;
        ulong hash;

        if (length >= 32)
        {
            ulong v1 = unchecked(P1 + P2);
            ulong v2 = P2;
            ulong v3 = 0UL;
            ulong v4 = unchecked(0UL - P1);
            while (offset + 32 <= length)
            {
                v1 = Round(v1, Read(data, offset)); offset += 8;
                v2 = Round(v2, Read(data, offset)); offset += 8;
                v3 = Round(v3, Read(data, offset)); offset += 8;
                v4 = Round(v4, Read(data, offset)); offset += 8;
            }
            hash = unchecked(RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18));
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else hash = P5;

        hash = unchecked(hash + (ulong)length);

        while (offset + 8 <= length)
        {
            ulong lane = Round(0UL, Read(data, offset));
            hash ^= lane;
            hash = unchecked(RotateLeft(hash, 27) * P1 + P4);
            offset += 8;
        }

        if (offset + 4 <= length)
        {
            hash ^= unchecked((ulong)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) * P1);
            hash = unchecked(RotateLeft(hash, 23) * P2 + P3);
            offset += 4;
        }

        while (offset < length)
        {
            hash ^= unchecked((ulong)data[offset] * P5);
            hash = unchecked(RotateLeft(hash, 11) * P1);
            offset++;
        }

        hash ^= hash >> 33;
        hash = unchecked(hash * P2);
        hash ^= hash >> 29;
        hash = unchecked(hash * P3);
        hash ^= hash >> 32;
        return hash;
    }

    private static ulong Read(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));

    private static ulong Round(ulong accumulator, ulong input)
    {
        accumulator = unchecked(accumulator + input * P2);
        accumulator = RotateLeft(accumulator, 31);
        return unchecked(accumulator * P1);
    }

    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        value = Round(0UL, value);
        accumulator ^= value;
        return unchecked(accumulator * P1 + P4);
    }
}
