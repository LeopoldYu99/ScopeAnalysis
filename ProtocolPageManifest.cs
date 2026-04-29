using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ScopeAnalysis
{
    [DataContract]
    internal sealed class ProtocolPageManifest
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "protocolType")]
        public string ProtocolType { get; set; }

        [DataMember(Name = "lineCount")]
        public int LineCount { get; set; }

        [DataMember(Name = "sampleRate")]
        public uint SampleRate { get; set; }

        [DataMember(Name = "dataRate")]
        public uint DataRate { get; set; }

        [DataMember(Name = "baudRate")]
        public int BaudRate { get; set; }

        [DataMember(Name = "parityText")]
        public string ParityText { get; set; }

        [DataMember(Name = "dataBits")]
        public int DataBits { get; set; }

        [DataMember(Name = "stopBits")]
        public double StopBits { get; set; }

        [DataMember(Name = "samplesPerBit")]
        public int SamplesPerBit { get; set; }

        [DataMember(Name = "bitOrder", EmitDefaultValue = false)]
        public string BitOrder { get; set; }

        [DataMember(Name = "pageDurationSeconds")]
        public double PageDurationSeconds { get; set; }

        [DataMember(Name = "timestampText")]
        public string TimestampText { get; set; }

        [DataMember(Name = "pages")]
        public List<ProtocolPageManifestPage> Pages { get; set; }
    }

    [DataContract]
    internal sealed class ProtocolPageManifestPage
    {
        [DataMember(Name = "pageNumber")]
        public int PageNumber { get; set; }

        [DataMember(Name = "fileName")]
        public string FileName { get; set; }

        [DataMember(Name = "startSampleIndex")]
        public long StartSampleIndex { get; set; }

        [DataMember(Name = "sampleCount")]
        public int SampleCount { get; set; }

        [DataMember(Name = "channelCount")]
        public int ChannelCount { get; set; }

        [DataMember(Name = "bytesPerChannel")]
        public int BytesPerChannel { get; set; }

        [DataMember(Name = "leadingHighSamples")]
        public int LeadingHighSamples { get; set; }

        [DataMember(Name = "isActiveData", EmitDefaultValue = false)]
        public bool? IsActiveData { get; set; }

        [DataMember(Name = "startTimeSeconds")]
        public double StartTimeSeconds { get; set; }

        [DataMember(Name = "durationSeconds")]
        public double DurationSeconds { get; set; }

        [DataMember(Name = "startsOnBoundary")]
        public bool StartsOnBoundary { get; set; }

        [DataMember(Name = "endsOnBoundary")]
        public bool EndsOnBoundary { get; set; }
    }

    internal static class ProtocolPageManifestStorage
    {
        public const string ManifestFileName = "manifest.json";

        public static void Save(string folderPath, ProtocolPageManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Folder path must be valid.", "folderPath");
            }

            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            Directory.CreateDirectory(folderPath);
            string manifestPath = Path.Combine(folderPath, ManifestFileName);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProtocolPageManifest));
            using (FileStream stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, manifest);
            }
        }

        public static bool TryLoad(string folderPath, out ProtocolPageManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(folderPath) || Directory.Exists(folderPath) == false)
            {
                return false;
            }

            string manifestPath = Path.Combine(folderPath, ManifestFileName);
            if (File.Exists(manifestPath) == false)
            {
                return false;
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProtocolPageManifest));
                using (FileStream stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    manifest = serializer.ReadObject(stream) as ProtocolPageManifest;
                }
            }
            catch
            {
                manifest = null;
            }

            return manifest != null
                && manifest.Pages != null
                && manifest.Pages.Count > 0
                && manifest.SampleRate > 0
                && manifest.LineCount > 0;
        }
    }

    internal static class ProtocolPageUtility
    {
        public static int GetPackedByteCountForSamples(int sampleCount)
        {
            if (sampleCount <= 0)
            {
                return 0;
            }

            return (sampleCount + 7) / 8;
        }

        public static byte[][] SplitInterleavedPackedBytes(byte[] interleavedBytes, int channelCount)
        {
            if (interleavedBytes == null || channelCount <= 0 || interleavedBytes.Length == 0 || interleavedBytes.Length % channelCount != 0)
            {
                return null;
            }

            int bytesPerChannel = interleavedBytes.Length / channelCount;
            byte[][] channels = new byte[channelCount][];
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                channels[channelIndex] = new byte[bytesPerChannel];
            }

            int sourceIndex = 0;
            for (int sampleByteIndex = 0; sampleByteIndex < bytesPerChannel; sampleByteIndex++)
            {
                for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    channels[channelIndex][sampleByteIndex] = interleavedBytes[sourceIndex++];
                }
            }

            return channels;
        }

        public static byte[] CombineChannelBytes(byte[][] channelBytes)
        {
            if (channelBytes == null || channelBytes.Length == 0)
            {
                return new byte[0];
            }

            int totalLength = 0;
            for (int i = 0; i < channelBytes.Length; i++)
            {
                if (channelBytes[i] != null)
                {
                    totalLength += channelBytes[i].Length;
                }
            }

            byte[] combinedBytes = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < channelBytes.Length; i++)
            {
                byte[] currentChannelBytes = channelBytes[i];
                if (currentChannelBytes == null || currentChannelBytes.Length == 0)
                {
                    continue;
                }

                Buffer.BlockCopy(currentChannelBytes, 0, combinedBytes, offset, currentChannelBytes.Length);
                offset += currentChannelBytes.Length;
            }

            return combinedBytes;
        }

        public static byte[] ExtractPackedBits(byte[] sourceBytes, long startBitIndex, int bitCount)
        {
            if (sourceBytes == null || sourceBytes.Length == 0 || bitCount <= 0)
            {
                return new byte[0];
            }

            if (startBitIndex < 0)
            {
                throw new ArgumentOutOfRangeException("startBitIndex");
            }

            byte[] outputBytes = new byte[GetPackedByteCountForSamples(bitCount)];
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                long sourceBitIndex = startBitIndex + bitIndex;
                int sourceByteIndex = (int)(sourceBitIndex / 8);
                if (sourceByteIndex < 0 || sourceByteIndex >= sourceBytes.Length)
                {
                    break;
                }

                int sourceBitOffset = 7 - (int)(sourceBitIndex % 8);
                if (((sourceBytes[sourceByteIndex] >> sourceBitOffset) & 0x1) == 0)
                {
                    continue;
                }

                int destinationByteIndex = bitIndex / 8;
                int destinationBitOffset = 7 - (bitIndex % 8);
                outputBytes[destinationByteIndex] |= (byte)(1 << destinationBitOffset);
            }

            return outputBytes;
        }

        public static List<ProtocolPageManifestPage> BuildFixedWidthPages(
            int totalSamples,
            int samplesPerSegment,
            uint sampleRate,
            double pageDurationSeconds,
            int channelCount)
        {
            if (totalSamples <= 0 || samplesPerSegment <= 0 || sampleRate == 0 || channelCount <= 0)
            {
                return new List<ProtocolPageManifestPage>();
            }

            List<long> pageEndCandidates = new List<long>();
            for (long pageEnd = samplesPerSegment; pageEnd <= totalSamples; pageEnd += samplesPerSegment)
            {
                pageEndCandidates.Add(pageEnd);
            }

            return BuildAlignedPages(totalSamples, pageEndCandidates, sampleRate, pageDurationSeconds, channelCount, 0);
        }

        public static List<ProtocolPageManifestPage> BuildUartPages(
            int totalSamples,
            int samplesPerBit,
            int frameSamples,
            int payloadByteCount,
            uint sampleRate)
        {
            return BuildUartPages(totalSamples, samplesPerBit, frameSamples, payloadByteCount, sampleRate, 0.1);
        }

        public static List<ProtocolPageManifestPage> BuildUartPages(
            int totalSamples,
            int samplesPerBit,
            int frameSamples,
            int payloadByteCount,
            uint sampleRate,
            double pageDurationSeconds)
        {
            if (totalSamples <= 0 || samplesPerBit <= 0 || frameSamples <= 0 || payloadByteCount <= 0 || sampleRate == 0)
            {
                return new List<ProtocolPageManifestPage>();
            }

            List<long> pageEndCandidates = new List<long>();
            for (int frameIndex = 1; frameIndex < payloadByteCount; frameIndex++)
            {
                pageEndCandidates.Add(samplesPerBit + ((long)frameIndex * frameSamples));
            }

            pageEndCandidates.Add(totalSamples);
            return BuildAlignedPages(totalSamples, pageEndCandidates, sampleRate, pageDurationSeconds, 1, samplesPerBit);
        }

        private static List<ProtocolPageManifestPage> BuildAlignedPages(
            int totalSamples,
            List<long> pageEndCandidates,
            uint sampleRate,
            double pageDurationSeconds,
            int channelCount,
            int leadingHighSamples)
        {
            List<ProtocolPageManifestPage> pages = new List<ProtocolPageManifestPage>();
            if (totalSamples <= 0 || pageEndCandidates == null || pageEndCandidates.Count == 0 || sampleRate == 0 || channelCount <= 0)
            {
                return pages;
            }

            long targetSamples = Math.Max(1L, (long)Math.Round(pageDurationSeconds * sampleRate, MidpointRounding.AwayFromZero));
            long pageStart = 0;
            int pageNumber = 1;
            int candidateIndex = 0;
            while (pageStart < totalSamples)
            {
                while (candidateIndex < pageEndCandidates.Count && pageEndCandidates[candidateIndex] <= pageStart)
                {
                    candidateIndex++;
                }

                if (candidateIndex >= pageEndCandidates.Count)
                {
                    break;
                }

                long targetEnd = pageStart + targetSamples;
                long selectedEnd = 0;
                for (int i = candidateIndex; i < pageEndCandidates.Count; i++)
                {
                    long candidate = pageEndCandidates[i];
                    if (candidate <= pageStart)
                    {
                        continue;
                    }

                    if (candidate <= targetEnd)
                    {
                        selectedEnd = candidate;
                        continue;
                    }

                    if (selectedEnd <= pageStart)
                    {
                        selectedEnd = candidate;
                    }

                    break;
                }

                if (selectedEnd <= pageStart)
                {
                    selectedEnd = pageEndCandidates[candidateIndex];
                }

                if (selectedEnd > totalSamples)
                {
                    selectedEnd = totalSamples;
                }

                int sampleCount = checked((int)(selectedEnd - pageStart));
                int bytesPerChannel = GetPackedByteCountForSamples(sampleCount);
                pages.Add(new ProtocolPageManifestPage
                {
                    PageNumber = pageNumber,
                    FileName = ProtocolBinNaming.BuildPageFileName(pageNumber),
                    StartSampleIndex = pageStart,
                    SampleCount = sampleCount,
                    ChannelCount = channelCount,
                    BytesPerChannel = bytesPerChannel,
                    LeadingHighSamples = pageStart > 0 ? leadingHighSamples : 0,
                    StartTimeSeconds = pageStart / (double)sampleRate,
                    DurationSeconds = sampleCount / (double)sampleRate,
                    StartsOnBoundary = true,
                    EndsOnBoundary = true
                });

                pageStart = selectedEnd;
                pageNumber++;
            }

            return pages;
        }
    }
}

