using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace TM.Framework.Appearance.ThemeManagement
{
    public static class BuiltInThemes
    {
        private static readonly Dictionary<ThemeType, string> _themes = new()
        {
            [ThemeType.Light] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 浅色主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#F1F5F9"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="Surface" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#CBD5E1"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#E2E8F0"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#1E293B"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#64748B"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#94A3B8"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#CBD5E1"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#E2E8F0"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#CBD5E1"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#E0E7FF"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#3B82F6"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#2563EB"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#1D4ED8"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#10B981"/>
    <SolidColorBrush x:Key="WarningColor" Color="#F59E0B"/>
    <SolidColorBrush x:Key="DangerColor" Color="#EF4444"/>
    <SolidColorBrush x:Key="DangerHover" Color="#DC2626"/>
    <SolidColorBrush x:Key="InfoColor" Color="#3B82F6"/>
</ResourceDictionary>
""",

            [ThemeType.Green] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 护眼色主题（牛皮纸暖棕调，参考 Kindle/iBooks Sepia + Figma Parchment） -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#E8DCC8"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F5EDDC"/>
    <SolidColorBrush x:Key="Surface" Color="#F5EDDC"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F5EDDC"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#C9BEA3"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#D8CCBA"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#4A3728"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#6B5744"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#998B78"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#BEB0A0"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#E2D5C2"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#D8CCBA"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#DDD0BC"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#8B6914"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#7A5C0F"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#69500A"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#6B8E5A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#C89030"/>
    <SolidColorBrush x:Key="DangerColor" Color="#C0543C"/>
    <SolidColorBrush x:Key="DangerHover" Color="#A8462F"/>
    <SolidColorBrush x:Key="InfoColor" Color="#7B8FA8"/>
</ResourceDictionary>
""",

            [ThemeType.Dark] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 深色主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#0F172A"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#1E293B"/>
    <SolidColorBrush x:Key="Surface" Color="#1E293B"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#1E293B"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#334155"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#334155"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#F1F5F9"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#94A3B8"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#64748B"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#475569"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#334155"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#475569"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#1E3A5F"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#60A5FA"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#3B82F6"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#2563EB"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#34D399"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FBBF24"/>
    <SolidColorBrush x:Key="DangerColor" Color="#F87171"/>
    <SolidColorBrush x:Key="DangerHover" Color="#EF4444"/>
    <SolidColorBrush x:Key="InfoColor" Color="#60A5FA"/>
</ResourceDictionary>
""",

            [ThemeType.Arctic] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 北极蓝主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#E8F0FE"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F0F7FF"/>
    <SolidColorBrush x:Key="Surface" Color="#F0F7FF"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F0F7FF"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#B0C4DE"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#C8D8E8"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#1A365D"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#2D5087"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#6889AB"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#A0B4C8"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#D6E4F0"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#C8D8E8"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#BDD0E7"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#0284C7"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#0369A1"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#075985"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#059669"/>
    <SolidColorBrush x:Key="WarningColor" Color="#D97706"/>
    <SolidColorBrush x:Key="DangerColor" Color="#DC2626"/>
    <SolidColorBrush x:Key="DangerHover" Color="#B91C1C"/>
    <SolidColorBrush x:Key="InfoColor" Color="#0284C7"/>
</ResourceDictionary>
""",

            [ThemeType.Forest] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 森林绿主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#E8F5E9"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F1F8F2"/>
    <SolidColorBrush x:Key="Surface" Color="#F1F8F2"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F1F8F2"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#A5C8A8"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#C3DBC5"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#1B3A1D"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#3E6B42"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#6B9E6E"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#A5C8A8"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#D4E8D5"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#C3DBC5"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#B8D4BA"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#2E7D32"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#1B5E20"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#134B17"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#43A047"/>
    <SolidColorBrush x:Key="WarningColor" Color="#F9A825"/>
    <SolidColorBrush x:Key="DangerColor" Color="#E53935"/>
    <SolidColorBrush x:Key="DangerHover" Color="#C62828"/>
    <SolidColorBrush x:Key="InfoColor" Color="#1976D2"/>
</ResourceDictionary>
""",

            [ThemeType.Violet] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 紫罗兰主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#F0E8F5"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F8F0FF"/>
    <SolidColorBrush x:Key="Surface" Color="#F8F0FF"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F8F0FF"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#C8A8E0"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#DCC8EC"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#2D1B4E"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#5B3E8A"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#8B6DB8"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#B8A0D0"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#E8D8F2"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#DCC8EC"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#D0BBE5"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#7C3AED"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#6D28D9"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#5B21B6"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#10B981"/>
    <SolidColorBrush x:Key="WarningColor" Color="#F59E0B"/>
    <SolidColorBrush x:Key="DangerColor" Color="#EF4444"/>
    <SolidColorBrush x:Key="DangerHover" Color="#DC2626"/>
    <SolidColorBrush x:Key="InfoColor" Color="#8B5CF6"/>
</ResourceDictionary>
""",

            [ThemeType.Business] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 商务灰主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#EDEDED"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F7F7F7"/>
    <SolidColorBrush x:Key="Surface" Color="#F7F7F7"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F7F7F7"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#BFBFBF"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#D4D4D4"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#262626"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#595959"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#8C8C8C"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#BFBFBF"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#E0E0E0"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#D4D4D4"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#D0D8E0"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#4A6FA5"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#3D5D8C"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#304B73"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FAAD14"/>
    <SolidColorBrush x:Key="DangerColor" Color="#FF4D4F"/>
    <SolidColorBrush x:Key="DangerHover" Color="#D9363E"/>
    <SolidColorBrush x:Key="InfoColor" Color="#4A6FA5"/>
</ResourceDictionary>
""",

            [ThemeType.MinimalBlack] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 极简黑主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#121212"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#1A1A1A"/>
    <SolidColorBrush x:Key="Surface" Color="#1A1A1A"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#1A1A1A"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#333333"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#2A2A2A"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#E8E8E8"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#A0A0A0"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#707070"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#4A4A4A"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#2A2A2A"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#333333"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#2D2D2D"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#6CB6FF"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#539BF5"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#4184E4"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#3FB950"/>
    <SolidColorBrush x:Key="WarningColor" Color="#D29922"/>
    <SolidColorBrush x:Key="DangerColor" Color="#F85149"/>
    <SolidColorBrush x:Key="DangerHover" Color="#DA3633"/>
    <SolidColorBrush x:Key="InfoColor" Color="#6CB6FF"/>
</ResourceDictionary>
""",

            [ThemeType.ModernBlue] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 现代深蓝主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#0A1628"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#112240"/>
    <SolidColorBrush x:Key="Surface" Color="#112240"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#112240"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#1E3A5F"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#1E3A5F"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#E2E8F0"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#8892B0"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#606D80"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#3D4F65"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#1A2D4A"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#1E3A5F"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#172E4F"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#1890FF"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#40A9FF"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#096DD9"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FAAD14"/>
    <SolidColorBrush x:Key="DangerColor" Color="#FF4D4F"/>
    <SolidColorBrush x:Key="DangerHover" Color="#FF7875"/>
    <SolidColorBrush x:Key="InfoColor" Color="#1890FF"/>
</ResourceDictionary>
""",

            [ThemeType.WarmOrange] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 暖阳橙主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#FFF0E0"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#FFF7E6"/>
    <SolidColorBrush x:Key="Surface" Color="#FFF7E6"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#FFF7E6"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#F0C8A0"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#F5D8B8"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#5C3A18"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#8C6540"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#B08060"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#D4B8A0"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#FFEDD0"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#F5D8B8"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#FFE4C0"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#E8780A"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#D06A05"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#B85C00"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FA8C16"/>
    <SolidColorBrush x:Key="DangerColor" Color="#F5222D"/>
    <SolidColorBrush x:Key="DangerHover" Color="#CF1322"/>
    <SolidColorBrush x:Key="InfoColor" Color="#1890FF"/>
</ResourceDictionary>
""",

            [ThemeType.Pink] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 樱花粉主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#FDE8EF"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#FFF0F6"/>
    <SolidColorBrush x:Key="Surface" Color="#FFF0F6"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#FFF0F6"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#F0B0C8"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#F5C8D8"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#4A1030"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#7A3055"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#A86080"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#D0A0B0"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#FFE0EB"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#F5C8D8"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#FFD6E5"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#EB2F96"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#C41D7F"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#9E1068"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FAAD14"/>
    <SolidColorBrush x:Key="DangerColor" Color="#FF4D4F"/>
    <SolidColorBrush x:Key="DangerHover" Color="#CF1322"/>
    <SolidColorBrush x:Key="InfoColor" Color="#1890FF"/>
</ResourceDictionary>
""",

            [ThemeType.TechCyan] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 科技青主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#0A1929"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#0D2137"/>
    <SolidColorBrush x:Key="Surface" Color="#0D2137"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#0D2137"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#1A3A50"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#1A3A50"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#E0F0F0"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#88B0B8"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#608088"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#384850"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#122E42"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#1A3A50"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#153348"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#13C2C2"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#36CFC9"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#08979C"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FAAD14"/>
    <SolidColorBrush x:Key="DangerColor" Color="#FF4D4F"/>
    <SolidColorBrush x:Key="DangerHover" Color="#FF7875"/>
    <SolidColorBrush x:Key="InfoColor" Color="#13C2C2"/>
</ResourceDictionary>
""",

            [ThemeType.Sunset] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 日落橙主题 -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#FDE8D8"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#FFF4EC"/>
    <SolidColorBrush x:Key="Surface" Color="#FFF4EC"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#FFF4EC"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#E0B8A0"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#F0D0C0"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#5C2E18"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#8C5A3C"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#B08060"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#D0A890"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#FFE8D5"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#F0D0C0"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#FFDFC8"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#E85D26"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#D04E1A"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#B84010"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#52C41A"/>
    <SolidColorBrush x:Key="WarningColor" Color="#FA8C16"/>
    <SolidColorBrush x:Key="DangerColor" Color="#F5222D"/>
    <SolidColorBrush x:Key="DangerHover" Color="#CF1322"/>
    <SolidColorBrush x:Key="InfoColor" Color="#1890FF"/>
</ResourceDictionary>
""",

            [ThemeType.Morandi] = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 莫兰迪主题（低饱和优雅风） -->
    <SolidColorBrush x:Key="UnifiedBackground" Color="#E8E4E0"/>
    <SolidColorBrush x:Key="ContentBackground" Color="#F5F4F2"/>
    <SolidColorBrush x:Key="Surface" Color="#F5F4F2"/>
    <SolidColorBrush x:Key="ContentHighlight" Color="#F5F4F2"/>
    <SolidColorBrush x:Key="WindowBorder" Color="#C0BAB5"/>
    <SolidColorBrush x:Key="BorderBrush" Color="#D0CBC5"/>
    <SolidColorBrush x:Key="TextPrimary" Color="#4A4845"/>
    <SolidColorBrush x:Key="TextSecondary" Color="#6B6865"/>
    <SolidColorBrush x:Key="TextTertiary" Color="#908D88"/>
    <SolidColorBrush x:Key="TextDisabled" Color="#B5B0AB"/>
    <SolidColorBrush x:Key="HoverBackground" Color="#E0DCD8"/>
    <SolidColorBrush x:Key="ActiveBackground" Color="#D0CBC5"/>
    <SolidColorBrush x:Key="SelectedBackground" Color="#D5D0CA"/>
    <SolidColorBrush x:Key="PrimaryColor" Color="#7C9299"/>
    <SolidColorBrush x:Key="PrimaryHover" Color="#6A8088"/>
    <SolidColorBrush x:Key="PrimaryActive" Color="#586E75"/>
    <SolidColorBrush x:Key="SuccessColor" Color="#7BA67D"/>
    <SolidColorBrush x:Key="WarningColor" Color="#C4A35A"/>
    <SolidColorBrush x:Key="DangerColor" Color="#C07070"/>
    <SolidColorBrush x:Key="DangerHover" Color="#A85D5D"/>
    <SolidColorBrush x:Key="InfoColor" Color="#7C9299"/>
</ResourceDictionary>
"""
        };

        public static bool IsBuiltIn(ThemeType themeType)
        {
            return _themes.ContainsKey(themeType);
        }

        public static ResourceDictionary? CreateResourceDictionary(ThemeType themeType)
        {
            if (!_themes.TryGetValue(themeType, out var xaml))
                return null;

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
            var dict = (ResourceDictionary)XamlReader.Load(stream);
            return dict;
        }
    }
}
