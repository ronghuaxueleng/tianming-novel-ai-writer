using System;
using System.Collections.Generic;

namespace TM.Framework.Notifications.Sound.Services
{
    public class AudioEqualizerService
    {
        private const double BassFrequency = 60;
        private const double MidBassFrequency = 230;
        private const double MidFrequency = 910;
        private const double MidTrebleFrequency = 3600;
        private const double TrebleFrequency = 14000;

        private double _bassGain = 0;
        private double _midBassGain = 0;
        private double _midGain = 0;
        private double _midTrebleGain = 0;
        private double _trebleGain = 0;

        private bool _isEnabled = false;

        public AudioEqualizerService()
        {
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                App.Log($"[AudioEqualizerService] 均衡器状态: {(value ? "已启用" : "已禁用")}");
                ApplyEqualizer();
            }
        }

        public void SetBassGain(double gain)
        {
            _bassGain = Math.Clamp(gain, -12, 12);
            App.Log($"[AudioEqualizerService] 低音增益: {_bassGain:F1}dB");
            ApplyEqualizer();
        }

        public void SetMidBassGain(double gain)
        {
            _midBassGain = Math.Clamp(gain, -12, 12);
            App.Log($"[AudioEqualizerService] 中低音增益: {_midBassGain:F1}dB");
            ApplyEqualizer();
        }

        public void SetMidGain(double gain)
        {
            _midGain = Math.Clamp(gain, -12, 12);
            App.Log($"[AudioEqualizerService] 中音增益: {_midGain:F1}dB");
            ApplyEqualizer();
        }

        public void SetMidTrebleGain(double gain)
        {
            _midTrebleGain = Math.Clamp(gain, -12, 12);
            App.Log($"[AudioEqualizerService] 中高音增益: {_midTrebleGain:F1}dB");
            ApplyEqualizer();
        }

        public void SetTrebleGain(double gain)
        {
            _trebleGain = Math.Clamp(gain, -12, 12);
            App.Log($"[AudioEqualizerService] 高音增益: {_trebleGain:F1}dB");
            ApplyEqualizer();
        }

        public void ApplyPreset(string presetName)
        {
            var presets = GetPresets();

            if (presets.TryGetValue(presetName, out var preset))
            {
                _bassGain = preset.bass;
                _midBassGain = preset.midBass;
                _midGain = preset.mid;
                _midTrebleGain = preset.midTreble;
                _trebleGain = preset.treble;

                App.Log($"[AudioEqualizerService] 应用预设: {presetName}");
                ApplyEqualizer();
            }
            else
            {
                App.Log($"[AudioEqualizerService] 未找到预设: {presetName}");
            }
        }

        public void Reset()
        {
            _bassGain = 0;
            _midBassGain = 0;
            _midGain = 0;
            _midTrebleGain = 0;
            _trebleGain = 0;

            App.Log("[AudioEqualizerService] 均衡器已重置");
            ApplyEqualizer();
        }

        public (double bass, double midBass, double mid, double midTreble, double treble) GetCurrentSettings()
        {
            return (_bassGain, _midBassGain, _midGain, _midTrebleGain, _trebleGain);
        }

        public Dictionary<string, (double bass, double midBass, double mid, double midTreble, double treble)> GetPresets()
        {
            return new Dictionary<string, (double, double, double, double, double)>
            {
                ["默认"] = (0, 0, 0, 0, 0),
                ["流行"] = (3, 1, 0, 1, 3),
                ["摇滚"] = (5, 2, -1, 2, 4),
                ["古典"] = (-2, -1, 0, 2, 3),
                ["爵士"] = (3, 1, 1, 1, 2),
                ["电子"] = (5, 3, 0, 1, 5),
                ["低音增强"] = (8, 5, 0, 0, 0),
                ["人声增强"] = (-2, 0, 4, 3, -1),
                ["柔和"] = (-3, -2, 0, 2, 3)
            };
        }

        private void ApplyEqualizer()
        {
            if (!_isEnabled)
            {
                App.Log("[AudioEqualizerService] 均衡器未启用，跳过应用");
                return;
            }

            App.Log($"[AudioEqualizerService] 均衡器设置 - 低音:{_bassGain:F1}dB 中低音:{_midBassGain:F1}dB 中音:{_midGain:F1}dB 中高音:{_midTrebleGain:F1}dB 高音:{_trebleGain:F1}dB");
            App.Log("[AudioEqualizerService] EQ配置已记录（需要音频库升级才能应用实时效果）");
        }
    }
}

