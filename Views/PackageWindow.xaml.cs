using System;
using System.Windows;
using Microsoft.Win32;
using System.IO;
using AffToSpcConverter.Utils;
using AffToSpcConverter.ViewModels;

namespace AffToSpcConverter.Views;

public partial class PackageWindow : Window
{
    private readonly PackageViewModel _vm = new PackageViewModel();

    public PackageWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == true)
        {
            _vm.SourceFilePath = dialog.FileName;
        }
    }

    private void BtnBrowseMapping_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        if (dialog.ShowDialog() == true)
        {
            _vm.MappingJsonPath = dialog.FileName;
        }
    }

    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        // WPF folder browser dialog is tricky without WinForms reference or newer .NET APIs.
        // Assuming user has access to FolderBrowserDialog via System.Windows.Forms or Ookii.
        // Or using OpenFolderDialog if .NET 8 (which supports it on Windows).
        
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
             _vm.OutputDirectory = dialog.FolderName;
        }
    }

    private void BtnPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Status = "Packaging...";
            // Use new Pack signature without key argument
            GameAssetPacker.Pack(_vm.SourceFilePath, _vm.OriginalFilename, _vm.MappingJsonPath, _vm.OutputDirectory);
            _vm.Status = "Success!";
            MessageBox.Show("Packaged successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _vm.Status = $"Error: {ex.Message}";
            MessageBox.Show($"Error packaging assets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
