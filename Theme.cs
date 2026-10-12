// layerpeel 3.0 —— 解压 · 去套娃助手（GPL-3.0，详见 LICENSE）
//
// 设计系统：调色板 + 控件样式库，全部写在运行时 XAML 里（XamlReader.Parse）。
// 这样既能用完整的 ControlTemplate（WPF 默认模板不吃 ComboBox.Background 这类属性），
// 又不需要 MSBuild / XAML 编译 / .NET SDK —— 一个 exe、系统自带 csc.exe 搞定。
//
// 配色与视觉语言参考工作区里的 `WPF界面模板`（深色调色板 + 卡片化 + 无边框标题栏）。
// 改颜色只改下面 PALETTE 段：所有样式只引用 B.* 笔刷，不写死颜色。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;

namespace LayerPeel
{
    public static class Theme
    {
        // 图标字体（Windows 自带）
        public const string IconFont = "Segoe MDL2 Assets, Segoe Fluent Icons";

        // 常用图标码位（MDL2 Assets）
        public const string IcoFolder = "\uE8B7";
        public const string IcoFlatten = "\uE71D";
        public const string IcoDelete = "\uE74D";
        public const string IcoLock = "\uE72E";
        public const string IcoUnlock = "\uE785";
        public const string IcoPlay = "\uE768";
        public const string IcoStop = "\uE71A";
        public const string IcoAdd = "\uE710";
        public const string IcoRefresh = "\uE72C";
        public const string IcoSettings = "\uE713";
        public const string IcoUndo = "\uE7A7";
        public const string IcoDoc = "\uE8A5";
        public const string IcoMin = "\uE921";
        public const string IcoMax = "\uE922";
        public const string IcoRestore = "\uE923";
        public const string IcoClose = "\uE8BB";
        public const string IcoCheck = "\uE73E";
        public const string IcoWarn = "\uE7BA";

        const string Xaml = @"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- ============================================================
       调色板（改颜色只改这里）
       ============================================================ -->
  <SolidColorBrush x:Key='B.Bg.Window'    Color='#FF0E1117'/>
  <SolidColorBrush x:Key='B.Bg.Panel'     Color='#FF151A23'/>
  <SolidColorBrush x:Key='B.Bg.PanelAlt'  Color='#FF1A2029'/>
  <SolidColorBrush x:Key='B.Bg.Elevated'  Color='#FF1F2630'/>
  <SolidColorBrush x:Key='B.Bg.Input'     Color='#FF12161D'/>
  <SolidColorBrush x:Key='B.Bg.Hover'     Color='#FF202834'/>
  <SolidColorBrush x:Key='B.Bg.Selected'  Color='#FF1C2B45'/>

  <SolidColorBrush x:Key='B.Border'       Color='#FF242B37'/>
  <SolidColorBrush x:Key='B.BorderSoft'   Color='#FF1B212A'/>
  <SolidColorBrush x:Key='B.BorderStrong' Color='#FF323B4A'/>

  <SolidColorBrush x:Key='B.Text.Primary'   Color='#FFE8ECF3'/>
  <SolidColorBrush x:Key='B.Text.Secondary' Color='#FF9BA5B7'/>
  <SolidColorBrush x:Key='B.Text.Muted'     Color='#FF6D778A'/>

  <SolidColorBrush x:Key='B.Accent'        Color='#FF4C8DFF'/>
  <SolidColorBrush x:Key='B.AccentHover'   Color='#FF6BA1FF'/>
  <SolidColorBrush x:Key='B.AccentPressed' Color='#FF3B76DB'/>
  <SolidColorBrush x:Key='B.AccentSoft'    Color='#FF1E2B44'/>
  <SolidColorBrush x:Key='B.ScrollThumb'   Color='#FF3A4356'/>

  <SolidColorBrush x:Key='B.Success'      Color='#FF3DD68C'/>
  <SolidColorBrush x:Key='B.Success.Soft' Color='#FF16352A'/>
  <SolidColorBrush x:Key='B.Warn'         Color='#FFF5B93F'/>
  <SolidColorBrush x:Key='B.Warn.Soft'    Color='#FF35290E'/>
  <SolidColorBrush x:Key='B.Danger'       Color='#FFF0666B'/>
  <SolidColorBrush x:Key='B.Danger.Soft'  Color='#FF351A1D'/>
  <SolidColorBrush x:Key='B.Info'         Color='#FF4EC9F5'/>
  <SolidColorBrush x:Key='B.Info.Soft'    Color='#FF152F3A'/>

  <FontFamily x:Key='Font.Ui'>Microsoft YaHei UI, Segoe UI</FontFamily>
  <FontFamily x:Key='Font.Icon'>Segoe MDL2 Assets, Segoe Fluent Icons</FontFamily>
  <FontFamily x:Key='Font.Mono'>Consolas, Cascadia Mono</FontFamily>

  <!-- ============================================================
       文本
       ============================================================ -->
  <Style x:Key='PageTitle' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='20'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
  </Style>
  <Style x:Key='PageSub' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
    <Setter Property='Margin' Value='0,4,0,0'/>
    <Setter Property='TextWrapping' Value='Wrap'/>
  </Style>
  <Style x:Key='CardTitle' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
  </Style>
  <Style x:Key='Muted' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Muted}'/>
  </Style>
  <Style x:Key='FieldLabel' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='11'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Muted}'/>
    <Setter Property='Margin' Value='0,0,0,5'/>
  </Style>
  <Style x:Key='StatValue' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='22'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
  </Style>

  <!-- ============================================================
       卡片
       ============================================================ -->
  <Style x:Key='Card' TargetType='Border'>
    <Setter Property='Background' Value='{StaticResource B.Bg.Elevated}'/>
    <Setter Property='BorderBrush' Value='{StaticResource B.Border}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='10'/>
    <Setter Property='Padding' Value='16,14,16,14'/>
  </Style>

  <!-- ============================================================
       滚动条
       ============================================================ -->
  <Style x:Key='FkThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border Background='{StaticResource B.ScrollThumb}' CornerRadius='4' Margin='2'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb><Thumb Style='{StaticResource FkThumb}'/></Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' Opacity='0'/>
              </Track.IncreaseRepeatButton>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' Opacity='0'/>
              </Track.DecreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Orientation' Value='Horizontal'>
              <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Height' Value='10'/>
        <Setter Property='Width' Value='Auto'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- ============================================================
       窗口按钮（标题栏用）
       ============================================================ -->
  <Style x:Key='WindowButton' TargetType='Button'>
    <Setter Property='Width' Value='40'/>
    <Setter Property='Height' Value='30'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Hover}'/>
              <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='WindowCloseButton' TargetType='Button' BasedOn='{StaticResource WindowButton}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Danger}'/>
              <Setter Property='Foreground' Value='White'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================
       普通按钮
       ============================================================ -->
  <Style x:Key='BtnGhost' TargetType='Button'>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='Padding' Value='12,6,12,6'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='{StaticResource B.Bg.PanelAlt}'
                  BorderBrush='{StaticResource B.BorderStrong}' BorderThickness='1'
                  CornerRadius='7' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Hover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BtnPrimary' TargetType='Button' BasedOn='{StaticResource BtnGhost}'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Padding' Value='16,7,16,7'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='{StaticResource B.Accent}' CornerRadius='7'
                  Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.AccentHover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BtnDanger' TargetType='Button' BasedOn='{StaticResource BtnGhost}'>
    <Setter Property='Foreground' Value='{StaticResource B.Danger}'/>
  </Style>
  <Style x:Key='IconButton' TargetType='Button'>
    <Setter Property='Width' Value='30'/>
    <Setter Property='Height' Value='30'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='B' Background='Transparent' CornerRadius='6'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Hover}'/>
              <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================
       输入框 / 下拉框 / 复选框
       ============================================================ -->
  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='{StaticResource B.Bg.Input}'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='BorderBrush' Value='{StaticResource B.Border}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CaretBrush' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='SelectionBrush' Value='{StaticResource B.Accent}'/>
    <Setter Property='Padding' Value='8,5,8,5'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='B' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'
                          VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocusWithin' Value='True'>
              <Setter TargetName='B' Property='BorderBrush' Value='{StaticResource B.Accent}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='B' Background='{StaticResource B.Bg.Input}'
                          BorderBrush='{StaticResource B.Border}' BorderThickness='1' CornerRadius='6'>
                    <Grid>
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='*'/>
                        <ColumnDefinition Width='22'/>
                      </Grid.ColumnDefinitions>
                      <Path Grid.Column='1' HorizontalAlignment='Center' VerticalAlignment='Center'
                            Data='M0,0 L8,0 L4,5 Z' Fill='{StaticResource B.Text.Muted}'/>
                    </Grid>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='B' Property='BorderBrush' Value='{StaticResource B.BorderStrong}'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin='9,0,26,0' VerticalAlignment='Center' IsHitTestVisible='False'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
            <Popup Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom'
                   AllowsTransparency='True' Focusable='False'>
              <Border Background='{StaticResource B.Bg.Elevated}'
                      BorderBrush='{StaticResource B.BorderStrong}' BorderThickness='1' CornerRadius='6'
                      Margin='0,3,0,0' MinWidth='{TemplateBinding ActualWidth}' MaxHeight='260'>
                <ScrollViewer><StackPanel IsItemsHost='True' Margin='3'/></ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='Padding' Value='8,6,8,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='B' Background='Transparent' CornerRadius='5'
                  Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Selected}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='Box' Width='15' Height='15' CornerRadius='4'
                    Background='{StaticResource B.Bg.Input}'
                    BorderBrush='{StaticResource B.BorderStrong}' BorderThickness='1'
                    VerticalAlignment='Center'>
              <TextBlock x:Name='Tick' Text='&#xE73E;' FontFamily='{StaticResource Font.Icon}'
                         FontSize='10' Foreground='White' Visibility='Collapsed'
                         HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Box' Property='Background' Value='{StaticResource B.Accent}'/>
              <Setter TargetName='Box' Property='BorderBrush' Value='{StaticResource B.Accent}'/>
              <Setter TargetName='Tick' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================
       左侧导航
       ============================================================ -->
  <Style x:Key='NavList' TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='8,0,8,0'/>
    <Setter Property='ItemContainerStyle'>
      <Setter.Value>
        <Style TargetType='ListBoxItem'>
          <Setter Property='Height' Value='40'/>
          <Setter Property='Margin' Value='0,1,0,1'/>
          <Setter Property='Foreground' Value='{StaticResource B.Text.Secondary}'/>
          <Setter Property='Cursor' Value='Hand'/>
          <Setter Property='Template'>
            <Setter.Value>
              <ControlTemplate TargetType='ListBoxItem'>
                <Grid>
                  <Border x:Name='B' Background='Transparent' CornerRadius='7'/>
                  <Border x:Name='Bar' Width='3' CornerRadius='2' Margin='2,9,0,9'
                          HorizontalAlignment='Left' Background='{StaticResource B.Accent}'
                          Visibility='Collapsed'/>
                  <ContentPresenter Margin='14,0,10,0' VerticalAlignment='Center'/>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Hover}'/>
                  </Trigger>
                  <Trigger Property='IsSelected' Value='True'>
                    <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Selected}'/>
                    <Setter TargetName='Bar' Property='Visibility' Value='Visible'/>
                    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================
       普通列表（目标列表 / 权限备份列表）
       ============================================================ -->
  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='{StaticResource B.Bg.Input}'/>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='BorderBrush' Value='{StaticResource B.Border}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBox'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='4'>
            <ScrollViewer><ItemsPresenter/></ScrollViewer>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{StaticResource B.Text.Primary}'/>
    <Setter Property='Padding' Value='6,4,6,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='B' Background='Transparent' CornerRadius='4'
                  Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='B' Property='Background' Value='{StaticResource B.Bg.Selected}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================
       进度条
       ============================================================ -->
  <Style TargetType='ProgressBar'>
    <Setter Property='Height' Value='4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Border Background='{StaticResource B.Bg.Input}' CornerRadius='2'>
            <Grid>
              <Rectangle x:Name='PART_Track'/>
              <Border x:Name='PART_Indicator' Background='{StaticResource B.Accent}'
                      CornerRadius='2' HorizontalAlignment='Left'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>
";

        static ResourceDictionary _dict;
        static bool _failed;

        public static void Apply(Window w)
        {
            try
            {
                _dict = (ResourceDictionary)XamlReader.Parse(Xaml);
                w.Resources.MergedDictionaries.Add(_dict);
            }
            catch (Exception e)
            {
                _failed = true;
                AppLog.Write("主题加载失败（界面会用系统默认外观）：" + e.Message);
            }
        }

        /// <summary>取调色板笔刷；取不到就退回白色，保证界面不会崩</summary>
        public static Brush B(string key)
        {
            if (_dict == null) return Brushes.White;
            object o = _dict[key];
            return (o as Brush) ?? Brushes.White;
        }

        /// <summary>取样式；取不到返回 null（控件用默认外观）</summary>
        public static Style S(string key)
        {
            if (_dict == null) return null;
            return _dict[key] as Style;
        }

        public static bool Failed { get { return _failed; } }
    }
}
