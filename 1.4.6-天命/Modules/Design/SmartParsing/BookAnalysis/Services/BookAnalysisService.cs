using System;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public class BookAnalysisService : ModuleServiceBase<BookAnalysisCategory, BookAnalysisData>
    {
        public BookAnalysisService()
            : base(
                modulePath: "Design/SmartParsing/BookAnalysis",
                categoriesFileName: "categories.json",
                dataFileName: "book_analysis.json")
        {
        }

        public List<BookAnalysisData> GetAllAnalysis() => GetAllData();

        public void AddAnalysis(BookAnalysisData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;
            AddData(data);
        }

        public async System.Threading.Tasks.Task AddAnalysisAsync(BookAnalysisData data)
        {
            if (data == null) return;
            if (string.IsNullOrWhiteSpace(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }
            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;
            await AddDataAsync(data);
        }

        public void UpdateAnalysis(BookAnalysisData data)
        {
            if (data == null) return;
            data.ModifiedTime = DateTime.Now;
            UpdateData(data);
        }

        public void DeleteAnalysis(string id)
        {
            DeleteData(id);
        }

        public int ClearAllAnalysis()
        {
            var count = DataItems.Count;
            DataItems.Clear();
            SaveData();
            return count;
        }

        protected override int OnBeforeDeleteData(string dataId)
        {
            return DataItems.RemoveAll(d => d.Id == dataId);
        }
    }
}
