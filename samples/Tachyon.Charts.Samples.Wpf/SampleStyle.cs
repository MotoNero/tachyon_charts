using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace Tachyon.Charts.Samples.Wpf;

/// <summary>The look of the showroom: a dark shell with one accent color.</summary>
internal static class SampleStyle
{
    private const int DarkTitleBar = 20;

    private const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="Ink" Color="#E8EDF4"/>
  <SolidColorBrush x:Key="Muted" Color="#8B97A8"/>
  <SolidColorBrush x:Key="Line" Color="#262E3A"/>
  <SolidColorBrush x:Key="Panel" Color="#161B23"/>
  <SolidColorBrush x:Key="Raised" Color="#1E2530"/>
  <LinearGradientBrush x:Key="Accent" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#38BDF8" Offset="0"/>
    <GradientStop Color="#6366F1" Offset="1"/>
  </LinearGradientBrush>
  <LinearGradientBrush x:Key="Shell" StartPoint="0,0" EndPoint="1,1">
    <GradientStop Color="#0F1319" Offset="0"/>
    <GradientStop Color="#0B0E13" Offset="1"/>
  </LinearGradientBrush>
  <LinearGradientBrush x:Key="Sidebar" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#171D26" Offset="0"/>
    <GradientStop Color="#10141B" Offset="1"/>
  </LinearGradientBrush>

  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Raised}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="Padding" Value="12,6"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Box" CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Box" Property="Background" Value="#2A3342"/>
              <Setter TargetName="Box" Property="BorderBrush" Value="#38BDF8"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Box" Property="Background" Value="{StaticResource Accent}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key="ComboToggle" TargetType="ToggleButton">
    <Border x:Name="Box" CornerRadius="7" Background="{StaticResource Raised}" BorderBrush="{StaticResource Line}" BorderThickness="1">
      <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0" Data="M0,0 L4,4 L8,0" Stroke="{StaticResource Muted}" StrokeThickness="1.5"/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#38BDF8"/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>

  <Style TargetType="TabControl">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="0"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TabControl">
          <DockPanel>
            <Border DockPanel.Dock="Top" BorderBrush="{StaticResource Line}" BorderThickness="0,0,0,1" Padding="8,0,8,0">
              <TabPanel IsItemsHost="True"/>
            </Border>
            <ContentPresenter ContentSource="SelectedContent"/>
          </DockPanel>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="TabItem">
    <Setter Property="Foreground" Value="{StaticResource Muted}"/>
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TabItem">
          <Grid Margin="0,0,4,0">
            <Border x:Name="Box" CornerRadius="7,7,0,0" Padding="14,7" Background="Transparent">
              <ContentPresenter ContentSource="Header" VerticalAlignment="Center"/>
            </Border>
            <Border x:Name="Bar" Height="2" VerticalAlignment="Bottom" CornerRadius="1" Background="{StaticResource Accent}" Visibility="Hidden"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter Property="Foreground" Value="{StaticResource Ink}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter Property="Foreground" Value="{StaticResource Ink}"/>
              <Setter TargetName="Box" Property="Background" Value="{StaticResource Raised}"/>
              <Setter TargetName="Bar" Property="Visibility" Value="Visible"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="Box" CornerRadius="5" Padding="10,5" Margin="3,1"><ContentPresenter/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Box" Property="Background" Value="#2A3342"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="MinWidth" Value="130"/>
    <Setter Property="Height" Value="32"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Template="{StaticResource ComboToggle}" Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" ClickMode="Press"/>
            <ContentPresenter IsHitTestVisible="False" Margin="12,0,26,0" VerticalAlignment="Center" Content="{TemplateBinding SelectionBoxItem}"/>
            <Popup Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
              <Border Margin="0,4,0,0" CornerRadius="8" Background="{StaticResource Raised}" BorderBrush="{StaticResource Line}" BorderThickness="1" Padding="2,4" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="320">
                <ScrollViewer><ItemsPresenter/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ContextMenu">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ContextMenu">
          <Border CornerRadius="8" Background="{StaticResource Raised}" BorderBrush="{StaticResource Line}" BorderThickness="1" Padding="4"><ItemsPresenter/></Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="MenuItem">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Border x:Name="Box" CornerRadius="5" Padding="12,6"><ContentPresenter ContentSource="Header"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Box" Property="Background" Value="#2A3342"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ListBox">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
    <Setter Property="KeyboardNavigation.TabNavigation" Value="Once"/>
    <Setter Property="VirtualizingPanel.IsVirtualizing" Value="False"/>
  </Style>

  <Style TargetType="ListBoxItem">
    <Setter Property="Foreground" Value="#C4CDD9"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Grid Margin="0,1">
            <Border x:Name="Box" CornerRadius="7" Padding="14,7"><ContentPresenter/></Border>
            <Border x:Name="Mark" Width="3" CornerRadius="2" Margin="0,7" HorizontalAlignment="Left" Background="{StaticResource Accent}" Visibility="Collapsed"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="Background" Value="#1E2530"/></Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Box" Property="Background" Value="#242D3A"/>
              <Setter TargetName="Mark" Property="Visibility" Value="Visible"/>
              <Setter Property="Foreground" Value="White"/>
            </Trigger>
            <DataTrigger Binding="{Binding IsHeader}" Value="True">
              <Setter Property="IsHitTestVisible" Value="False"/>
              <Setter Property="Focusable" Value="False"/>
              <Setter Property="Foreground" Value="#5F6C7E"/>
              <Setter Property="FontSize" Value="11"/>
              <Setter Property="FontWeight" Value="SemiBold"/>
              <Setter TargetName="Box" Property="Padding" Value="14,14,14,4"/>
            </DataTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="8"/>
    <Setter Property="MinWidth" Value="8"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Track x:Name="PART_Track" IsDirectionReversed="True">
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType="Thumb"><Border CornerRadius="4" Background="#343E4D" Margin="1"/></ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
""";

    /// <summary>Loads the styles.</summary>
    /// <param name="application">The application.</param>
    public static void Apply(Application application) =>
        application.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Xaml));

    /// <summary>Darkens the title bar of a window.</summary>
    /// <param name="window">The window.</param>
    public static void DarkenTitleBar(Window window)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, DarkTitleBar, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
