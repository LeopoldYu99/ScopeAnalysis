using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScopeAnalysis
{
    internal sealed class ProtocolBinFolderMetadata
    {
        public int LineCount { get; set; }
        public uint SampleRate { get; set; }
        public uint DataRate { get; set; }
        public string TimestampText { get; set; }
    }

    internal sealed class ProtocolBinChunkFileMetadata
    {
        public int FilePageNumber { get; set; }
        public HashSet<int> ActivePartitions { get; set; }
    }

    internal sealed class UartBinFileMetadata
    {
        public int LineCount { get; set; }
        public uint SampleRate { get; set; }
        public int BaudRate { get; set; }
        public string ParityText { get; set; }
        public int DataBits { get; set; }
        public double StopBits { get; set; }
        public string TimestampText { get; set; }
        public int FileNumber { get; set; }
    }

    internal static class ProtocolBinNaming
    {
        public static string BuildExportFolderName(int lineCount, uint sampleRate, uint dataRate, DateTime exportTimestamp)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};",
                lineCount,
                sampleRate,
                dataRate,
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

        public static string BuildPageFileName(int filePageNumber)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}.bin",
                Math.Max(1, filePageNumber));
        }

        public static string BuildUartExportFolderName(
            uint sampleRate,
            int baudRate,
            string parityText,
            int dataBits,
            double stopBits,
            DateTime exportTimestamp)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "1;{0};{1};{2};{3};{4};{5}",
                sampleRate,
                baudRate,
                NormalizeFileNameToken(parityText),
                dataBits,
                FormatStopBits(stopBits),
                BuildTimestampText(exportTimestamp));
        }

        public static string BuildUartExportFileName(
            uint sampleRate,
            int baudRate,
            string parityText,
            int dataBits,
            double stopBits,
            DateTime exportTimestamp,
            int fileNumber)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "1;{0};{1};{2};{3};{4};{5}-{6}.bin",
                sampleRate,
                baudRate,
                NormalizeFileNameToken(parityText),
                dataBits,
                FormatStopBits(stopBits),
                BuildTimestampText(exportTimestamp),
                Math.Max(1, fileNumber));
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

            string[] parts = folderName.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            int lineCount;
            uint sampleRate;
            uint dataRate = 0;
            string timestampText = parts.Length >= 4 ? parts[3] : parts[2];
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lineCount) == false
                || lineCount <= 0
                || uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate) == false
                || sampleRate == 0
                || string.IsNullOrWhiteSpace(timestampText))
            {
                return false;
            }

            if (parts.Length >= 4
                && (uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out dataRate) == false || dataRate == 0))
            {
                return false;
            }

            metadata = new ProtocolBinFolderMetadata
            {
                LineCount = lineCount,
                SampleRate = sampleRate,
                DataRate = dataRate,
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

        public static bool TryParseUartFolderMetadata(string folderPathOrName, out UartBinFileMetadata metadata)
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

            string[] parts = folderName.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 7)
            {
                return false;
            }

            int lineCount;
            uint sampleRate;
            int baudRate;
            int dataBits;
            double stopBits;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lineCount) == false
                || lineCount <= 0
                || uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate) == false
                || sampleRate == 0
                || int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out baudRate) == false
                || baudRate <= 0
                || string.IsNullOrWhiteSpace(parts[3])
                || int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out dataBits) == false
                || dataBits <= 0
                || TryParseStopBits(parts[5], out stopBits) == false
                || string.IsNullOrWhiteSpace(parts[6]))
            {
                return false;
            }

            metadata = new UartBinFileMetadata
            {
                LineCount = lineCount,
                SampleRate = sampleRate,
                BaudRate = baudRate,
                ParityText = parts[3].Trim(),
                DataBits = dataBits,
                StopBits = stopBits,
                TimestampText = parts[6].Trim(),
                FileNumber = 0
            };
            return true;
        }

        public static bool TryParseUartFileMetadata(string filePathOrName, out UartBinFileMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(filePathOrName))
            {
                return false;
            }

            string fileNameWithoutExtension;
            try
            {
                fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filePathOrName);
            }
            catch
            {
                fileNameWithoutExtension = filePathOrName;
            }

            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return false;
            }

            string[] parts = fileNameWithoutExtension.Split(new[] { ';' }, StringSplitOptions.None);
            if (parts.Length != 7)
            {
                return false;
            }

            int lineCount;
            uint sampleRate;
            int baudRate;
            int dataBits;
            double stopBits;
            string timestampText;
            int fileNumber;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lineCount) == false
                || lineCount <= 0
                || uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate) == false
                || sampleRate == 0
                || int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out baudRate) == false
                || baudRate <= 0
                || string.IsNullOrWhiteSpace(parts[3])
                || int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out dataBits) == false
                || dataBits <= 0
                || TryParseStopBits(parts[5], out stopBits) == false
                || TryParseTimestampAndFileNumber(parts[6], out timestampText, out fileNumber) == false)
            {
                return false;
            }

            metadata = new UartBinFileMetadata
            {
                LineCount = lineCount,
                SampleRate = sampleRate,
                BaudRate = baudRate,
                ParityText = parts[3].Trim(),
                DataBits = dataBits,
                StopBits = stopBits,
                TimestampText = timestampText,
                FileNumber = fileNumber
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

        private static string FormatStopBits(double stopBits)
        {
            return stopBits.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string NormalizeFileNameToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "None";
            }

            char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
            string normalizedToken = token.Trim();
            for (int i = 0; i < invalidFileNameChars.Length; i++)
            {
                normalizedToken = normalizedToken.Replace(invalidFileNameChars[i], '_');
            }

            return normalizedToken.Length == 0 ? "None" : normalizedToken;
        }

        private static bool TryParseStopBits(string text, out double stopBits)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out stopBits) && stopBits > 0;
        }

        private static bool TryParseTimestampAndFileNumber(string text, out string timestampText, out int fileNumber)
        {
            timestampText = null;
            fileNumber = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int separatorIndex = text.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex >= text.Length - 1)
            {
                return false;
            }

            string parsedTimestampText = text.Substring(0, separatorIndex);
            int parsedFileNumber;
            if (string.IsNullOrWhiteSpace(parsedTimestampText)
                || int.TryParse(text.Substring(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedFileNumber) == false
                || parsedFileNumber <= 0)
            {
                return false;
            }

            timestampText = parsedTimestampText;
            fileNumber = parsedFileNumber;
            return true;
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

