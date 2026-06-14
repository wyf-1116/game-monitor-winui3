using System.IO.MemoryMappedFiles;
using System.Text;
using GameMonitor.Models;

namespace GameMonitor.Services;

public sealed class AfterburnerReader
{
    private const string MappingName = "MAHMSharedMemory";
    private const uint Signature = 0x4D41484D;
    private const uint DeadSignature = 0x0000DEAD;
    private const int MaxPath = 260;
    private const int FixedStringsSize = MaxPath * 5;
    private const float InvalidFloat = 3.4028234663852886e38f;

    public IReadOnlyList<AfterburnerSensor> Read()
    {
        using var mapping = MemoryMappedFile.OpenExisting(
            MappingName,
            MemoryMappedFileRights.Read);
        var header = ReadBytes(mapping, 32);

        var signature = BitConverter.ToUInt32(header, 0);
        if (signature == DeadSignature)
        {
            throw new InvalidDataException("MSI Afterburner shared memory is marked as inactive.");
        }

        if (signature != Signature)
        {
            throw new InvalidDataException("MSI Afterburner shared memory signature is invalid.");
        }

        var headerSize = BitConverter.ToUInt32(header, 8);
        var entryCount = BitConverter.ToUInt32(header, 12);
        var entrySize = BitConverter.ToUInt32(header, 16);
        var gpuCount = BitConverter.ToUInt32(header, 24);
        var gpuEntrySize = BitConverter.ToUInt32(header, 28);

        if (headerSize < 24 || entryCount == 0 || entrySize < FixedStringsSize + 24)
        {
            throw new InvalidDataException("MSI Afterburner has no hardware monitoring entries.");
        }

        var mappingSize = checked((long)headerSize + (long)entryCount * entrySize + (long)gpuCount * gpuEntrySize);
        var data = ReadBytes(mapping, mappingSize);
        var sensors = new List<AfterburnerSensor>((int)entryCount);

        for (var index = 0; index < entryCount; index++)
        {
            var offset = checked((int)(headerSize + index * entrySize));
            if ((long)offset + entrySize > data.Length)
            {
                break;
            }

            var sourceName = Decode(data, offset, MaxPath);
            var sourceUnits = Decode(data, offset + MaxPath, MaxPath);
            var localizedName = Decode(data, offset + MaxPath * 2, MaxPath);
            var localizedUnits = Decode(data, offset + MaxPath * 3, MaxPath);
            var valueOffset = offset + FixedStringsSize;
            var value = BitConverter.ToSingle(data, valueOffset);
            var gpuIndex = BitConverter.ToUInt32(data, valueOffset + 16);
            var sourceId = BitConverter.ToUInt32(data, valueOffset + 20);

            if (!float.IsFinite(value) || Math.Abs(value) >= InvalidFloat)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(localizedName) ? sourceName : localizedName;
            var units = string.IsNullOrWhiteSpace(localizedUnits) ? sourceUnits : localizedUnits;
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            sensors.Add(new AfterburnerSensor(
                $"mahm:{gpuIndex}:{sourceId}:{sourceName}",
                label,
                units,
                $"{sourceName} {localizedName} {sourceUnits} {localizedUnits}",
                value,
                gpuIndex,
                sourceId));
        }

        return sensors;
    }

    private static byte[] ReadBytes(MemoryMappedFile mapping, long length)
    {
        using var accessor = mapping.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
        var bytes = new byte[checked((int)length)];
        accessor.ReadArray(0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static string Decode(byte[] data, int offset, int length)
    {
        var end = Array.IndexOf(data, (byte)0, offset, length);
        var count = (end < 0 ? offset + length : end) - offset;
        return Encoding.Default.GetString(data, offset, count).Trim();
    }
}
