using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;

namespace VRAMOptimizerApp {
    public class VRAMOptimizerWindow {
        public Window Window { get; private set; }

        private bool isDarkMode = true;
        private bool isUpdatingControls = false;

        private Button btnThemeToggle;
        private TextBlock lblCard1Title;
        private TextBlock lblLiveVramBudget;
        private TextBlock lblLiveVramGain;
        private TextBlock lblLiveWddmReserve;
        private TextBlock lblLiveWddmState;
        private TextBlock lblLiveSharedRam;
        private TextBlock lblLiveSharedGb;
        private ComboBox cmbGpuSelector;
        private TextBlock lblLiveTotalPool;

        private Button btnPresetRecommended;
        private Button btnPresetAggressive;
        private Button btnPresetDefault;
        private Button btnExportBackup;
        private Button btnRestoreBackup;

        // Sliders & Live Badges
        private TextBlock lblFragValue;
        private Slider sldFragBuffer;
        private TextBlock lblProcValue;
        private Slider sldProcCap;
        private TextBlock lblSharedValue;
        private Slider sldSharedRam;
        private ComboBox cmbHags;

        // Benchmark Controls
        private TextBlock lblBenchGpuName;
        private TextBlock lblBenchCard1Title;
        private TextBlock lblBenchDedicated;
        private TextBlock lblBenchCard2Title;
        private TextBlock lblBenchShared;
        private TextBlock lblBenchCard3Title;
        private TextBlock lblBenchCombined;
        private Button btnRunVramTest;
        private Button btnStopVramTest;
        private ProgressBar pbVramTest;
        private TextBox txtTestLog;

        private Button btnRefresh;
        private Button btnApply;
        private Button btnReboot;
        private TextBlock txtActionNotice;

        // Multi-GPU State & Hardware Telemetry
        private List<VramBenchmark.GpuAdapterInfo> detectedGpus = new List<VramBenchmark.GpuAdapterInfo>();
        private VramBenchmark.GpuAdapterInfo selectedGpu = null;
        private double totalRamGb = 32.0;

        public VRAMOptimizerWindow() {
            string xaml = @"
<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Windows WDDM &amp; VRAM Optimizer v1.2"" Height=""760"" Width=""920""
        MinHeight=""620"" MinWidth=""820""
        WindowStartupLocation=""CenterScreen"" ResizeMode=""CanResize""
        Background=""{DynamicResource BgBrush}"" Foreground=""{DynamicResource TextBrush}"" FontFamily=""Segoe UI"">
    
    <Window.Resources>
        <SolidColorBrush x:Key=""BgBrush"" Color=""#0B1120""/>
        <SolidColorBrush x:Key=""CardBrush"" Color=""#1E293B""/>
        <SolidColorBrush x:Key=""CardBorderBrush"" Color=""#334155""/>
        <SolidColorBrush x:Key=""TextBrush"" Color=""#F8FAFC""/>
        <SolidColorBrush x:Key=""SubTextBrush"" Color=""#94A3B8""/>
        <SolidColorBrush x:Key=""HeaderBrush"" Color=""#FFFFFF""/>
        <SolidColorBrush x:Key=""InputBgBrush"" Color=""#0F172A""/>
        <SolidColorBrush x:Key=""InputFgBrush"" Color=""#F8FAFC""/>
        <SolidColorBrush x:Key=""InputBorderBrush"" Color=""#475569""/>
        <SolidColorBrush x:Key=""ConsoleBgBrush"" Color=""#020617""/>
        <SolidColorBrush x:Key=""ConsoleFgBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""CardTagDxgiBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""CardTagWddmBrush"" Color=""#818CF8""/>
        <SolidColorBrush x:Key=""CardTagSharedBrush"" Color=""#34D399""/>
        <SolidColorBrush x:Key=""CardTagGpuBrush"" Color=""#FBBF24""/>
        <SolidColorBrush x:Key=""SectionTitleBrush"" Color=""#38BDF8""/>
        <SolidColorBrush x:Key=""AccentGreenBrush"" Color=""#4ADE80""/>
        <SolidColorBrush x:Key=""SliderTrackBrush"" Color=""#334155""/>
        <SolidColorBrush x:Key=""SliderThumbBrush"" Color=""#38BDF8""/>

        <!-- Button Style -->
        <Style TargetType=""Button"">
            <Setter Property=""Padding"" Value=""14,8""/>
            <Setter Property=""FontWeight"" Value=""SemiBold""/>
            <Setter Property=""BorderThickness"" Value=""0""/>
            <Setter Property=""Cursor"" Value=""Hand""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Button"">
                        <Border x:Name=""border"" Background=""{TemplateBinding Background}"" CornerRadius=""6"" Padding=""{TemplateBinding Padding}"">
                            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsMouseOver"" Value=""True"">
                                <Setter Property=""Opacity"" Value=""0.88""/>
                            </Trigger>
                            <Trigger Property=""IsPressed"" Value=""True"">
                                <Setter Property=""Opacity"" Value=""0.75""/>
                            </Trigger>
                            <Trigger Property=""IsEnabled"" Value=""False"">
                                <Setter Property=""Opacity"" Value=""0.45""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Thumb Style -->
        <Style x:Key=""SliderThumbStyle"" TargetType=""Thumb"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Thumb"">
                        <Ellipse Width=""18"" Height=""18"" Fill=""{DynamicResource SliderThumbBrush}"" Stroke=""#FFFFFF"" StrokeThickness=""2"" Cursor=""Hand""/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Slider Template Style -->
        <Style TargetType=""Slider"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
            <Setter Property=""VerticalAlignment"" Value=""Center""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""Slider"">
                        <Grid VerticalAlignment=""Center"">
                            <Border Height=""6"" Background=""{DynamicResource SliderTrackBrush}"" CornerRadius=""3"" VerticalAlignment=""Center""/>
                            <Track x:Name=""PART_Track"">
                                <Track.Thumb>
                                    <Thumb Style=""{StaticResource SliderThumbStyle}""/>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBoxItem Style -->
        <Style TargetType=""ComboBoxItem"">
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""Padding"" Value=""10,6""/>
            <Setter Property=""HorizontalContentAlignment"" Value=""Left""/>
            <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
            <Setter Property=""Background"" Value=""Transparent""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource InputFgBrush}""/>
            <Setter Property=""BorderThickness"" Value=""0""/>
            <Setter Property=""Cursor"" Value=""Hand""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBoxItem"">
                        <Border x:Name=""ItemBorder"" Background=""{TemplateBinding Background}"" CornerRadius=""4"" Padding=""{TemplateBinding Padding}"" Margin=""1"">
                            <ContentPresenter x:Name=""ItemContent"" 
                                              HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" 
                                              VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""
                                              TextElement.Foreground=""{TemplateBinding Foreground}""
                                              TextElement.FontSize=""12""/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsHighlighted"" Value=""True"">
                                <Setter TargetName=""ItemBorder"" Property=""Background"" Value=""#2563EB""/>
                                <Setter Property=""Foreground"" Value=""#FFFFFF""/>
                            </Trigger>
                            <Trigger Property=""IsSelected"" Value=""True"">
                                <Setter TargetName=""ItemBorder"" Property=""Background"" Value=""#1D4ED8""/>
                                <Setter Property=""Foreground"" Value=""#FFFFFF""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- Custom ComboBox ToggleButton Template -->
        <ControlTemplate x:Key=""ComboBoxToggleButtonTemplate"" TargetType=""ToggleButton"">
            <Border x:Name=""templateRoot"" 
                    Background=""{DynamicResource InputBgBrush}"" 
                    BorderBrush=""{DynamicResource InputBorderBrush}"" 
                    BorderThickness=""1"" 
                    CornerRadius=""6"">
                <Border x:Name=""splitBorder"" Width=""30"" HorizontalAlignment=""Right"">
                    <Path x:Name=""arrow"" 
                          Data=""M 0 0 L 4.5 4.5 L 9 0 Z"" 
                          Fill=""{DynamicResource InputFgBrush}"" 
                          HorizontalAlignment=""Center"" 
                          VerticalAlignment=""Center""/>
                </Border>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property=""IsMouseOver"" Value=""True"">
                    <Setter TargetName=""templateRoot"" Property=""BorderBrush"" Value=""#38BDF8""/>
                </Trigger>
                <Trigger Property=""IsChecked"" Value=""True"">
                    <Setter TargetName=""templateRoot"" Property=""BorderBrush"" Value=""#2563EB""/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>

        <!-- Custom ComboBox Style -->
        <Style TargetType=""ComboBox"">
            <Setter Property=""Foreground"" Value=""{DynamicResource InputFgBrush}""/>
            <Setter Property=""Background"" Value=""{DynamicResource InputBgBrush}""/>
            <Setter Property=""BorderBrush"" Value=""{DynamicResource InputBorderBrush}""/>
            <Setter Property=""BorderThickness"" Value=""1""/>
            <Setter Property=""FontSize"" Value=""12.5""/>
            <Setter Property=""FontWeight"" Value=""Medium""/>
            <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
            <Setter Property=""Template"">
                <Setter.Value>
                    <ControlTemplate TargetType=""ComboBox"">
                        <Grid x:Name=""templateRoot"">
                            <ToggleButton x:Name=""toggleButton"" 
                                          Template=""{StaticResource ComboBoxToggleButtonTemplate}"" 
                                          Focusable=""False"" 
                                          IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" 
                                          ClickMode=""Press""/>
                            <ContentPresenter x:Name=""contentPresenter"" 
                                              Content=""{TemplateBinding SelectionBoxItem}"" 
                                              ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}"" 
                                              ContentTemplateSelector=""{TemplateBinding ItemTemplateSelector}"" 
                                              Margin=""10,0,32,0"" 
                                              HorizontalAlignment=""Left"" 
                                              VerticalAlignment=""Center"" 
                                              IsHitTestVisible=""False""
                                              TextElement.Foreground=""{DynamicResource InputFgBrush}""
                                              TextElement.FontSize=""12""
                                              TextElement.FontWeight=""SemiBold""/>
                            <Popup x:Name=""PART_Popup"" 
                                   AllowsTransparency=""True"" 
                                   IsOpen=""{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}"" 
                                   Placement=""Bottom"" 
                                   PopupAnimation=""Fade"">
                                <Border x:Name=""dropDownBorder"" 
                                        Background=""{DynamicResource CardBrush}"" 
                                        BorderBrush=""{DynamicResource CardBorderBrush}"" 
                                        BorderThickness=""1"" 
                                        CornerRadius=""6"" 
                                        Margin=""0,3,0,0"" 
                                        Padding=""4"" 
                                        MinWidth=""{Binding ActualWidth, ElementName=templateRoot}"" 
                                        MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                                    <ScrollViewer x:Name=""DropDownScrollViewer"" CanContentScroll=""True"">
                                        <ItemsPresenter x:Name=""ItemsPresenter"" KeyboardNavigation.DirectionalNavigation=""Contained""/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin=""16"">
        <Grid.RowDefinitions>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""*""/>
            <RowDefinition Height=""Auto""/>
        </Grid.RowDefinitions>

        <!-- Top Header & Theme Switcher -->
        <Grid Grid.Row=""0"" Margin=""0,0,0,12"">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""Auto""/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column=""0"">
                <TextBlock Text=""Windows WDDM &amp; VRAM Optimizer"" FontSize=""22"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                <TextBlock Text=""Native WDDM (dxgmms2.sys) video memory reserve tuning, PCIe shared RAM scaling &amp; Direct3D 11 benchmark"" FontSize=""12.5"" Foreground=""{DynamicResource SubTextBrush}"" Margin=""0,2,0,0""/>
            </StackPanel>
            <Button x:Name=""btnThemeToggle"" Grid.Column=""1"" Content=""☀️ Light Mode"" Background=""#334155"" Foreground=""#F8FAFC"" VerticalAlignment=""Center"" Padding=""12,7"" ToolTip=""Toggle between Dark Mode and Light Mode""/>
        </Grid>

        <!-- 4 Live Status Cards -->
        <Grid Grid.Row=""1"" Margin=""0,0,0,12"">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""1.3*""/>
            </Grid.ColumnDefinitions>

            <!-- Card 1: Dedicated / UMA Budget -->
            <Border Grid.Column=""0"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12,10"" Margin=""0,0,5,0"">
                <StackPanel>
                    <TextBlock x:Name=""lblCard1Title"" Text=""DXGI DEDICATED BUDGET"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagDxgiBrush}""/>
                    <TextBlock x:Name=""lblLiveVramBudget"" Text=""-- MiB"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveVramGain"" Text=""Querying DXGI..."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}"" Margin=""0,2,0,0"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
            </Border>

            <!-- Card 2: WDDM Reserve -->
            <Border Grid.Column=""1"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12,10"" Margin=""4,0,4,0"">
                <StackPanel>
                    <TextBlock Text=""WDDM KERNEL RESERVE"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagWddmBrush}""/>
                    <TextBlock x:Name=""lblLiveWddmReserve"" Text=""-- MiB"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveWddmState"" Text=""Reading Registry..."" FontSize=""11"" Foreground=""{DynamicResource CardTagWddmBrush}"" Margin=""0,2,0,0"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
            </Border>

            <!-- Card 3: Shared RAM Cap -->
            <Border Grid.Column=""2"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""12,10"" Margin=""4,0,4,0"">
                <StackPanel>
                    <TextBlock Text=""SHARED SYSTEM RAM CAP"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagSharedBrush}""/>
                    <TextBlock x:Name=""lblLiveSharedRam"" Text=""50%"" FontSize=""17"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}""/>
                    <TextBlock x:Name=""lblLiveSharedGb"" Text=""-- GB Limit"" FontSize=""11"" Foreground=""{DynamicResource CardTagSharedBrush}"" Margin=""0,2,0,0"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
            </Border>

            <!-- Card 4: Active GPU Adapter & Combined Max Pool -->
            <Border Grid.Column=""3"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""10,8"" Margin=""5,0,0,0"">
                <StackPanel>
                    <Grid Margin=""0,0,0,2"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column=""0"" Text=""SELECT GPU ADAPTER"" FontSize=""10"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagGpuBrush}""/>
                        <TextBlock Grid.Column=""1"" Text=""Multi-GPU"" FontSize=""9"" FontWeight=""SemiBold"" Foreground=""{DynamicResource SubTextBrush}""/>
                    </Grid>
                    <ComboBox x:Name=""cmbGpuSelector"" Height=""28"" Margin=""0,2,0,3"" ToolTip=""Select which GPU adapter to inspect and benchmark""/>
                    <TextBlock x:Name=""lblLiveTotalPool"" Text=""Total Pool: -- GiB"" FontSize=""10.5"" Foreground=""{DynamicResource AccentGreenBrush}"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Main Scrollable Body -->
        <ScrollViewer Grid.Row=""2"" VerticalScrollBarVisibility=""Auto"">
            <StackPanel Margin=""0,0,6,0"">

                <!-- Presets & Backup Controls -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""14"" Margin=""0,0,0,12"">
                    <StackPanel>
                        <Grid Margin=""0,0,0,10"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column=""0"" Text=""OPTIMIZATION PRESETS &amp; SAFE REGISTRY BACKUP"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}"" VerticalAlignment=""Center""/>
                            <StackPanel Grid.Column=""1"" Orientation=""Horizontal"">
                                <Button x:Name=""btnExportBackup"" Content=""💾 Export .reg Backup"" Background=""#334155"" Foreground=""#F8FAFC"" Padding=""10,5"" Margin=""0,0,6,0"" FontSize=""11"" ToolTip=""Export current WDDM &amp; HAGS registry settings to a .reg file""/>
                                <Button x:Name=""btnRestoreBackup"" Content=""📂 Restore from .reg"" Background=""#334155"" Foreground=""#F8FAFC"" Padding=""10,5"" FontSize=""11"" ToolTip=""Validate and import a previously exported .reg backup file""/>
                            </StackPanel>
                        </Grid>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name=""btnPresetRecommended"" Grid.Column=""0"" Content=""★ Recommended (Optimal)"" Background=""#2563EB"" Foreground=""White"" Margin=""0,0,5,0"" ToolTip=""128 MiB Frag Cap + 32 MiB Process Cap + 75% Shared RAM (unlocks +608 MiB on 6GB+ GPUs)""/>
                            <Button x:Name=""btnPresetAggressive"" Grid.Column=""1"" Content=""⚡ Aggressive (AI / LLM)"" Background=""#7C3AED"" Foreground=""White"" Margin=""5,0,5,0"" ToolTip=""32 MiB Kernel Floor + 80% Shared RAM (unlocks +704 MiB on 6GB+ GPUs)""/>
                            <Button x:Name=""btnPresetDefault"" Grid.Column=""2"" Content=""↺ Windows Stock Defaults"" Background=""#475569"" Foreground=""#F8FAFC"" Margin=""5,0,0,0"" ToolTip=""Removes custom MemoryManager overrides and restores stock Windows defaults""/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Fine-Grained Sliders Card -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,12"">
                    <StackPanel>
                        <TextBlock Text=""FINE-GRAINED WDDM KERNEL CAPS (SYSTEM-WIDE OVERRIDES)"" FontWeight=""Bold"" FontSize=""13.5"" Foreground=""{DynamicResource SectionTitleBrush}"" Margin=""0,0,0,12""/>
                        
                        <!-- Slider 1: Fragmentation Buffer -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Max Video Memory Fragmentation Buffer (32–512 MiB)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Caps WDDM's 10% adapter-wide page-table &amp; defragmentation reserve."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblFragValue"" Text=""512 MiB (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldFragBuffer"" Grid.Row=""1"" Height=""24"" Minimum=""32"" Maximum=""512"" Value=""512"" TickFrequency=""32"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- Slider 2: Process Budget Cap -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Max Process Budget Cap Buffer (32–256 MiB)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Caps WDDM's 5% single-process foreground budget reserve."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblProcValue"" Text=""256 MiB (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldProcCap"" Grid.Row=""1"" Height=""24"" Minimum=""32"" Maximum=""256"" Value=""256"" TickFrequency=""32"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- Slider 3: Shared RAM Commit Limit -->
                        <Grid Margin=""0,0,0,14"">
                            <Grid.RowDefinitions>
                                <RowDefinition Height=""Auto""/>
                                <RowDefinition Height=""Auto""/>
                            </Grid.RowDefinitions>
                            <Grid Grid.Row=""0"" Margin=""0,0,0,6"">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width=""*""/>
                                    <ColumnDefinition Width=""Auto""/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column=""0"">
                                    <TextBlock Text=""Shared GPU Memory Commit Limit (50%–90% System RAM)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                    <TextBlock Text=""Maximum host physical RAM addressable by GPUs via SystemPartitionCommitLimitPercentage."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </StackPanel>
                                <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,4"" VerticalAlignment=""Center"">
                                    <TextBlock x:Name=""lblSharedValue"" Text=""50% (Stock Default)"" FontWeight=""Bold"" FontSize=""12"" Foreground=""{DynamicResource SubTextBrush}""/>
                                </Border>
                            </Grid>
                            <Slider x:Name=""sldSharedRam"" Grid.Row=""1"" Height=""24"" Minimum=""50"" Maximum=""90"" Value=""50"" TickFrequency=""5"" IsSnapToTickEnabled=""True""/>
                        </Grid>

                        <!-- HAGS -->
                        <Grid Margin=""0,2,0,0"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""310""/>
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column=""0"">
                                <TextBlock Text=""Hardware-Accelerated GPU Scheduling (HAGS)"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{DynamicResource TextBrush}""/>
                                <TextBlock Text=""OFF saves 40–100 MiB VRAM for CUDA/LLMs; ON required for DLSS 3/4 Frame Gen."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}""/>
                            </StackPanel>
                            <ComboBox x:Name=""cmbHags"" Grid.Column=""1"" Height=""32"" VerticalAlignment=""Center""/>
                        </Grid>
                    </StackPanel>
                </Border>

                <!-- Live VRAM + Shared RAM Allocation Benchmark -->
                <Border Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,6"">
                    <StackPanel>
                        <Grid Margin=""0,0,0,3"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column=""0"" Text=""LIVE DIRECT3D 11 MEMORY ALLOCATION BENCHMARK"" FontWeight=""Bold"" FontSize=""13.5"" Foreground=""{DynamicResource AccentGreenBrush}""/>
                            <TextBlock x:Name=""lblBenchGpuName"" Grid.Column=""1"" Text=""Target GPU: --"" FontSize=""11.5"" FontWeight=""SemiBold"" Foreground=""{DynamicResource CardTagGpuBrush}""/>
                        </Grid>
                        <TextBlock Text=""Allocates real 64 MiB GPU-resident D3D11 buffers with live QueryVideoMemoryInfo telemetry (leaving a 1.5 GiB OS safety cushion so Windows stays responsive)."" FontSize=""11"" Foreground=""{DynamicResource SubTextBrush}"" Margin=""0,0,0,12""/>

                        <!-- Target Metrics -->
                        <Grid Margin=""0,0,0,12"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""1.2*""/>
                            </Grid.ColumnDefinitions>

                            <Border Grid.Column=""0"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""0,0,5,0"">
                                <StackPanel>
                                    <TextBlock x:Name=""lblBenchCard1Title"" Text=""DEDICATED LOCAL TARGET"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagDxgiBrush}""/>
                                    <TextBlock x:Name=""lblBenchDedicated"" Text=""-- MiB"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column=""1"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""4,0,4,0"">
                                <StackPanel>
                                    <TextBlock x:Name=""lblBenchCard2Title"" Text=""SHARED PCIe TARGET"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource CardTagSharedBrush}""/>
                                    <TextBlock x:Name=""lblBenchShared"" Text=""-- MiB"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource HeaderBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>

                            <Border Grid.Column=""2"" Background=""{DynamicResource BgBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,8"" Margin=""5,0,0,0"">
                                <StackPanel>
                                    <TextBlock x:Name=""lblBenchCard3Title"" Text=""MAX COMBINED POOL"" FontSize=""9.5"" FontWeight=""Bold"" Foreground=""{DynamicResource AccentGreenBrush}""/>
                                    <TextBlock x:Name=""lblBenchCombined"" Text=""-- MiB"" FontSize=""13"" FontWeight=""Bold"" Foreground=""{DynamicResource AccentGreenBrush}"" Margin=""0,2,0,0""/>
                                </StackPanel>
                            </Border>
                        </Grid>

                        <!-- Action Buttons -->
                        <Grid Margin=""0,0,0,10"">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <Button x:Name=""btnRunVramTest"" Grid.Column=""0"" Content=""▶ Run Full Memory Benchmark"" Background=""#16A34A"" Foreground=""White"" Padding=""16,8"" Margin=""0,0,8,0""/>
                            <Button x:Name=""btnStopVramTest"" Grid.Column=""1"" Content=""■ Stop (Esc)"" Background=""#DC2626"" Foreground=""White"" Padding=""16,8"" IsEnabled=""False""/>
                        </Grid>

                        <ProgressBar x:Name=""pbVramTest"" Height=""8"" Background=""{DynamicResource InputBgBrush}"" Foreground=""#16A34A"" Value=""0"" Margin=""0,0,0,8""/>

                        <Border Background=""{DynamicResource ConsoleBgBrush}"" CornerRadius=""6"" Padding=""10"">
                            <TextBox x:Name=""txtTestLog"" Text=""Ready to test. Select any GPU adapter above and click 'Run Full Memory Benchmark' to verify hardware allocation."" FontFamily=""Consolas"" FontSize=""11.5"" Foreground=""{DynamicResource ConsoleFgBrush}"" Background=""Transparent"" BorderThickness=""0"" IsReadOnly=""True"" TextWrapping=""Wrap"" MaxHeight=""135"" VerticalScrollBarVisibility=""Auto""/>
                        </Border>
                    </StackPanel>
                </Border>

            </StackPanel>
        </ScrollViewer>

        <!-- Bottom Action Bar -->
        <Border Grid.Row=""3"" Background=""{DynamicResource CardBrush}"" BorderBrush=""{DynamicResource CardBorderBrush}"" BorderThickness=""1"" CornerRadius=""8"" Padding=""10,9"" Margin=""0,10,0,0"">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""Auto""/>
                    <ColumnDefinition Width=""*""/>
                    <ColumnDefinition Width=""Auto""/>
                    <ColumnDefinition Width=""Auto""/>
                </Grid.ColumnDefinitions>
                <Button x:Name=""btnRefresh"" Grid.Column=""0"" Content=""Refresh Live Status (F5)"" Background=""#475569"" Foreground=""White"" Margin=""0,0,8,0""/>
                <TextBlock x:Name=""txtActionNotice"" Grid.Column=""1"" VerticalAlignment=""Center"" Margin=""8,0"" Foreground=""{DynamicResource AccentGreenBrush}"" FontWeight=""SemiBold"" TextTrimming=""CharacterEllipsis"" Text=""""/>
                <Button x:Name=""btnApply"" Grid.Column=""2"" Content=""Apply Settings (Ctrl+S)"" Background=""#2563EB"" Foreground=""White"" Margin=""0,0,8,0""/>
                <Button x:Name=""btnReboot"" Grid.Column=""3"" Content=""Restart Windows Now"" Background=""#DC2626"" Foreground=""White""/>
            </Grid>
        </Border>
    </Grid>
</Window>";

            using (XmlReader reader = XmlReader.Create(new StringReader(xaml))) {
                Window = (Window)XamlReader.Load(reader);
            }

            btnThemeToggle = (Button)Window.FindName("btnThemeToggle");
            lblCard1Title = (TextBlock)Window.FindName("lblCard1Title");
            lblLiveVramBudget = (TextBlock)Window.FindName("lblLiveVramBudget");
            lblLiveVramGain = (TextBlock)Window.FindName("lblLiveVramGain");
            lblLiveWddmReserve = (TextBlock)Window.FindName("lblLiveWddmReserve");
            lblLiveWddmState = (TextBlock)Window.FindName("lblLiveWddmState");
            lblLiveSharedRam = (TextBlock)Window.FindName("lblLiveSharedRam");
            lblLiveSharedGb = (TextBlock)Window.FindName("lblLiveSharedGb");
            cmbGpuSelector = (ComboBox)Window.FindName("cmbGpuSelector");
            lblLiveTotalPool = (TextBlock)Window.FindName("lblLiveTotalPool");

            btnPresetRecommended = (Button)Window.FindName("btnPresetRecommended");
            btnPresetAggressive = (Button)Window.FindName("btnPresetAggressive");
            btnPresetDefault = (Button)Window.FindName("btnPresetDefault");
            btnExportBackup = (Button)Window.FindName("btnExportBackup");
            btnRestoreBackup = (Button)Window.FindName("btnRestoreBackup");

            lblFragValue = (TextBlock)Window.FindName("lblFragValue");
            sldFragBuffer = (Slider)Window.FindName("sldFragBuffer");
            lblProcValue = (TextBlock)Window.FindName("lblProcValue");
            sldProcCap = (Slider)Window.FindName("sldProcCap");
            lblSharedValue = (TextBlock)Window.FindName("lblSharedValue");
            sldSharedRam = (Slider)Window.FindName("sldSharedRam");
            cmbHags = (ComboBox)Window.FindName("cmbHags");
            cmbHags.Items.Add("Disabled (Saves 40–100 MiB / HwSchMode=1)");
            cmbHags.Items.Add("Enabled (Required for Frame Gen / HwSchMode=2)");
            cmbHags.SelectedIndex = 1;

            lblBenchGpuName = (TextBlock)Window.FindName("lblBenchGpuName");
            lblBenchCard1Title = (TextBlock)Window.FindName("lblBenchCard1Title");
            lblBenchDedicated = (TextBlock)Window.FindName("lblBenchDedicated");
            lblBenchCard2Title = (TextBlock)Window.FindName("lblBenchCard2Title");
            lblBenchShared = (TextBlock)Window.FindName("lblBenchShared");
            lblBenchCard3Title = (TextBlock)Window.FindName("lblBenchCard3Title");
            lblBenchCombined = (TextBlock)Window.FindName("lblBenchCombined");
            btnRunVramTest = (Button)Window.FindName("btnRunVramTest");
            btnStopVramTest = (Button)Window.FindName("btnStopVramTest");
            pbVramTest = (ProgressBar)Window.FindName("pbVramTest");
            txtTestLog = (TextBox)Window.FindName("txtTestLog");

            btnRefresh = (Button)Window.FindName("btnRefresh");
            btnApply = (Button)Window.FindName("btnApply");
            btnReboot = (Button)Window.FindName("btnReboot");
            txtActionNotice = (TextBlock)Window.FindName("txtActionNotice");

            btnThemeToggle.Click += (s, e) => SetTheme(!isDarkMode);

            cmbGpuSelector.SelectionChanged += (s, e) => {
                if (isUpdatingControls) return;
                VramBenchmark.GpuAdapterInfo info = cmbGpuSelector.SelectedItem as VramBenchmark.GpuAdapterInfo;
                if (info != null) {
                    selectedGpu = info;
                    UpdateDashboardCards();
                    UpdateSliderBadges();
                }
            };

            sldFragBuffer.ValueChanged += (s, e) => UpdateSliderBadges();
            sldProcCap.ValueChanged += (s, e) => UpdateSliderBadges();
            sldSharedRam.ValueChanged += (s, e) => UpdateSliderBadges();

            btnPresetRecommended.Click += (s, e) => PreviewAndConfirmPreset(
                "Recommended (Optimal)",
                128, 32, 75, null,
                "MaxVideoMemoryFragmentationBuffer = 128 MiB\nMaxProcessBudgetCapBuffer = 32 MiB\nShared GPU RAM Limit = 75%");

            btnPresetAggressive.Click += (s, e) => PreviewAndConfirmPreset(
                "Aggressive (AI / LLM)",
                32, 32, 80, null,
                "MaxVideoMemoryFragmentationBuffer = 32 MiB\nMaxProcessBudgetCapBuffer = 32 MiB\nShared GPU RAM Limit = 80%");

            btnPresetDefault.Click += (s, e) => PreviewAndConfirmPreset(
                "Windows Stock Defaults",
                null, null, null, 2,
                "Deletes custom MemoryManager overrides (512 MiB / 256 MiB / 50% stock)\nRestores HwSchMode = 2 (HAGS Enabled)");

            btnExportBackup.Click += (s, e) => {
                try {
                    SaveFileDialog sfd = new SaveFileDialog {
                        Filter = "Registry Files (*.reg)|*.reg",
                        FileName = string.Format("WDDM_VRAM_Backup_{0:yyyyMMdd_HHmmss}.reg", DateTime.Now),
                        Title = "Export WDDM Registry Backup"
                    };
                    if (sfd.ShowDialog() == true) {
                        RegistryManager.ExportRegistryFile(sfd.FileName);
                        txtActionNotice.Text = "Backup exported: " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show("Registry configuration exported to:\n" + sfd.FileName,
                            "Backup Exported", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                } catch (Exception ex) {
                    MessageBox.Show("Failed to export registry backup:\n" + ex.Message,
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnRestoreBackup.Click += (s, e) => {
                try {
                    OpenFileDialog ofd = new OpenFileDialog {
                        Filter = "Registry Files (*.reg)|*.reg",
                        Title = "Select WDDM Registry Backup (.reg) to Restore"
                    };
                    if (ofd.ShowDialog() == true) {
                        MessageBoxResult res = MessageBox.Show(
                            "Restore WDDM & HAGS registry settings from:\n" + ofd.FileName + "\n\nContinue?",
                            "Confirm Registry Restore", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (res == MessageBoxResult.Yes) {
                            RegistryManager.RestoreRegistryFile(ofd.FileName);
                            RefreshAllFromHardwareAndRegistry();
                            txtActionNotice.Text = "Restored from " + Path.GetFileName(ofd.FileName) + " (restart required).";
                            MessageBox.Show("Registry settings restored from:\n" + ofd.FileName + "\n\nRestart Windows to apply changes.",
                                "Restore Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                } catch (Exception ex) {
                    MessageBox.Show("Failed to restore registry backup:\n" + ex.Message,
                        "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            btnApply.Click += (s, e) => {
                int fragVal = (int)sldFragBuffer.Value;
                int procVal = (int)sldProcCap.Value;
                int sharedVal = (int)sldSharedRam.Value;
                int hagsVal = (cmbHags.SelectedIndex == 0) ? 1 : 2;

                int? fragOpt = (fragVal == WddmCalculator.STOCK_MAX_FRAG_MB) ? (int?)null : fragVal;
                int? procOpt = (procVal == WddmCalculator.STOCK_MAX_PROC_MB) ? (int?)null : procVal;
                int? sharedOpt = (sharedVal == WddmCalculator.STOCK_SHARED_PCT) ? (int?)null : sharedVal;

                ApplyAndRefresh(fragOpt, procOpt, sharedOpt, hagsVal);
            };

            btnRefresh.Click += (s, e) => {
                RefreshAllFromHardwareAndRegistry();
                txtActionNotice.Text = "Live DXGI hardware & registry telemetry refreshed.";
            };

            btnReboot.Click += (s, e) => {
                MessageBoxResult res = MessageBox.Show(
                    "Restart Windows now so updated WDDM kernel settings take effect?",
                    "Restart Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes) {
                    Process.Start("shutdown.exe", "/r /t 0");
                }
            };

            btnRunVramTest.Click += (s, e) => {
                btnRunVramTest.IsEnabled = false;
                btnStopVramTest.IsEnabled = true;
                cmbGpuSelector.IsEnabled = false;

                string gpuLabel = (selectedGpu != null) ? selectedGpu.Name : "Default GPU";
                long targetLuid = (selectedGpu != null) ? selectedGpu.Luid : 0;
                int targetIdx = (selectedGpu != null) ? selectedGpu.Index : -1;

                txtTestLog.Text = string.Format("Starting Direct3D 11 allocation benchmark on {0}...\r\n", gpuLabel);
                pbVramTest.Value = 0;

                Thread t = new Thread(() => {
                    VramBenchmark.RunFullBenchmark(
                        targetLuid,
                        targetIdx,
                        msg => Window.Dispatcher.BeginInvoke(new Action(() => {
                            txtTestLog.AppendText(msg + "\r\n");
                            txtTestLog.ScrollToEnd();
                        })),
                        pct => Window.Dispatcher.BeginInvoke(new Action(() => {
                            pbVramTest.Value = pct;
                        })),
                        () => Window.Dispatcher.BeginInvoke(new Action(() => {
                            btnRunVramTest.IsEnabled = true;
                            btnStopVramTest.IsEnabled = false;
                            cmbGpuSelector.IsEnabled = true;
                            RefreshGpuHardwareList();
                            UpdateDashboardCards();
                        }))
                    );
                });
                t.IsBackground = true;
                t.Start();
            };

            btnStopVramTest.Click += (s, e) => {
                VramBenchmark.CancelRequested = true;
                btnStopVramTest.IsEnabled = false;
                txtTestLog.AppendText("Halt requested — releasing allocated buffers...\r\n");
            };

            Window.KeyDown += (s, e) => {
                if (e.Key == Key.F5) {
                    RefreshAllFromHardwareAndRegistry();
                    txtActionNotice.Text = "Live DXGI hardware & registry telemetry refreshed (F5).";
                    e.Handled = true;
                } else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                    if (btnApply.IsEnabled) {
                        btnApply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    e.Handled = true;
                } else if (e.Key == Key.Escape) {
                    if (btnStopVramTest.IsEnabled) {
                        btnStopVramTest.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        e.Handled = true;
                    }
                }
            };

            RegistryManager.EnsureStartupSnapshot();
            SetTheme(true);
            RefreshAllFromHardwareAndRegistry();
        }

        private void PreviewAndConfirmPreset(string presetTitle, int? fragMb, int? procMb, int? sharedPct, int? forceHags, string summaryLines) {
            sldFragBuffer.Value = fragMb ?? WddmCalculator.STOCK_MAX_FRAG_MB;
            sldProcCap.Value = procMb ?? WddmCalculator.STOCK_MAX_PROC_MB;
            sldSharedRam.Value = sharedPct ?? WddmCalculator.STOCK_SHARED_PCT;
            if (forceHags.HasValue) {
                cmbHags.SelectedIndex = (forceHags.Value == 1) ? 0 : 1;
            }
            UpdateSliderBadges();

            MessageBoxResult confirm = MessageBox.Show(
                string.Format("Apply the '{0}' preset to Windows Registry now?\n\n{1}\n\n(Click 'No' to keep these values previewed on the sliders without writing to the registry.)",
                    presetTitle, summaryLines),
                "Confirm Preset Application",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes) {
                int hagsToWrite = forceHags ?? ((cmbHags.SelectedIndex == 0) ? 1 : 2);
                ApplyAndRefresh(fragMb, procMb, sharedPct, hagsToWrite);
            } else {
                txtActionNotice.Text = string.Format("'{0}' loaded on sliders (press Ctrl+S to apply).", presetTitle);
            }
        }

        private void ApplyAndRefresh(int? fragMb, int? procMb, int? sharedPct, int? hagsMode) {
            try {
                RegistryManager.ApplySettings(fragMb, procMb, sharedPct, hagsMode);
                RefreshAllFromHardwareAndRegistry();
                txtActionNotice.Text = "Settings written to HKLM! Restart Windows to take effect.";
            } catch (Exception ex) {
                MessageBox.Show("Failed to write settings to registry:\n" + ex.Message,
                    "Registry Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshGpuHardwareList() {
            long previousLuid = (selectedGpu != null) ? selectedGpu.Luid : 0;
            totalRamGb = VramBenchmark.GetTotalSystemRamGb();
            detectedGpus = VramBenchmark.GetAllGpuAdapters();

            isUpdatingControls = true;
            try {
                cmbGpuSelector.ItemsSource = null;
                cmbGpuSelector.ItemsSource = detectedGpus;

                if (detectedGpus.Count > 0) {
                    int chosenIdx = -1;
                    if (previousLuid != 0) {
                        for (int i = 0; i < detectedGpus.Count; i++) {
                            if (detectedGpus[i].Luid == previousLuid) {
                                chosenIdx = i;
                                break;
                            }
                        }
                    }

                    // Default to discrete GPU with largest Dedicated VRAM
                    if (chosenIdx < 0) {
                        chosenIdx = 0;
                        double bestScore = -1;
                        for (int i = 0; i < detectedGpus.Count; i++) {
                            double score = (detectedGpus[i].IsUma ? 0 : 1000000.0) + detectedGpus[i].DedicatedMb;
                            if (score > bestScore) {
                                bestScore = score;
                                chosenIdx = i;
                            }
                        }
                    }

                    cmbGpuSelector.SelectedIndex = chosenIdx;
                    selectedGpu = detectedGpus[chosenIdx];
                } else {
                    selectedGpu = null;
                }
            } finally {
                isUpdatingControls = false;
            }
        }

        private double GetReferenceDiscreteVramMb() {
            if (selectedGpu != null && !selectedGpu.IsUma && selectedGpu.DedicatedMb > 0) {
                return selectedGpu.DedicatedMb;
            }
            foreach (var g in detectedGpus) {
                if (!g.IsUma && g.DedicatedMb > 0) {
                    return g.DedicatedMb;
                }
            }
            return 6144.0;
        }

        private void UpdateSliderBadges() {
            if (lblFragValue == null || lblProcValue == null || lblSharedValue == null) return;

            double refVramMb = GetReferenceDiscreteVramMb();

            int fragCap = (int)sldFragBuffer.Value;
            int stockFragEff = WddmCalculator.ComputeEffectiveFragReserveMb(refVramMb, null);
            int curFragEff = WddmCalculator.ComputeEffectiveFragReserveMb(refVramMb, fragCap);
            int fragSaved = stockFragEff - curFragEff;

            if (fragSaved > 0) {
                lblFragValue.Text = string.Format("{0} MiB (+{1} MiB Unlocked)", fragCap, fragSaved);
                lblFragValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
            } else if (fragCap == WddmCalculator.STOCK_MAX_FRAG_MB) {
                lblFragValue.Text = string.Format("{0} MiB (Stock Default)", fragCap);
                lblFragValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
            } else {
                lblFragValue.Text = string.Format("{0} MiB Cap (Effective: {1} MiB)", fragCap, curFragEff);
                lblFragValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
            }

            int procCap = (int)sldProcCap.Value;
            int stockProcEff = WddmCalculator.ComputeEffectiveProcReserveMb(refVramMb, null);
            int curProcEff = WddmCalculator.ComputeEffectiveProcReserveMb(refVramMb, procCap);
            int procSaved = stockProcEff - curProcEff;

            if (procSaved > 0) {
                lblProcValue.Text = string.Format("{0} MiB (+{1} MiB Unlocked)", procCap, procSaved);
                lblProcValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
            } else if (procCap == WddmCalculator.STOCK_MAX_PROC_MB) {
                lblProcValue.Text = string.Format("{0} MiB (Stock Default)", procCap);
                lblProcValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
            } else {
                lblProcValue.Text = string.Format("{0} MiB Cap (Effective: {1} MiB)", procCap, curProcEff);
                lblProcValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
            }

            int sharedVal = (int)sldSharedRam.Value;
            double sharedGb = Math.Round((sharedVal * totalRamGb) / 100.0, 1);
            if (sharedVal > WddmCalculator.STOCK_SHARED_PCT) {
                lblSharedValue.Text = string.Format("{0}% (~{1:F1} GiB on {2:F1} GiB RAM / +{3}%)",
                    sharedVal, sharedGb, totalRamGb, sharedVal - WddmCalculator.STOCK_SHARED_PCT);
                lblSharedValue.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
            } else {
                lblSharedValue.Text = string.Format("{0}% (Stock / ~{1:F1} GiB on {2:F1} GiB RAM)",
                    sharedVal, sharedGb, totalRamGb);
                lblSharedValue.Foreground = (Brush)Window.Resources["SubTextBrush"];
            }
        }

        private void UpdateDashboardCards() {
            WddmRegistryState regState = RegistryManager.ReadCurrentState();

            if (selectedGpu != null) {
                if (selectedGpu.IsUma) {
                    lblCard1Title.Text = "UMA UNIFIED BUDGET";
                    lblLiveVramBudget.Text = string.Format("{0:N0} MiB", selectedGpu.LocalBudgetMb);
                    lblLiveVramGain.Text = string.Format("{0:N0} MiB Carve-Out + Shared RAM", selectedGpu.DedicatedMb);
                    lblLiveVramGain.Foreground = (Brush)Window.Resources["CardTagDxgiBrush"];

                    lblLiveWddmReserve.Text = "UMA Shared";
                    lblLiveWddmState.Text = string.Format("dGPU Caps: {0}+{1} MiB", regState.EffectiveFragCapMb, regState.EffectiveProcCapMb);
                    lblLiveWddmState.Foreground = (Brush)Window.Resources["SubTextBrush"];

                    lblBenchCard1Title.Text = "BIOS UMA CARVE-OUT";
                    lblBenchDedicated.Text = string.Format("{0:N0} MiB ({1:F2} GiB)", selectedGpu.DedicatedMb, selectedGpu.DedicatedMb / 1024.0);
                    lblBenchCard2Title.Text = "UNIFIED SYSTEM POOL";
                    lblBenchShared.Text = string.Format("{0:N0} MiB ({1:F1} GiB)", selectedGpu.LocalBudgetMb, selectedGpu.LocalBudgetMb / 1024.0);
                    lblBenchCard3Title.Text = "MAX UNIFIED POOL";
                    lblBenchCombined.Text = string.Format("{0:N0} MiB (~{1:F1} GiB)", selectedGpu.TotalPoolBudgetMb, selectedGpu.TotalPoolBudgetMb / 1024.0);
                } else {
                    lblCard1Title.Text = "DXGI DEDICATED BUDGET";
                    lblLiveVramBudget.Text = string.Format("{0:N0} MiB", selectedGpu.LocalBudgetMb);

                    double stockBudgetMb = WddmCalculator.ComputeStockDedicatedBudgetMb(selectedGpu.DedicatedMb);
                    double deltaMb = Math.Round(selectedGpu.LocalBudgetMb - stockBudgetMb);
                    if (deltaMb >= 16) {
                        lblLiveVramGain.Text = string.Format("+{0:N0} MiB Unlocked ({1:F2} GiB)", deltaMb, selectedGpu.LocalBudgetMb / 1024.0);
                        lblLiveVramGain.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                    } else if (deltaMb >= -16) {
                        lblLiveVramGain.Text = string.Format("Stock Baseline ({0:F2} GiB)", selectedGpu.LocalBudgetMb / 1024.0);
                        lblLiveVramGain.Foreground = (Brush)Window.Resources["SubTextBrush"];
                    } else {
                        lblLiveVramGain.Text = string.Format("{0:F2} GiB Dynamic Budget", selectedGpu.LocalBudgetMb / 1024.0);
                        lblLiveVramGain.Foreground = (Brush)Window.Resources["SubTextBrush"];
                    }

                    int effFrag = WddmCalculator.ComputeEffectiveFragReserveMb(selectedGpu.DedicatedMb, regState.CustomFragMb);
                    int effProc = WddmCalculator.ComputeEffectiveProcReserveMb(selectedGpu.DedicatedMb, regState.CustomProcMb);
                    int effTotal = effFrag + effProc;
                    int stockTotal = WddmCalculator.ComputeStockTotalReserveMb(selectedGpu.DedicatedMb);

                    lblLiveWddmReserve.Text = string.Format("{0} MiB", effTotal);
                    if (effTotal < stockTotal) {
                        lblLiveWddmState.Text = string.Format("Optimized ({0}+{1} MiB)", effFrag, effProc);
                        lblLiveWddmState.Foreground = (Brush)Window.Resources["AccentGreenBrush"];
                    } else {
                        lblLiveWddmState.Text = string.Format("Stock ({0}+{1} MiB)", effFrag, effProc);
                        lblLiveWddmState.Foreground = (Brush)Window.Resources["CardTagGpuBrush"];
                    }

                    lblBenchCard1Title.Text = "DEDICATED LOCAL TARGET";
                    lblBenchDedicated.Text = string.Format("{0:N0} MiB ({1:F1} GiB)", selectedGpu.LocalBudgetMb, selectedGpu.LocalBudgetMb / 1024.0);
                    lblBenchCard2Title.Text = "SHARED PCIe TARGET";
                    lblBenchShared.Text = string.Format("{0:N0} MiB ({1:F1} GiB)", selectedGpu.NonLocalBudgetMb, selectedGpu.NonLocalBudgetMb / 1024.0);
                    lblBenchCard3Title.Text = "MAX COMBINED POOL";
                    lblBenchCombined.Text = string.Format("{0:N0} MiB (~{1:F1} GiB)", selectedGpu.TotalPoolBudgetMb, selectedGpu.TotalPoolBudgetMb / 1024.0);
                }

                double totalPoolGiB = selectedGpu.TotalPoolBudgetMb / 1024.0;
                lblLiveTotalPool.Text = selectedGpu.IsUma
                    ? string.Format("Unified Pool: ~{0:F1} GiB (UMA)", totalPoolGiB)
                    : string.Format("Total Pool: ~{0:F1} GiB", totalPoolGiB);

                lblBenchGpuName.Text = string.Format("Target GPU: {0}",
                    selectedGpu.Name.Replace("NVIDIA ", "").Replace("AMD ", "").Replace("Intel(R) ", "").Replace("Intel ", ""));
            }

            int sharedPct = regState.EffectiveSharedPct;
            lblLiveSharedRam.Text = string.Format("{0}%", sharedPct);
            double approxGb = Math.Round((sharedPct * totalRamGb) / 100.0, 1);
            lblLiveSharedGb.Text = string.Format("~{0:F1} GiB of {1:F1} GiB RAM", approxGb, totalRamGb);
        }

        private void RefreshAllFromHardwareAndRegistry() {
            RefreshGpuHardwareList();

            WddmRegistryState state = RegistryManager.ReadCurrentState();
            sldFragBuffer.Value = Math.Max(WddmCalculator.MIN_BUFFER_MB, Math.Min(WddmCalculator.STOCK_MAX_FRAG_MB, state.EffectiveFragCapMb));
            sldProcCap.Value = Math.Max(WddmCalculator.MIN_BUFFER_MB, Math.Min(WddmCalculator.STOCK_MAX_PROC_MB, state.EffectiveProcCapMb));
            sldSharedRam.Value = state.EffectiveSharedPct;
            cmbHags.SelectedIndex = (state.EffectiveHagsMode == 1) ? 0 : 1;

            UpdateDashboardCards();
            UpdateSliderBadges();
        }

        private void SetBrushColor(string key, string hex) {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            SolidColorBrush b = Window.Resources.Contains(key) ? (Window.Resources[key] as SolidColorBrush) : null;
            if (b != null && !b.IsFrozen) {
                b.Color = c;
            } else {
                Window.Resources[key] = new SolidColorBrush(c);
            }
            if (Application.Current != null && Application.Current.Resources != null) {
                Application.Current.Resources[key] = Window.Resources[key];
            }
        }

        private void SetTheme(bool dark) {
            isDarkMode = dark;
            if (dark) {
                SetBrushColor("BgBrush", "#0B1120");
                SetBrushColor("CardBrush", "#1E293B");
                SetBrushColor("CardBorderBrush", "#334155");
                SetBrushColor("TextBrush", "#F8FAFC");
                SetBrushColor("SubTextBrush", "#94A3B8");
                SetBrushColor("HeaderBrush", "#FFFFFF");
                SetBrushColor("InputBgBrush", "#0F172A");
                SetBrushColor("InputFgBrush", "#F8FAFC");
                SetBrushColor("InputBorderBrush", "#475569");
                SetBrushColor("ConsoleBgBrush", "#020617");
                SetBrushColor("ConsoleFgBrush", "#38BDF8");
                SetBrushColor("CardTagDxgiBrush", "#38BDF8");
                SetBrushColor("CardTagWddmBrush", "#818CF8");
                SetBrushColor("CardTagSharedBrush", "#34D399");
                SetBrushColor("CardTagGpuBrush", "#FBBF24");
                SetBrushColor("SectionTitleBrush", "#38BDF8");
                SetBrushColor("AccentGreenBrush", "#4ADE80");
                SetBrushColor("SliderTrackBrush", "#334155");
                SetBrushColor("SliderThumbBrush", "#38BDF8");

                btnThemeToggle.Content = "☀️ Light Mode";
                btnThemeToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                btnThemeToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            } else {
                SetBrushColor("BgBrush", "#F8FAFC");
                SetBrushColor("CardBrush", "#FFFFFF");
                SetBrushColor("CardBorderBrush", "#E2E8F0");
                SetBrushColor("TextBrush", "#0F172A");
                SetBrushColor("SubTextBrush", "#475569");
                SetBrushColor("HeaderBrush", "#0F172A");
                SetBrushColor("InputBgBrush", "#F1F5F9");
                SetBrushColor("InputFgBrush", "#0F172A");
                SetBrushColor("InputBorderBrush", "#94A3B8");
                SetBrushColor("ConsoleBgBrush", "#0F172A");
                SetBrushColor("ConsoleFgBrush", "#38BDF8");
                SetBrushColor("CardTagDxgiBrush", "#0284C7");
                SetBrushColor("CardTagWddmBrush", "#4F46E5");
                SetBrushColor("CardTagSharedBrush", "#059669");
                SetBrushColor("CardTagGpuBrush", "#D97706");
                SetBrushColor("SectionTitleBrush", "#0284C7");
                SetBrushColor("AccentGreenBrush", "#16A34A");
                SetBrushColor("SliderTrackBrush", "#CBD5E1");
                SetBrushColor("SliderThumbBrush", "#0284C7");

                btnThemeToggle.Content = "🌙 Dark Mode";
                btnThemeToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                btnThemeToggle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            }

            UpdateDashboardCards();
            UpdateSliderBadges();
        }
    }
}
