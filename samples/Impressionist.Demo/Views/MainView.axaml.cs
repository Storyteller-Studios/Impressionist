using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Impressionist.Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;

namespace Impressionist.Demo.Views
{
    public partial class MainView : UserControl
    {

        public MainView()
        {
            InitializeComponent();

            
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, DropArea_OnDrop);
            AddHandler(DragDrop.DragOverEvent, DropArea_OnDragOver);
        }

        private async void OpenImageButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }
            var topLevel = TopLevel.GetTopLevel(this)!;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "选择图片",
                FileTypeFilter =
                [
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                    }
                ]
            });

            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            viewModel.Image = new Avalonia.Media.Imaging.Bitmap(stream);
        }

        private void DropArea_OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void DropArea_OnDrop(object? sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var file = e.DataTransfer.TryGetFile() as IStorageFile;
            if (file is null)
            {
                return;
            }
            if (file.Name.EndsWith(".glsl")){
                using var reader = new StreamReader(await file.OpenReadAsync());
                viewModel.ShaderCode = await reader.ReadToEndAsync();
                return;
            }
            await using var stream = await file.OpenReadAsync();
            viewModel.Image = new Avalonia.Media.Imaging.Bitmap(stream);
        }
    }
}