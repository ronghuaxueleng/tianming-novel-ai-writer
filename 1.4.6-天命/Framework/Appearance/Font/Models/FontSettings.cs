using System;
using System.Reflection;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Appearance.Font.Models
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TextRenderingMode
    {
        Auto,
        Aliased,
        Grayscale,
        ClearType
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TextFormattingMode
    {
        Ideal,
        Display
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum TextHintingMode
    {
        Auto,
        Fixed
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class FontSettings : INotifyPropertyChanged
    {
        private string _fontFamily = "Microsoft YaHei UI";
        private double _fontSize = 14;
        private string _fontWeight = "Normal";
        private double _lineHeight = 1.5;
        private double _letterSpacing = 0;
        private TextRenderingMode _textRenderingMode = TextRenderingMode.Auto;
        private TextFormattingMode _textFormattingMode = TextFormattingMode.Ideal;
        private TextHintingMode _textHintingMode = TextHintingMode.Auto;
        private bool _enableLigatures = false;

        private bool _showZeroWidthChars = false;
        private bool _visualizeWhitespace = false;
        private string _tabSymbol = "→";
        private string _spaceSymbol = "·";

        [JsonPropertyName("fontFamily")]
        public string FontFamily
        {
            get => _fontFamily;
            set
            {
                if (_fontFamily != value)
                {
                    _fontFamily = value;
                    OnPropertyChanged(nameof(FontFamily));
                }
            }
        }

        [JsonPropertyName("fontSize")]
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    OnPropertyChanged(nameof(FontSize));
                }
            }
        }

        [JsonPropertyName("fontWeight")]
        public string FontWeight
        {
            get => _fontWeight;
            set
            {
                if (_fontWeight != value)
                {
                    _fontWeight = value;
                    OnPropertyChanged(nameof(FontWeight));
                }
            }
        }

        [JsonPropertyName("lineHeight")]
        public double LineHeight
        {
            get => _lineHeight;
            set
            {
                if (_lineHeight != value)
                {
                    _lineHeight = value;
                    OnPropertyChanged(nameof(LineHeight));
                }
            }
        }

        [JsonPropertyName("letterSpacing")]
        public double LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                if (_letterSpacing != value)
                {
                    _letterSpacing = value;
                    OnPropertyChanged(nameof(LetterSpacing));
                }
            }
        }

        [JsonPropertyName("textRenderingMode")]
        public TextRenderingMode TextRendering
        {
            get => _textRenderingMode;
            set
            {
                if (_textRenderingMode != value)
                {
                    _textRenderingMode = value;
                    OnPropertyChanged(nameof(TextRendering));
                }
            }
        }

        [JsonPropertyName("textFormattingMode")]
        public TextFormattingMode TextFormatting
        {
            get => _textFormattingMode;
            set
            {
                if (_textFormattingMode != value)
                {
                    _textFormattingMode = value;
                    OnPropertyChanged(nameof(TextFormatting));
                }
            }
        }

        [JsonPropertyName("textHintingMode")]
        public TextHintingMode TextHinting
        {
            get => _textHintingMode;
            set
            {
                if (_textHintingMode != value)
                {
                    _textHintingMode = value;
                    OnPropertyChanged(nameof(TextHinting));
                }
            }
        }

        [JsonPropertyName("enableLigatures")]
        public bool EnableLigatures
        {
            get => _enableLigatures;
            set
            {
                if (_enableLigatures != value)
                {
                    _enableLigatures = value;
                    OnPropertyChanged(nameof(EnableLigatures));
                }
            }
        }

        [JsonPropertyName("showZeroWidthChars")]
        public bool ShowZeroWidthChars
        {
            get => _showZeroWidthChars;
            set
            {
                if (_showZeroWidthChars != value)
                {
                    _showZeroWidthChars = value;
                    OnPropertyChanged(nameof(ShowZeroWidthChars));
                }
            }
        }

        [JsonPropertyName("visualizeWhitespace")]
        public bool VisualizeWhitespace
        {
            get => _visualizeWhitespace;
            set
            {
                if (_visualizeWhitespace != value)
                {
                    _visualizeWhitespace = value;
                    OnPropertyChanged(nameof(VisualizeWhitespace));
                }
            }
        }

        [JsonPropertyName("tabSymbol")]
        public string TabSymbol
        {
            get => _tabSymbol;
            set
            {
                if (_tabSymbol != value)
                {
                    _tabSymbol = value;
                    OnPropertyChanged(nameof(TabSymbol));
                }
            }
        }

        [JsonPropertyName("spaceSymbol")]
        public string SpaceSymbol
        {
            get => _spaceSymbol;
            set
            {
                if (_spaceSymbol != value)
                {
                    _spaceSymbol = value;
                    OnPropertyChanged(nameof(SpaceSymbol));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FontSettings Clone()
        {
            return new FontSettings
            {
                FontFamily = this.FontFamily,
                FontSize = this.FontSize,
                FontWeight = this.FontWeight,
                LineHeight = this.LineHeight,
                LetterSpacing = this.LetterSpacing,
                TextRendering = this.TextRendering,
                TextFormatting = this.TextFormatting,
                TextHinting = this.TextHinting,
                EnableLigatures = this.EnableLigatures,
                ShowZeroWidthChars = this.ShowZeroWidthChars,
                VisualizeWhitespace = this.VisualizeWhitespace,
                TabSymbol = this.TabSymbol,
                SpaceSymbol = this.SpaceSymbol
            };
        }
    }

    public class FontConfiguration
    {
        [JsonPropertyName("uiFont")]
        public FontSettings UIFont { get; set; } = new();

        [JsonPropertyName("editorFont")]
        public FontSettings EditorFont { get; set; } = new();

        public static FontConfiguration GetDefault()
        {
            return new FontConfiguration
            {
                UIFont = new FontSettings
                {
                    FontFamily = "Microsoft YaHei UI",
                    FontSize = 14,
                    FontWeight = "Normal",
                    LineHeight = 1.5,
                    LetterSpacing = 0
                },
                EditorFont = new FontSettings
                {
                    FontFamily = "Consolas",
                    FontSize = 13,
                    FontWeight = "Normal",
                    LineHeight = 1.6,
                    LetterSpacing = 0.5
                }
            };
        }
    }
}

