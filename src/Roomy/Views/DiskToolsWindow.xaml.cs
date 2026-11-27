using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Roomy.Core;

namespace Roomy.Views;

public partial class DiskToolsWindow : Window
{
    public DiskToolsWindow(IEnumerable<string> roots, bool demo)
    {
        InitializeComponent();
        Volume.ItemsSource = roots.ToArray();
        Volume.SelectedIndex = 0;
        CheckButton.IsEnabled = !demo && Volume.Items.Count > 0;
        HealthButton.IsEnabled = OptimizeButton.IsEnabled = MemoryButton.IsEnabled = !demo;
        if (demo) StatusText.Text = "Демонстрационный режим.";
    }

    void Check_Click(object sender, RoutedEventArgs e)
    {
        if (DiskTools.VolumeCheck(Volume.SelectedItem as string) is { } info) Open(info);
    }

    void Health_Click(object sender, RoutedEventArgs e) => Open(DiskTools.StorageHealth());
    void Optimize_Click(object sender, RoutedEventArgs e) => Open(DiskTools.Optimize());
    void Memory_Click(object sender, RoutedEventArgs e) => Open(DiskTools.Memory());

    void Open(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            StatusText.Text = "";
        }
        catch (Win32Exception ex)
        {
            StatusText.Text = ex.NativeErrorCode == 1223 ? "Запуск отменён." : "Не удалось открыть средство Windows. " + ex.Message;
        }
    }
}
