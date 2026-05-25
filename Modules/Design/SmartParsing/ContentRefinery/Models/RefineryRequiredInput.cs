using System;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Models
{
    public class RefineryRequiredInput
    {
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string Placeholder { get; set; } = string.Empty;

        public bool IsRequired { get; set; } = true;

        public Func<string, bool>? Validator { get; set; }
    }
}
