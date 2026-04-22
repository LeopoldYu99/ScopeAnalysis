using System;
using System.Collections.Generic;
using System.Globalization;

namespace InteractiveExamples
{
    internal sealed class ProtocolBinFolderMetadata
    {
        public int LineCount { get; set; }
        public uint SampleRate { get; set; }
        public string TimestampText { get; set; }
    }

    internal sealed class ProtocolBinChunkFileMetadata
    {
        public int FilePageNumber { get; set; }
        public HashSet<int> ActivePartitions { get; set; }
    }

    internal static class ProtocolBinNaming
    {
        public static string BuildExportFolderName(int lineCount, uint sampleRate, DateTime exportTimestamp)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};",
                lineCount,
                sampleRate,
                BuildTimestampText(exportTimestamp));
        }

        public static string BuildExportChunkFileName(int filePageNumber, string activePartitionList)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}.bin",
                filePageNumber,
                NormalizeActivePartitionList(activePartitionList));
        }

        public static bool TryParseFolderMetadata(string folderPathOrName, out ProtocolBinFolderMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(folderPathOrName))
            {
                return false;
            }

            string folderName = folderPathOrName;
            try
            {
                folderName = System.IO.Path.GetFileName(folderPathOrName.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }
            catch
            {
                folderName = folderPathOrName;
            }

            if (string.IsNullOrWhiteSpace(folderName))
            {
                return false;
            }

            string[] parts = folderName.Split(';');
            if (parts.Length < 3)
            {
                return false;
            }

            int lineCount;
            uint sampleRate;
            string timestampText = parts[2];
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lineCount) == false
                || lineCount <= 0
                || uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate) == false
                || sampleRate == 0
                || string.IsNullOrWhiteSpace(timestampText))
            {
                return false;
            }

            metadata = new ProtocolBinFolderMetadata
            {
                LineCount = lineCount,
                SampleRate = sampleRate,
                TimestampText = timestampText
            };
            return true;
        }

        public static bool TryParseChunkFileMetadata(string fileNameWithoutExtension, out ProtocolBinChunkFileMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return false;
            }

            int separatorIndex = fileNameWithoutExtension.IndexOf('-');
            string pageNumberText;
            string activePartitionList;
            if (separatorIndex < 0)
            {
                pageNumberText = fileNameWithoutExtension;
                activePartitionList = string.Empty;
            }
            else
            {
                pageNumberText = fileNameWithoutExtension.Substring(0, separatorIndex);
                activePartitionList = separatorIndex >= fileNameWithoutExtension.Length - 1
                    ? string.Empty
                    : fileNameWithoutExtension.Substring(separatorIndex + 1);
            }

            int filePageNumber;
            if (int.TryParse(pageNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out filePageNumber) == false
                || filePageNumber <= 0)
            {
                return false;
            }

            metadata = new ProtocolBinChunkFileMetadata
            {
                FilePageNumber = filePageNumber,
                ActivePartitions = ParseActivePartitions(activePartitionList)
            };
            return true;
        }

        private static string BuildTimestampText(DateTime exportTimestamp)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}_{2}_{3}_{4}_{5}_{6}",
                exportTimestamp.Year,
                exportTimestamp.Month,
                exportTimestamp.Day,
                exportTimestamp.Hour,
                exportTimestamp.Minute,
                exportTimestamp.Second,
                exportTimestamp.Millisecond);
        }

        private static string NormalizeActivePartitionList(string activePartitionList)
        {
            return string.IsNullOrWhiteSpace(activePartitionList)
                ? string.Empty
                : activePartitionList.Trim();
        }

        private static HashSet<int> ParseActivePartitions(string activePartitionList)
        {
            HashSet<int> activePartitions = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(activePartitionList))
            {
                return activePartitions;
            }

            string[] partitionTokens = activePartitionList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in partitionTokens)
            {
                int partitionNumber;
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out partitionNumber) && partitionNumber > 0)
                {
                    activePartitions.Add(partitionNumber);
                }
            }

            return activePartitions;
        }
    }
}
