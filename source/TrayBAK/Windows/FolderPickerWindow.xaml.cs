using System.Windows;
using System.Windows.Controls;
using TrayBAK.ViewModels;

namespace TrayBAK.Windows;

public partial class FolderPickerWindow : Window
{
    private readonly FileNode _root;

    public List<string> ExcludedPaths { get; private set; } = new();

    public FolderPickerWindow(string sourceFolder, IEnumerable<string> excludedPaths)
    {
        InitializeComponent();
        SourceText.Text = sourceFolder;
        _root = FileNode.CreateRoot(sourceFolder, excludedPaths);
        _root.EnsureLoaded();
        Tree.ItemsSource = _root.Children;
    }

    private void Tree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileNode node })
            node.EnsureLoaded();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _root.SetAll(true);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _root.SetAll(false);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_root.IsChecked == false)
        {
            MessageBox.Show(this, "Nothing is ticked. Tick at least one file or folder to back up.", "TrayBAK",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ExcludedPaths = _root.CollectExcluded();
        DialogResult = true;
    }
}
