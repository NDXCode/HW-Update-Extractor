using System.IO;
using System.Windows;
using System.ComponentModel;

namespace HW_Update_Extractor
{
    public class Partition : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        public string Type { get; set; }
        public long Size { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public long DataOffset { get; set; }
        public string FilePath { get; set; }
        public string SourceFileName => Path.GetFileName(FilePath);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private const uint MAGIC = 0xA55AAA55;
        // private const int CHUNK_SIZE = 0x400;
        private const int ALIGNMENT = 4;

        private List<string> loadedFilePaths = new List<string>();
        private List<Partition> partitions = new List<Partition>();

        public MainWindow()
        {
            InitializeComponent();
            PartitionsListView.ItemsSource = partitions;
        }

        // --- File/Directory Selection ---

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "UPDATE.APP files|*.APP|All files|*.*",
                Multiselect = true 
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadFiles(openFileDialog.FileNames);
            }
        }

        private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new FolderBrowserDialog();
            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputDirTextBox.Text = folderDialog.SelectedPath;
            }
        }

        // --- Drag and Drop Handling ---

        private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                LoadFiles(files);
            }
            e.Handled = true;
        }

        // --- Loading Logic ---

        private void LoadFiles(IEnumerable<string> filePaths)
        {
            loadedFilePaths.Clear();
            partitions.Clear();

            loadedFilePaths.AddRange(filePaths);

            if (!loadedFilePaths.Any())
            {
                FilePathTextBox.Text = "No files selected.";
                PartitionsListView.Items.Refresh();
                return;
            }

            if (loadedFilePaths.Count == 1)
            {
                FilePathTextBox.Text = loadedFilePaths[0];
            }
            else
            {
                FilePathTextBox.Text = $"{loadedFilePaths.Count} files loaded.";
            }

            foreach (var filePath in loadedFilePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        ParsePartitions(filePath);
                    }
                    else if (Directory.Exists(filePath))
                    {
                        System.Windows.MessageBox.Show($"Skipping directory: {filePath}", "Skipped Item", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (IOException ioEx)
                {
                    System.Windows.MessageBox.Show($"Error accessing file '{Path.GetFileName(filePath)}': {ioEx.Message}", "File Access Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error loading file '{Path.GetFileName(filePath)}': {ex.Message}", "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            PartitionsListView.Items.Refresh();

            if (!partitions.Any() && loadedFilePaths.Any())
            {
                System.Windows.MessageBox.Show("No valid partitions found in the selected file(s).", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- Partition Parsing ---

        private void ParsePartitions(string filePath)
        {
            using (var file = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                long currentPosition = 0;
                while (currentPosition < file.BaseStream.Length)
                {
                    file.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
                    var buffer = new byte[4];

                    if (file.BaseStream.Length - currentPosition < 4) break;

                    if (file.Read(buffer, 0, 4) != 4) break;

                    if (BitConverter.ToUInt32(buffer, 0) == MAGIC)
                    {
                        long headerStartPosition = currentPosition;
                        var partition = ReadPartition(file, headerStartPosition, filePath);
                        if (partition != null)
                        {
                            partitions.Add(partition);
                            currentPosition = file.BaseStream.Position;
                        }
                        else
                        {
                            currentPosition += 4;
                        }
                    }
                    else
                    {
                        currentPosition++;
                    }
                }
            }
        }

        private Partition ReadPartition(BinaryReader file, long startPosition, string filePath)
        {
            try
            {
                file.BaseStream.Seek(startPosition + 4, SeekOrigin.Begin);

                if (file.BaseStream.Length - file.BaseStream.Position < (4 + 4 + 8 + 4 + 4 + 16 + 16 + 16))
                    return null;

                var headerSize = file.ReadUInt32();
                var unknown1 = file.ReadUInt32();
                var hardwareId = file.ReadUInt64();
                var sequence = file.ReadUInt32();
                var size = file.ReadUInt32(); 

                var date = ReadNullTerminatedString(file, 16);
                var time = ReadNullTerminatedString(file, 16);
                var type = ReadNullTerminatedString(file, 16).Trim(); 

                long currentHeaderRead = (file.BaseStream.Position - (startPosition + 4));
                long remainingHeaderBytesToSkip = (long)headerSize - 4 - currentHeaderRead; 

                if (remainingHeaderBytesToSkip < 0) return null;

                if (file.BaseStream.Length - file.BaseStream.Position < remainingHeaderBytesToSkip + size)
                    return null; 

                file.BaseStream.Seek(remainingHeaderBytesToSkip, SeekOrigin.Current);
                long dataOffset = file.BaseStream.Position; 
                file.BaseStream.Seek(size, SeekOrigin.Current);

                var alignment = (ALIGNMENT - file.BaseStream.Position % ALIGNMENT) % ALIGNMENT;
                if (file.BaseStream.Length - file.BaseStream.Position < alignment)
                    return null;

                file.BaseStream.Seek(alignment, SeekOrigin.Current);

                return new Partition
                {
                    IsSelected = true,
                    Type = string.IsNullOrWhiteSpace(type) ? "UNKNOWN" : type,
                    Size = size,
                    Start = $"0x{dataOffset:X}", 
                    End = $"0x{dataOffset + size:X}", 
                    Date = date,
                    Time = time,
                    DataOffset = dataOffset,
                    FilePath = filePath 
                };
            }
            catch (EndOfStreamException)
            {
                return null;
            }
            catch (Exception ex) 
            {
                return null;
            }
        }


        private string ReadNullTerminatedString(BinaryReader reader, int maxLength)
        {
            var bytes = new List<byte>();
            int count = 0;
            byte b;
            while (count < maxLength && (b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);
                count++;
            }
            if (count < maxLength)
            {
                reader.BaseStream.Seek(maxLength - 1 - count, SeekOrigin.Current);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        // --- Extraction Logic ---

        private void ExtractPartitions_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputDirTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory first.", "Output Directory Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedPartitions = partitions.Where(p => p.IsSelected).ToList();

            if (!selectedPartitions.Any())
            {
                System.Windows.MessageBox.Show("No partitions selected for extraction.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string baseOutputDir = OutputDirTextBox.Text;
                int extractedCount = 0;
                int errorCount = 0;

                foreach (var partition in selectedPartitions)
                {
                    try
                    {
                        string sourceFileName = Path.GetFileName(partition.FilePath);
                        string subDir = Path.Combine(baseOutputDir, sourceFileName);
                        Directory.CreateDirectory(subDir);

                        string safePartitionType = string.Join("_", partition.Type.Split(Path.GetInvalidFileNameChars())); 
                        string outputPath = Path.Combine(subDir, $"{safePartitionType}.img");

                        ExtractPartitionToFile(partition, outputPath);
                        extractedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error extracting partition '{partition.Type}' from '{partition.SourceFileName}': {ex.Message}");
                        errorCount++;
                    }
                }

                string message = $"{extractedCount} partition(s) extracted successfully.";
                if (errorCount > 0)
                {
                    message += $"\n{errorCount} partition(s) failed to extract.";
                }
                System.Windows.MessageBox.Show(message, "Extraction Complete", MessageBoxButton.OK, errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error during extraction process: {ex.Message}", "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtractPartitionToFile(Partition partition, string outputPath)
        {
            const int bufferSize = 1024 * 1024;
            byte[] buffer = new byte[bufferSize];

            try
            {
                using (var inputFile = new FileStream(partition.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var outputFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    inputFile.Seek(partition.DataOffset, SeekOrigin.Begin);
                    long remainingBytes = partition.Size;

                    while (remainingBytes > 0)
                    {
                        int bytesToRead = (int)Math.Min(bufferSize, remainingBytes);
                        int bytesRead = inputFile.Read(buffer, 0, bytesToRead);
                        if (bytesRead == 0)
                        {
                            throw new EndOfStreamException($"Unexpected end of file '{Path.GetFileName(partition.FilePath)}' while reading partition '{partition.Type}'. Expected {partition.Size} bytes, read {partition.Size - remainingBytes}.");
                        }


                        outputFile.Write(buffer, 0, bytesRead);
                        remainingBytes -= bytesRead;
                    }
                }
            }
            catch (IOException ioEx)
            {
                throw new IOException($"IO Error processing partition '{partition.Type}' from '{partition.SourceFileName}' to '{Path.GetFileName(outputPath)}': {ioEx.Message}", ioEx);
            }
        }
    }
}