﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnumerableAsyncProcessor.Extensions;
using FrameExtractor;
using FrameExtractor.Extensions;
using Frituquim.Helpers;
using Frituquim.Models;
using Wpf.Ui;
using Wpf.Ui.Controls;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Frituquim.ViewModels
{
    public record ExecutionMessage(string Title, string Message);

    public record ExecutionMessageMap(ExecutionMessage Success, ExecutionMessage Error);

    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty] private string _videoPathOrUrl = "https://www.youtube.com/watch?v=vaphaFCyLQI";
        
        [ObservableProperty] private string? _inputDirectory;
        
        [ObservableProperty] private string _inputFilter = "*.MOV";
        
        [ObservableProperty] private bool _inputIncludeSubDirectories = true;
        
        [ObservableProperty] private ConversionType _conversionType = ConversionType.Mp4;
        
        [ObservableProperty] private ConversionHardware _conversionHardware = ConversionHardware.Cpu;
        
        

        [ObservableProperty]
        private string _outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLoadingVisibility))]
        [NotifyPropertyChangedFor(nameof(IsLoading))]
        private bool _isExtractButtonEnabled = true;

        [ObservableProperty] private bool _createSubfolders = true;

        [ObservableProperty] private bool _openFolderAfterExecution = true;

        [ObservableProperty] private TimeSpan? _extractionTimeLimit;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowIndeterminateProgress))]
        private double? _currentProgress;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowFrameControls))]
        [NotifyPropertyChangedFor(nameof(ShowUrlInput))]
        [NotifyPropertyChangedFor(nameof(ShowDirectoryInput))]
        [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
        private ExtractionType _selectedExtractionType = ExtractionType.Frames;

        [ObservableProperty] private ICollection<ExtractionType> _extractionTypes = new List<ExtractionType>
        {
            ExtractionType.Frames,
            ExtractionType.Video,
            ExtractionType.Audio,
            ExtractionType.Convert
        };
        
        [ObservableProperty] private ICollection<ConversionHardware> _conversionHardwares = new List<ConversionHardware>
        {
            ConversionHardware.Nvidia,
            ConversionHardware.IntelQuickSync,
            ConversionHardware.Cpu
        };
        public bool ShowFrameControls => SelectedExtractionType == ExtractionType.Frames;

        public bool ShowUrlInput => SelectedExtractionType is ExtractionType.Video or ExtractionType.Audio or ExtractionType.Frames;
        
        public bool ShowDirectoryInput => SelectedExtractionType is ExtractionType.Convert;

        public bool ShowIndeterminateProgress => !CurrentProgress.HasValue;

        private static Dictionary<ExtractionType, ExecutionMessageMap> _messageMap =
            new()
            {
                {
                    ExtractionType.Frames,
                    new ExecutionMessageMap(
                        new ExecutionMessage("Frames extraídos com sucesso!",
                            "Os frames foram extraídos com sucesso e salvos no diretório de saída."),
                        new ExecutionMessage("Erro ao extrair frames!",
                            "Ocorreu um erro ao extrair os frames, tente novamente.")
                    )
                },
                {
                    ExtractionType.Audio,
                    new ExecutionMessageMap(
                        new ExecutionMessage("Audio extraído com sucesso!",
                            "O audio foi extraído com sucesso e salvo no diretório de saída."),
                        new ExecutionMessage("Erro ao extrair audio!",
                            "Ocorreu um erro ao extrair o audio, tente novamente.")
                    )
                },
                {
                    ExtractionType.Video,
                    new ExecutionMessageMap(
                        new ExecutionMessage("Video baixado com sucesso!",
                            "O video foi baixado com sucesso e salvo no diretório de saída."),
                        new ExecutionMessage("Erro ao baixar video!",
                            "Ocorreu um erro ao baixar o video, tente novamente.")
                    )
                },
                {
                    ExtractionType.Convert,
                    new ExecutionMessageMap(
                        new ExecutionMessage("Video convertido com sucesso!",
                            "O video foi convertido com sucesso e salvo no diretório de saída."),
                        new ExecutionMessage("Erro ao converter video!",
                            "Ocorreu um erro ao converter o video, tente novamente.")
                    )
                }
            };

        public DashboardViewModel(ISnackbarService snackbarService)
        {
            SnackbarService = snackbarService;
        }

        public bool IsLoading => !IsExtractButtonEnabled;

        public Visibility IsLoadingVisibility => IsExtractButtonEnabled ? Visibility.Collapsed : Visibility.Visible;

        public string ExecuteButtonText => SelectedExtractionType switch
        {
            ExtractionType.Frames => "Extrair Frames",
            ExtractionType.Video => "Baixar Video",
            ExtractionType.Audio => "Baixar Audio",
            ExtractionType.Convert => "Converter videos",
            _ => "Extrair"
        };

        private ISnackbarService SnackbarService { get; }


        [RelayCommand]
        private void OpenFileDialog()
        {
            var fileDialog = new OpenFileDialog();
            if (fileDialog.ShowDialog() ?? false)
            {
                VideoPathOrUrl = fileDialog.FileName;
            }
        }

        [RelayCommand]
        private void OpenOutputDirectoryDialog()
        {
            var fileDialog = new FolderBrowserDialog();
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                OutputDirectory = fileDialog.SelectedPath;
            }
        }
        
        [RelayCommand]
        private void OpenInputDirectoryDialog()
        {
            var fileDialog = new FolderBrowserDialog();
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                InputDirectory = fileDialog.SelectedPath;
            }
        }

        [RelayCommand]
        private async Task Execute()
        {
            IsExtractButtonEnabled = false;

            try
            {
                if (SelectedExtractionType == ExtractionType.Frames)
                {
                    await ExtractFrames();
                    return;
                }
                
                if (SelectedExtractionType == ExtractionType.Convert)
                {
                    await ConvertVideos();
                    return;
                }

                var fileName = await YtdlpHelper.GetFileName(VideoPathOrUrl, SelectedExtractionType);
                var downloadFilePath = Path.Combine(OutputDirectory, fileName);

                if (File.Exists(downloadFilePath))
                {
                    File.Delete(downloadFilePath);
                }

                var downloadedFile = await YtdlpHelper.CreateYtdlpCommand(VideoPathOrUrl, downloadFilePath,
                        SelectedExtractionType == ExtractionType.Audio
                            ? new[] { "-x", "--audio-format", "mp3" }
                            : Array.Empty<string>())
                    .ExecuteAsync()
                    .Task
                    .ContinueWith(t => t.IsCompletedSuccessfully);

                var messageMap = _messageMap[SelectedExtractionType];

                if (!downloadedFile)
                {
                    SnackbarService.Show(messageMap.Error.Title, messageMap.Error.Message, ControlAppearance.Danger,
                        null, TimeSpan.FromSeconds(3));
                    IsExtractButtonEnabled = true;
                    return;
                }

                SnackbarService.Show(messageMap.Success.Title, messageMap.Success.Message, ControlAppearance.Success,
                    null, TimeSpan.FromSeconds(3));

                if (OpenFolderAfterExecution)
                {
                    System.Diagnostics.Process.Start("explorer", OutputDirectory);
                }
            }
            finally
            {
                IsExtractButtonEnabled = true;
            }
        }

        private async Task ConvertVideos()
        {
            if(InputDirectory == null)
            {
                SnackbarService.Show("Diretório de entrada não selecionado!",
                    "Selecione um diretório de entrada para continuar.", ControlAppearance.Danger, null,
                    TimeSpan.FromSeconds(3));
                IsExtractButtonEnabled = true;
                return;
            }
            
            if(!Directory.Exists(InputDirectory))
            {
                SnackbarService.Show("Diretório de entrada não encontrado!",
                    "O diretório de entrada selecionado não foi encontrado.", ControlAppearance.Danger, null,
                    TimeSpan.FromSeconds(3));
                IsExtractButtonEnabled = true;
                return;
            }
            
            var files = Directory.GetFiles(InputDirectory, InputFilter, InputIncludeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            
            if(files.Length == 0)
            {
                SnackbarService.Show("Nenhum arquivo encontrado!",
                    "Nenhum arquivo foi encontrado no diretório de entrada.", ControlAppearance.Danger, null,
                    TimeSpan.FromSeconds(3));
                IsExtractButtonEnabled = true;
                return;
            }

            await files.ToAsyncProcessorBuilder()
                .ForEachAsync(async file =>
                {
                    var fileName = Path.GetFileName(file);
                    var outputFilePath =
                        Path.Combine(Path.GetDirectoryName(file)!, Path.ChangeExtension(fileName, ".mp4"));

                    if (File.Exists(outputFilePath))
                    {
                        File.Delete(outputFilePath);
                    }

                    var ffmpegCommand = FFmpegHelper.ConvertFile(file, outputFilePath, ConversionType, ConversionHardware);
                    var convertedFile = await ffmpegCommand
                        .ExecuteAsync()
                        .Task
                        .ContinueWith(t => t.IsCompletedSuccessfully);

                    if (!convertedFile)
                    {
                        SnackbarService.Show("Erro ao converter video!",
                            "Ocorreu um erro ao converter o video, tente novamente.", ControlAppearance.Danger, null,
                            TimeSpan.FromSeconds(3));
                        IsExtractButtonEnabled = true;
                    }
                })
                .ProcessInParallel(3);
        }

        private async Task ExtractFrames()
        {
            var isUrl = Uri.IsWellFormedUriString(VideoPathOrUrl, UriKind.Absolute);

            if (isUrl)
            {
                var fileName = await YtdlpHelper.GetFileName(VideoPathOrUrl, ExtractionType.Frames);
                var tempFilePath = Path.Combine(Path.GetTempPath(), fileName);

                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }

                var downloadedVideo = await YtdlpHelper
                    .CreateYtdlpCommand(VideoPathOrUrl, tempFilePath, Array.Empty<string>())
                    .ExecuteAsync()
                    .Task
                    .ContinueWith(t => t.IsCompletedSuccessfully);

                if (!downloadedVideo)
                {
                    SnackbarService.Show("Erro ao baixar o video!",
                        "Ocorreu um erro ao baixar o video, tente novamente.", ControlAppearance.Danger, null,
                        TimeSpan.FromSeconds(3));
                    IsExtractButtonEnabled = true;
                    return;
                }

                SnackbarService.Show("Video baixado com sucesso!",
                    "O video foi baixado com sucesso e a extração de frames será iniciada.", ControlAppearance.Success,
                    null, TimeSpan.FromSeconds(3));
                await ExtractFramesToFolder(tempFilePath);
                File.Delete(tempFilePath);
            }
            else
            {
                await ExtractFramesToFolder(VideoPathOrUrl);
            }
        }

        private async Task ExtractFramesToFolder(string filePath)
        {
            var videoName = Path.GetFileNameWithoutExtension(filePath);
            var basePath = CreateSubfolders ? Path.Combine(OutputDirectory, videoName) : OutputDirectory;

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            var frameExtractionOptions = new FrameExtractionOptions
            {
                TimeLimit = ExtractionTimeLimit,
                FrameFormat = FrameFormat.Png
            };

            await foreach (var frame in FrameExtractionService.Default.GetFrames(filePath,
                               options: frameExtractionOptions, onDurationUpdate: OnDurationUpdate))
            {
                await WriteFrame(frame, basePath);
            }

            CurrentProgress = null;

            if (OpenFolderAfterExecution)
            {
                System.Diagnostics.Process.Start("explorer", basePath);
            }
        }

        private void OnDurationUpdate(TimeSpan maxDuration, TimeSpan currentDuration)
        {
            CurrentProgress = currentDuration.TotalSeconds /
                (ExtractionTimeLimit?.TotalSeconds ?? maxDuration.TotalSeconds) * 100;
        }

        private static async Task WriteFrame(Frame frame, string basePath)
        {
            var outputPath = Path.Combine(basePath,
                $"frame-{frame.Position}{frame.Options.FrameFormat.GetPipeFormat()}");

            await File.WriteAllBytesAsync(outputPath, frame.Data);
        }
    }
}