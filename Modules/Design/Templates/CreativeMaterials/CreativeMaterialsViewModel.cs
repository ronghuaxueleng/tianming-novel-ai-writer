using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.SmartParsing.BookAnalysis.Services;
using TM.Modules.Design.Templates.CreativeMaterials.Services;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.CreativeMaterials
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class CreativeMaterialsViewModel : DataManagementViewModelBase<CreativeMaterialData, CreativeMaterialCategory, CreativeMaterialsService>
    {
        private readonly IPromptRepository _promptRepository;
        private readonly IFocusContextService _focusContextService;
        private readonly BookAnalysisService _bookAnalysisService;
        private readonly SpecLoader _specLoader;
        private void UpdateSourceBookName()
        {
            var book = BookOptions.FirstOrDefault(b => b.Id == FormBookAnalysisId);
            FormSourceBookName = book?.Name ?? string.Empty;

            if (!_genreManuallySet)
            {
                var genre = book?.Genre;
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    _suppressGenreManualMark = true;
                    FormGenre = genre;
                    _suppressGenreManualMark = false;
                }
            }
        }

        private void LoadBookOptions()
        {
            var analyses = _bookAnalysisService.GetAllAnalysis();
            BookOptions = analyses
                .Select(b => new BookAnalysisOption { Id = b.Id, Name = b.Name, Author = b.Author, Genre = b.Genre })
                .ToList();

            GenreOptions = LoadGenresFromSpec();
        }

        public void RefreshBookOptions() => LoadBookOptions();

        private List<GenreInfo> LoadGenresFromSpec()
        {
            try
            {
                var specTemplates = _promptRepository.GetAllTemplates()
                    .Where(t => t.Tags != null && t.Tags.Contains("Spec") && !string.IsNullOrWhiteSpace(t.Category))
                    .ToList();

                if (specTemplates.Count == 0)
                {
                    TM.App.Log("[CreativeMaterials] 未找到任何Spec模板，题材下拉为空");
                    return new List<GenreInfo>();
                }

                return specTemplates.Select(t => new GenreInfo
                {
                    Name = t.Category,
                    Icon = TM.Framework.Common.Helpers.IconHelper.TryGet(t.Icon),
                    Description = ExtractShortDescription(t.Description),
                    Elements = ExtractElements(t.SystemPrompt),
                    Avoidances = ExtractAvoidances(t.SystemPrompt),
                }).ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeMaterials] 读取Spec题材失败: {ex.Message}");
                return new List<GenreInfo>();
            }
        }

        private static string ExtractShortDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var idx = description.IndexOf('，');
            return idx > 0 && idx < description.Length - 1 ? description[(idx + 1)..] : description;
        }

        private static string ExtractElements(string? systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt)) return string.Empty;
            const string key = "【必须包含】";
            var start = systemPrompt.IndexOf(key);
            if (start < 0) return string.Empty;
            start += key.Length;
            var end = systemPrompt.IndexOf('\n', start);
            var raw = end > start ? systemPrompt[start..end] : systemPrompt[start..];
            return raw.Trim().Replace(",", "、");
        }

        private static string ExtractAvoidances(string? systemPrompt)
        {
            if (string.IsNullOrWhiteSpace(systemPrompt)) return string.Empty;
            const string key = "【必须避免】";
            var start = systemPrompt.IndexOf(key);
            if (start < 0) return string.Empty;
            start += key.Length;
            var end = systemPrompt.IndexOf('\n', start);
            var raw = end > start ? systemPrompt[start..end] : systemPrompt[start..];
            return raw.Trim().Replace(",", "、");
        }

        private GenreInfo? FindGenreInfo(string genreName)
        {
            if (string.IsNullOrWhiteSpace(genreName)) return null;
            return GenreOptions.FirstOrDefault(g => string.Equals(g.Name, genreName, StringComparison.OrdinalIgnoreCase));
        }

        public CreativeMaterialsViewModel(IPromptRepository promptRepository, IFocusContextService focusContextService, BookAnalysisService bookAnalysisService, SpecLoader specLoader)
        {
            _promptRepository = promptRepository;
            _focusContextService = focusContextService;
            _bookAnalysisService = bookAnalysisService;
            _specLoader = specLoader;
            FormSelectedGenres.CollectionChanged += OnFormSelectedGenresChanged;
        }

        protected override void OnAfterInitializeRefresh()
        {
            LoadBookOptions();
        }

    }
}
