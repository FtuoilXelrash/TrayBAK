using System.Reflection;
using System.Windows;

namespace TrayBAK.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        VersionText.Text = version == null ? "" : $"Version {version.Major}.{version.Minor}.{version.Build}";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        CopyrightText.Text = copyright ?? "Ftuoil Xelrash";
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        App.Instance.OpenHelp();
    }
}
