using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WildToys.Pages;

namespace WildToys;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        ContentFrame.Navigate(typeof(GeneralPage));
        NavView.SelectedItem = NavGeneral;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
            return;

        Type? page = tag switch
        {
            "GeneralPage" => typeof(GeneralPage),
            "MouseWarpPage" => typeof(MouseWarpPage),
            "PowerSwitcherPage" => typeof(PowerSwitcherPage),
            "LumaEdgesPage" => typeof(LumaEdgesPage),
            "MouseGesturePage" => typeof(MouseGesturePage),
            _ => null,
        };

        if (page is not null && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }
}
