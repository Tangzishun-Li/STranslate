using STranslate.Core;
using STranslate.ViewModels.Pages;
using System.Windows;

namespace STranslate.Views.Pages;

public partial class AboutPage
{
    public AboutPage(AboutViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }

    public AboutViewModel ViewModel { get; }

    private void OnRepoCopy(object sender, RoutedEventArgs e)
        => Utilities.SetText(RepoTextBox.Text);

    private void OnReportRequest(object sender, RoutedEventArgs e)
    {
        var url = "https://github.com/zggsong/stranslate/issues/new/choose";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnWebsiteRequest(object sender, RoutedEventArgs e)
    {
        var url = "https://stranslate.zggsong.com";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}