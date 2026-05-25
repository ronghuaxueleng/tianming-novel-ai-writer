namespace TM.Modules.Design.Templates.CreativeMaterials
{
    public class BookAnalysisOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(Author) ? Name : $"{Name} ← {Author}";
    }

    public class GenreInfo
    {
        public string Name { get; set; } = string.Empty;
        public System.Windows.Media.ImageSource? Icon { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Elements { get; set; } = string.Empty;
        public string Avoidances { get; set; } = string.Empty;

        public string ToPromptString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Name}（{Description}，核心元素：{Elements}");
            if (!string.IsNullOrWhiteSpace(Avoidances))
                sb.Append($"，必须避免：{Avoidances}");
            sb.Append('）');
            return sb.ToString();
        }
    }
}
