using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.ComponentModel;

namespace HW_Update_Extractor
{

    // Class for all partitions
    public class Partition
    {
        public string Type { get; set; }
        public long Size { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public long DataOffset { get; set; }
        public string FilePath { get; set; }
    }

    public partial class MainWindow : Window
    {
        // Constants
        private const uint MAGIC = 0xA55AAA55;
        private const int CHUNK_SIZE = 0x400;
        private const int ALIGNMENT = 4;
        private List<Partition> partitions = new List<Partition>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            // UPDATE.APP picker
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "UPDATE.APP files|UPDATE.APP|All files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
        {
            // Output picker
            var folderDialog = new FolderBrowserDialog();
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputDirTextBox.Text = folderDialog.SelectedPath;
            }
        }

        // Load UPDATE.APP to GUI
        private void LoadUpdateApp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select an UPDATE.APP file first.");
                return;
            }

            try
            {
                ParsePartitions(FilePathTextBox.Text);
                PartitionsListView.ItemsSource = partitions;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading UPDATE.APP: {ex.Message}");
            }
        }

        // Get partition data
        private void ParsePartitions(string filePath)
        {
            partitions.Clear();
            using (var file = new BinaryReader(File.OpenRead(filePath)))
            {
                while (file.BaseStream.Position < file.BaseStream.Length)
                {
                    var buffer = new byte[4];
                    if (file.Read(buffer, 0, 4) != 4) break;

                    if (BitConverter.ToUInt32(buffer, 0) == MAGIC)
                    {
                        var partition = ReadPartition(file, file.BaseStream.Position - 4, filePath);
                        if (partition != null)
                        {
                            partitions.Add(partition);
                        }
                    }
                }
            }
        }

        // Process partition
        private Partition ReadPartition(BinaryReader file, long startPosition, string filePath)
        {
            try
            {
                var headerSize = file.ReadUInt32();
                var unknown1 = file.ReadUInt32();
                var hardwareId = file.ReadUInt64();
                var sequence = file.ReadUInt32();
                var size = file.ReadUInt32();

                var date = ReadNullTerminatedString(file, 16);
                var time = ReadNullTerminatedString(file, 16);
                var type = ReadNullTerminatedString(file, 16);

                var remainingHeaderSize = headerSize - 98;
                file.BaseStream.Seek(remainingHeaderSize + 22, SeekOrigin.Current);

                long dataOffset = file.BaseStream.Position;

                file.BaseStream.Seek(size, SeekOrigin.Current);

                var alignment = (ALIGNMENT - file.BaseStream.Position % ALIGNMENT) % ALIGNMENT;
                file.BaseStream.Seek(alignment, SeekOrigin.Current);

                return new Partition
                {
                    Type = type,
                    Size = size,
                    Start = $"0x{startPosition:X}",
                    End = $"0x{file.BaseStream.Position:X}",
                    Date = date,
                    Time = time,
                    DataOffset = dataOffset,
                    FilePath = filePath
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The function name says it lol
        private string ReadNullTerminatedString(BinaryReader reader, int maxLength)
        {
            var bytes = reader.ReadBytes(maxLength);
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = maxLength;
            return System.Text.Encoding.ASCII.GetString(bytes, 0, length);
        }

        private void ExtractPartitions_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputDirTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory first.");
                return;
            }

            try
            {
                var outputDir = OutputDirTextBox.Text;
                Directory.CreateDirectory(outputDir);
                var filter = PartitionFilterTextBox.Text;

                foreach (var partition in partitions)
                {
                    if (string.IsNullOrEmpty(filter) || partition.Type == filter)
                    {
                        var outputPath = Path.Combine(outputDir, $"{partition.Type}.img");
                        ExtractPartitionToFile(partition, outputPath);
                    }
                }

                System.Windows.MessageBox.Show("Partitions extracted successfully!");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error extracting partitions: {ex.Message}");
            }
        }

        // Export the partitions
        private void ExtractPartitionToFile(Partition partition, string outputPath)
        {
            const int bufferSize = 1024 * 1024;
            using (var inputFile = new FileStream(partition.FilePath, FileMode.Open, FileAccess.Read))
            using (var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                inputFile.Seek(partition.DataOffset, SeekOrigin.Begin);
                var remainingBytes = partition.Size;
                var buffer = new byte[bufferSize];

                while (remainingBytes > 0)
                {
                    var bytesToRead = (int)Math.Min(bufferSize, remainingBytes);
                    var bytesRead = inputFile.Read(buffer, 0, bytesToRead);
                    if (bytesRead == 0) break;

                    outputFile.Write(buffer, 0, bytesRead);
                    remainingBytes -= bytesRead;
                }
            }
        }
    }
}