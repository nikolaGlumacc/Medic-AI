$xamlContent = @'
<Window x:Class="MedicAIGUI.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MedicAI"
        Width="1100"
        Height="700"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        WindowStyle="None"
        Background="#0E0E12"
        Foreground="#FFFFFF"
        FontFamily="Segoe UI Variable"
        Closing="Window_Closing">

    <Window.Resources>
        <Color x:Key="BaseDark">#0E0E12</Color>
        <Color x:Key="BaseSoft">#141418</Color>
        <Color x:Key="PanelBase">#15161D</Color>
        <Color x:Key="Accent">#E63946</Color>
        <Color x:Key="AccentSoft">#FF6B6B</Color>
        <Color x:Key="BorderSoft">#33FFFFFF</Color>
        <Color x:Key="TextPrimary">#FFFFFF</Color>
        <Color x:Key="TextSecondary">#A6A6A6</Color>
        <Color x:Key="TextHint">#666E7F</Color>
        <SolidColorBrush x:Key="WindowBrush" Color="{StaticResource BaseDark}" />
        <SolidColorBrush x:Key="PanelBrush" Color="{StaticResource PanelBase}" />
        <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource Accent}" />
        <SolidColorBrush x:Key="AccentSoftBrush" Color="{StaticResource AccentSoft}" />
        <SolidColorBrush x:Key="BorderBrushSoft" Color="{StaticResource BorderSoft}" />
        <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimary}" />
        <SolidColorBrush x:Key="TextSecondaryBrush" Color="{StaticResource TextSecondary}" />
        <SolidColorBrush x:Key="TextHintBrush" Color="{StaticResource TextHint}" />
        <DropShadowEffect x:Key="PanelShadow" Color="#000000" BlurRadius="18" ShadowDepth="6" Opacity="0.2" />
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
            <Setter Property="FontWeight" Value="Normal" />
        </Style>
        <Style TargetType="Button">
            <Setter Property="FontFamily" Value="Segoe UI Variable" />
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
            <Setter Property="Background" Value="#1C1F27" />
            <Setter Property="BorderBrush" Value="{StaticResource BorderBrushSoft}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Padding" Value="10,8" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="SnapsToDevicePixels" Value="True" />
            <Setter Property="RenderTransformOrigin" Value="0.5,0.5" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Root" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" SnapsToDevicePixels="True">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Root" Property="Background" Value="#242930" />
                                <Setter TargetName="Root" Property="BorderBrush" Value="{StaticResource AccentBrush}" />
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="Root" Property="Background" Value="#1A1D26" />
                                <Setter TargetName="Root" Property="RenderTransform">
                                    <Setter.Value>
                                        <ScaleTransform ScaleX="0.98" ScaleY="0.98" />
                                    </Setter.Value>
                                </Setter>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
            <Setter Property="Background" Value="#171A20" />
            <Setter Property="BorderBrush" Value="#FFFFFF1A" />
            <Setter Property="BorderThickness" Value="0,0,0,1" />
            <Setter Property="Padding" Value="0,6,0,6" />
            <Setter Property="Margin" Value="0,6,0,6" />
        </Style>
        <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
            <Setter Property="Background" Value="#171A20" />
            <Setter Property="BorderBrush" Value="#FFFFFF1A" />
            <Setter Property="BorderThickness" Value="0,0,0,1" />
            <Setter Property="Padding" Value="0,6,0,6" />
            <Setter Property="Margin" Value="0,6,0,6" />
        </Style>
        <Style TargetType="Slider">
            <Setter Property="Height" Value="22" />
            <Setter Property="Margin" Value="0,10,0,10" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid Height="22">
                            <Track x:Name="PART_Track" VerticalAlignment="Center">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Background="#1E2128" BorderBrush="#FFFFFF14" BorderThickness="0" />
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb Width="14" Height="14" Background="{StaticResource AccentBrush}" BorderBrush="#FFFFFF22" BorderThickness="1" />
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Background="#1E2128" BorderBrush="#FFFFFF14" BorderThickness="0" />
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="CheckBox">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Grid Width="48" Height="26">
                                <Border x:Name="Track" CornerRadius="13" Background="#1F2229" BorderBrush="#FFFFFF14" BorderThickness="1" />
                                <Ellipse x:Name="Thumb" Width="20" Height="20" Fill="#14161B" Margin="3" HorizontalAlignment="Left" />
                            </Grid>
                            <ContentPresenter VerticalAlignment="Center" Margin="10,0,0,0" />
                        </StackPanel>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="Track" Property="Background" Value="{StaticResource AccentBrush}" />
                                <Setter TargetName="Thumb" Property="Fill" Value="#FFFFFF" />
                                <Setter TargetName="Thumb" Property="HorizontalAlignment" Value="Right" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key="CardStyle" TargetType="Border">
            <Setter Property="Background" Value="#15181FDD" />
            <Setter Property="CornerRadius" Value="12" />
            <Setter Property="Padding" Value="18" />
            <Setter Property="BorderBrush" Value="#FFFFFF14" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="Effect" Value="{StaticResource PanelShadow}" />
        </Style>
        <Style x:Key="TitleText" TargetType="TextBlock">
            <Setter Property="FontSize" Value="14" />
            <Setter Property="FontWeight" Value="Bold" />
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
        </Style>
        <Style x:Key="HintText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource TextHintBrush}" />
            <Setter Property="FontSize" Value="12" />
        </Style>
        <Style x:Key="NavItemStyle" TargetType="RadioButton">
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Padding" Value="12,10" />
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Margin" Value="0,0,0,8" />
            <Setter Property="HorizontalContentAlignment" Value="Left" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RadioButton">
                        <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="10" BorderBrush="Transparent" BorderThickness="1" Padding="4">
                            <TextBlock x:Name="Label" Text="{TemplateBinding Content}" VerticalAlignment="Center" Margin="12,0,0,0" FontWeight="SemiBold" Foreground="{TemplateBinding Foreground}" />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Root" Property="Background" Value="#1E222D" />
                                <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
                            </Trigger>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="Root" Property="Background" Value="#1F242F" />
                                <Setter TargetName="Root" Property="BorderBrush" Value="{StaticResource AccentBrush}" />
                                <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style x:Key="ListBoxItemCardStyle" TargetType="ListBoxItem">
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderThickness" Value="0" />
            <Setter Property="Padding" Value="0,4" />
            <Setter Property="Margin" Value="0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Bd" Background="#15171E" CornerRadius="8" Padding="8" BorderBrush="#FFFFFF14" BorderThickness="1" Margin="0,2">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsSelected" Value="true">
                                <Setter TargetName="Bd" Property="Background" Value="#1E242F" />
                                <Setter TargetName="Bd" Property="BorderBrush" Value="{StaticResource AccentBrush}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="8" />
            <Setter Property="Background" Value="#111418" />
            <Setter Property="Foreground" Value="#5F6572" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Grid Background="Transparent">
                            <Track x:Name="PART_Track" IsDirectionReversed="True">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.LineUpCommand" Background="Transparent" BorderThickness="0" />
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb Background="#2E343F" />
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command="ScrollBar.LineDownCommand" Background="Transparent" BorderThickness="0" />
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="58" />
            <RowDefinition Height="*" />
            <RowDefinition Height="46" />
        </Grid.RowDefinitions>

        <Border Grid.RowSpan="3" Grid.ColumnSpan="2" Background="{StaticResource WindowBrush}" />

        <!-- Title Bar -->
        <Border Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" Background="#0F141C" Opacity="0.96" Effect="{StaticResource PanelShadow}" />
        <Grid Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" Margin="24,0" MouseDown="TitleBar_MouseDown">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="150" />
            </Grid.ColumnDefinitions>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <Border Width="42" Height="42" Background="#172B3A" CornerRadius="12" BorderBrush="{StaticResource BorderBrushSoft}" BorderThickness="1" VerticalAlignment="Center">
                    <TextBlock Text="M" HorizontalAlignment="Center" VerticalAlignment="Center" FontWeight="Bold" Foreground="{StaticResource AccentBrush}" />
                </Border>
                <StackPanel Margin="12,0,0,0">
                    <TextBlock Text="MedicAI" FontSize="16" FontWeight="Bold" />
                    <TextBlock Text="Premium bot dashboard" Style="{StaticResource HintText}" />
                </StackPanel>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Grid.Column="1" HorizontalAlignment="Right" VerticalAlignment="Center">
                <Button x:Name="MinimizeBtn" Click="MinimizeBtn_Click" Width="38" Height="32" Content="_" Margin="0,0,8,0" />
                <Button x:Name="CloseBtn" Click="CloseBtn_Click" Width="38" Height="32" Content="✕" />
            </StackPanel>
        </Grid>

        <!-- Sidebar Navigation -->
        <Border Grid.Row="1" Grid.Column="0" Margin="24,18,12,18" Background="#141C28" CornerRadius="28" BorderBrush="{StaticResource BorderBrushSoft}" BorderThickness="1" Effect="{StaticResource PanelShadow}">
            <StackPanel Margin="20">
                <TextBlock Text="Navigation" Style="{StaticResource TitleText}" />
                <RadioButton x:Name="DashboardTabBtn" Content="Dashboard" Style="{StaticResource NavItemStyle}" IsChecked="True" Checked="NavTab_Checked" />
                <RadioButton x:Name="PriorityTabBtn" Content="Priority" Style="{StaticResource NavItemStyle}" Checked="NavTab_Checked" />
                <RadioButton x:Name="SettingsTabBtn" Content="Settings" Style="{StaticResource NavItemStyle}" Checked="NavTab_Checked" />
                <Border Margin="0,24,0,0" Padding="16" Background="#111820" CornerRadius="22" BorderBrush="#FFFFFF14" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="Live Status" Style="{StaticResource HintText}" />
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,10,0,0">
                            <Ellipse Width="12" Height="12" Fill="#2ED47A" />
                            <TextBlock x:Name="StatusLabel" Text="Connected" FontWeight="SemiBold" Margin="10,0,0,0" />
                        </StackPanel>
                        <TextBlock x:Name="FollowModeLabel" Text="Mode: ACTIVE" Margin="0,12,0,0" />
                        <TextBlock x:Name="UberMeterLabel" Text="Uber: 0%" Margin="0,4,0,0" />
                    </StackPanel>
                </Border>
            </StackPanel>
        </Border>

        <!-- Main Content - Dashboard Tab (Default) -->
        <ScrollViewer Grid.Row="1" Grid.Column="1" VerticalScrollBarVisibility="Auto" x:Name="DashboardScroller" Margin="12,18,24,18">
            <StackPanel x:Name="DashboardContent" Visibility="Visible">
                <Grid Margin="0,0,0,16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="2.4*" />
                        <ColumnDefinition Width="1.6*" />
                    </Grid.ColumnDefinitions>
                    <Border Grid.Column="0" Style="{StaticResource CardStyle}" Margin="0,0,12,0">
                        <StackPanel>
                            <TextBlock Text="Quick Controls" Style="{StaticResource TitleText}" />
                            <Grid Margin="0,18,0,0">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="Primary Weapon" Style="{StaticResource HintText}" />
                                    <ComboBox x:Name="PrimaryWeapon" SelectedIndex="0" Margin="0,6,0,16">
                                        <ComboBoxItem Content="Crusader's Crossbow" />
                                        <ComboBoxItem Content="Medi Gun" />
                                        <ComboBoxItem Content="Kritzkrieg" />
                                        <ComboBoxItem Content="Vaccinator" />
                                    </ComboBox>
                                    <TextBlock Text="Secondary Weapon" Style="{StaticResource HintText}" />
                                    <ComboBox x:Name="SecondaryWeapon" SelectedIndex="0" Margin="0,6,0,16">
                                        <ComboBoxItem Content="Medi Gun" />
                                        <ComboBoxItem Content="Kritzkrieg" />
                                        <ComboBoxItem Content="Quick-Fix" />
                                        <ComboBoxItem Content="Stock" />
                                    </ComboBox>
                                    <TextBlock Text="Melee Weapon" Style="{StaticResource HintText}" />
                                    <ComboBox x:Name="MeleeWeapon" SelectedIndex="0" Margin="0,6,0,0">
                                        <ComboBoxItem Content="Ubersaw" />
                                        <ComboBoxItem Content="Amputator" />
                                        <ComboBoxItem Content="Solemn Vow" />
                                        <ComboBoxItem Content="Killing Gloves" />
                                    </ComboBox>
                                </StackPanel>
                                <StackPanel Grid.Column="1" Margin="18,0,0,0">
                                    <TextBlock Text="Bot IP" Style="{StaticResource HintText}" />
                                    <TextBox x:Name="BotIpInput" Text="127.0.0.1" />
                                    <TextBlock Text="Port" Style="{StaticResource HintText}" />
                                    <TextBox x:Name="PortInput" Text="8766" />
                                    <CheckBox x:Name="AutoReconnectCB" Content="Auto reconnect" Margin="0,12,0,0" />
                                    <StackPanel Orientation="Horizontal" Margin="0,20,0,0">
                                        <Button x:Name="ConnectBtn" Content="Connect" Width="110" Click="ConnectBtn_Click" />
                                        <Button x:Name="StartBtn" Content="Deploy Bot" Width="120" Margin="10,0,0,0" Click="StartBtn_Click" />
                                    </StackPanel>
                                </StackPanel>
                            </Grid>
                        </StackPanel>
                    </Border>
                    <Border Grid.Column="1" Style="{StaticResource CardStyle}">
                        <StackPanel>
                            <TextBlock Text="Bot Settings" Style="{StaticResource TitleText}" />
                            <TextBlock Text="Follow Distance" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <Slider x:Name="FollowDistanceSlider" Minimum="0" Maximum="200" Value="50" TickFrequency="10" IsSnapToTickEnabled="True" />
                            <TextBlock Text="Spy Check Frequency" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <Slider x:Name="SpyCheckFreqSlider" Minimum="1" Maximum="30" Value="8" TickFrequency="1" IsSnapToTickEnabled="True" />
                            <TextBlock Text="Uber Behavior" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <ComboBox x:Name="UberBehaviorCombo" SelectedIndex="0" Margin="0,0,0,10">
                                <ComboBoxItem Content="Manual" />
                                <ComboBoxItem Content="Auto" />
                                <ComboBoxItem Content="Aggressive" />
                            </ComboBox>
                            <TextBlock Text="Startup Mode" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <ComboBox x:Name="StartupModeCombo" SelectedIndex="0" Margin="0,0,0,10">
                                <ComboBoxItem Content="Standard" />
                                <ComboBoxItem Content="Passive" />
                            </ComboBox>
                            <TextBlock Text="Master Volume" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <Slider x:Name="MasterVolumeSlider" Minimum="0" Maximum="100" Value="75" TickFrequency="5" IsSnapToTickEnabled="True" />
                            <Button x:Name="SendConfigBtn" Content="Push Settings" Width="140" Margin="0,20,0,0" Click="SendConfigBtn_Click" />
                            <Border Background="#111418" CornerRadius="12" Padding="14" Margin="0,20,0,0">
                                <StackPanel>
                                    <TextBlock Text="Session" Style="{StaticResource TitleText}" FontSize="13" />
                                    <TextBlock x:Name="SessionTimer" Text="00:00:00" FontSize="26" FontWeight="Bold" Margin="0,10,0,0" />
                                    <TextBlock x:Name="MeleeKillsStatLabel" Text="Melee Kills: 0" Style="{StaticResource HintText}" Margin="0,12,0,0" />
                                    <TextBlock x:Name="TotalHealingStatLabel" Text="Total Healing: 0 HP" Style="{StaticResource HintText}" Margin="0,6,0,0" />
                                </StackPanel>
                            </Border>
                            <TextBlock Text="Respawn Timers" Style="{StaticResource TitleText}" Margin="0,18,0,8" />
                            <ListView x:Name="RespawnTimerList" Background="Transparent" BorderThickness="0" Height="160">
                                <ListView.ItemContainerStyle>
                                    <Style TargetType="ListViewItem">
                                        <Setter Property="Padding" Value="0" />
                                        <Setter Property="Margin" Value="0,0,0,8" />
                                        <Setter Property="Background" Value="Transparent" />
                                        <Setter Property="BorderThickness" Value="0" />
                                    </Style>
                                </ListView.ItemContainerStyle>
                                <ListView.ItemTemplate>
                                    <DataTemplate>
                                        <Border Background="#15171E" CornerRadius="12" Padding="12" BorderBrush="#FFFFFF14" BorderThickness="1">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="48" />
                                                    <ColumnDefinition Width="*" />
                                                    <ColumnDefinition Width="Auto" />
                                                </Grid.ColumnDefinitions>
                                                <Grid Width="42" Height="42" HorizontalAlignment="Center" VerticalAlignment="Center">
                                                    <Ellipse Stroke="#252A34" StrokeThickness="4" />
                                                    <Ellipse Stroke="{Binding Color}" StrokeThickness="4" RenderTransformOrigin="0.5,0.5">
                                                        <Ellipse.RenderTransform>
                                                            <RotateTransform x:Name="TimerRotation" Angle="-90" />
                                                        </Ellipse.RenderTransform>
                                                    </Ellipse>
                                                </Grid>
                                                <StackPanel Grid.Column="1" Margin="12,0,0,0" VerticalAlignment="Center">
                                                    <TextBlock Text="{Binding DisplayText}" FontWeight="SemiBold" />
                                                    <TextBlock Text="Respawn progress" Style="{StaticResource HintText}" />
                                                </StackPanel>
                                                <TextBlock Grid.Column="2" Text="+" Foreground="{Binding Color}" FontSize="18" VerticalAlignment="Center" />
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ListView.ItemTemplate>
                            </ListView>
                        </StackPanel>
                    </Border>
                </Grid>
                <Border Style="{StaticResource CardStyle}" Margin="0,12,0,12">
                    <StackPanel>
                        <TextBlock Text="Activity Feed" Style="{StaticResource TitleText}" />
                        <ScrollViewer VerticalScrollBarVisibility="Auto" Height="220" Background="Transparent" Margin="0,10,0,0">
                            <RichTextBox x:Name="ActivityLog" IsReadOnly="True" Background="Transparent" BorderThickness="0" Foreground="{StaticResource TextSecondaryBrush}" />
                        </ScrollViewer>
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>

        <!-- Priority Tab Content -->
        <ScrollViewer Grid.Row="1" Grid.Column="1" VerticalScrollBarVisibility="Auto" x:Name="PriorityScroller" Margin="12,18,24,18" Visibility="Collapsed">
            <StackPanel x:Name="PriorityContent">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="2.2*" />
                        <ColumnDefinition Width="1.8*" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="*" />
                    </Grid.RowDefinitions>
                    <Border Grid.Row="0" Grid.Column="0" Style="{StaticResource CardStyle}" Margin="0,0,12,12">
                        <StackPanel>
                            <TextBlock Text="Priority Players" Style="{StaticResource TitleText}" />
                            <StackPanel Orientation="Horizontal" Margin="0,10,0,12">
                                <TextBox x:Name="PriorityNameInput" Width="180" Margin="0,0,10,0" />
                                <TextBox x:Name="TierInput" Width="90" Margin="0,0,10,0" Text="1" />
                                <Button x:Name="AddPriority" Content="Add" Width="90" Click="AddPriority_Click" />
                            </StackPanel>
                            <ListView x:Name="PriorityListView" Background="Transparent" BorderThickness="0" Height="230" SelectionMode="Single" ItemContainerStyle="{StaticResource ListBoxItemCardStyle}">
                                <ListView.ItemTemplate>
                                    <DataTemplate>
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="24" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <Ellipse Width="14" Height="14" Fill="#2ED47A" VerticalAlignment="Center" />
                                            <StackPanel Grid.Column="1" Margin="10,0,0,0">
                                                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" />
                                                <TextBlock Style="{StaticResource HintText}">
                                                    <Run Text="Tier " />
                                                    <Run Text="{Binding Tier}" />
                                                    <Run Text=" • " />
                                                    <Run Text="{Binding Deaths}" />
                                                    <Run Text=" deaths" />
                                                </TextBlock>
                                            </StackPanel>
                                            <TextBlock Grid.Column="2" Text="{Binding StatusIcon}" FontSize="16" VerticalAlignment="Center" Foreground="{StaticResource AccentBrush}" />
                                        </Grid>
                                    </DataTemplate>
                                </ListView.ItemTemplate>
                            </ListView>
                            <Button x:Name="RemovePriority" Content="Remove Selected" Width="150" Margin="0,10,0,0" Click="RemovePriority_Click" />
                        </StackPanel>
                    </Border>
                    <Border Grid.Row="0" Grid.Column="1" Style="{StaticResource CardStyle}" Margin="0,0,0,12">
                        <StackPanel>
                            <TextBlock Text="Bot Rules" Style="{StaticResource TitleText}" />
                            <TextBlock Text="Whitelist" Style="{StaticResource HintText}" Margin="0,10,0,4" />
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                                <TextBox x:Name="WhitelistInput" Width="200" />
                                <Button x:Name="AddWhitelist" Content="Add" Width="90" Margin="10,0,0,0" Click="AddWhitelist_Click" />
                                <Button x:Name="RemoveWhitelist" Content="Remove" Width="90" Margin="10,0,0,0" Click="RemoveWhitelist_Click" />
                            </StackPanel>
                            <ListBox x:Name="WhitelistBox" Height="100" Background="#111B27" BorderBrush="#FFFFFF14" BorderThickness="1" Margin="0,0,0,14" ItemContainerStyle="{StaticResource ListBoxItemCardStyle}" />
                            <TextBlock Text="Blacklist" Style="{StaticResource HintText}" Margin="0,0,0,4" />
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                                <TextBox x:Name="BlacklistInput" Width="200" />
                                <Button x:Name="AddBlacklist" Content="Add" Width="90" Margin="10,0,0,0" Click="AddBlacklist_Click" />
                                <Button x:Name="RemoveBlacklist" Content="Remove" Width="90" Margin="10,0,0,0" Click="RemoveBlacklist_Click" />
                            </StackPanel>
                            <Button x:Name="QuickBanBtn" Content="Quick Ban" Width="120" Click="QuickBanBtn_Click" />
                            <ListBox x:Name="BlacklistBox" Height="100" Background="#111B27" BorderBrush="#FFFFFF14" BorderThickness="1" Margin="0,10,0,0" ItemContainerStyle="{StaticResource ListBoxItemCardStyle}" />
                        </StackPanel>
                    </Border>
                </Grid>
            </StackPanel>
        </ScrollViewer>

        <!-- Settings Tab Content -->
        <ScrollViewer Grid.Row="1" Grid.Column="1" VerticalScrollBarVisibility="Auto" x:Name="SettingsScroller" Margin="12,18,24,18" Visibility="Collapsed">
            <StackPanel x:Name="SettingsContent" Margin="0,0,0,12">
                <Border Style="{StaticResource CardStyle}">
                    <StackPanel>
                        <TextBlock Text="Advanced Settings" Style="{StaticResource TitleText}" />
                        <TextBlock Text="System" Style="{StaticResource HintText}" Margin="0,18,0,8" FontWeight="Bold" />
                        <Button x:Name="ResetStatsBtn" Content="Reset Session Stats" Width="200" Margin="0,0,0,8" Click="ResetStatsBtn_Click" />
                        <Button x:Name="CheckUpdatesBtn" Content="Check for Updates" Width="200" Margin="0,0,0,8" Click="CheckUpdatesBtn_Click" />
                        <Button x:Name="UpdateBtn" Content="Download &amp; Install Update" Width="200" Margin="0,0,0,8" Click="UpdateBtn_Click" />
                        <Button x:Name="RollbackBtn" Content="Rollback to Previous Version" Width="200" Margin="0,0,0,16" Click="RollbackBtn_Click" />
                        <TextBlock Text="Audio" Style="{StaticResource HintText}" Margin="0,0,0,8" FontWeight="Bold" />
                        <Button x:Name="UploadSoundsBtn" Content="Upload Custom Sounds" Width="200" Margin="0,0,0,16" Click="UploadSoundsBtn_Click" />
                        <TextBlock Text="Information" Style="{StaticResource HintText}" Margin="0,0,0,8" FontWeight="Bold" />
                        <TextBlock Text="Version: 1.0.0" Style="{StaticResource HintText}" />
                        <TextBlock Text="Python: 3.11.9" Style="{StaticResource HintText}" Margin="0,4,0,0" />
                        <TextBlock Text=".NET Framework: 10.0" Style="{StaticResource HintText}" Margin="0,4,0,0" />
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>

        <!-- Bottom Status Bar -->
        <Border Grid.Row="2" Grid.ColumnSpan="2" Margin="24,0,24,16" Background="#121417" CornerRadius="12" BorderBrush="#FFFFFF14" BorderThickness="1">
            <Grid Margin="16,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Ellipse Width="10" Height="10" Fill="#2ED47A" />
                    <TextBlock Text="Connected" Style="{StaticResource HintText}" Margin="10,0,0,0" />
                    <TextBlock Text="|" Style="{StaticResource HintText}" Margin="10,0,0,0" />
                    <TextBlock Text="Bot state: READY" Style="{StaticResource HintText}" Margin="10,0,0,0" />
                </StackPanel>
                <TextBlock Grid.Column="1" Text="Mode: ACTIVE" Style="{StaticResource HintText}" VerticalAlignment="Center" Margin="30,0,0,0" />
                <TextBlock Grid.Column="2" Text="Uber: 0%" Style="{StaticResource HintText}" VerticalAlignment="Center" Margin="30,0,0,0" />
                <TextBlock Grid.Column="3" Text="{Binding Text, ElementName=SessionTimer}" FontSize="12" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" VerticalAlignment="Center" Margin="30,0,0,0" />
            </Grid>
        </Border>
    </Grid>
</Window>
'@

$xamlContent | Out-File -FilePath "C:\Users\nikol\OneDrive\Desktop\MedicAI\gui\MainWindow.xaml" -Encoding UTF8 -Force
Write-Host "MainWindow.xaml updated successfully!"
