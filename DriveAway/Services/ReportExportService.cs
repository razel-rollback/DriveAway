using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace DriveAway.Services
{
    public class ReportExportService : IReportExportService
    {
        public ReportExportService()
        {
            // Required for QuestPDF community license
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] ExportToExcel(DataTable data, string reportName)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(reportName);

            // Add Title
            worksheet.Cell(1, 1).Value = reportName;
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 16;
            worksheet.Range(1, 1, 1, data.Columns.Count).Merge();

            // Add generated date
            worksheet.Cell(2, 1).Value = $"Generated: {DateTime.Now:MMM dd, yyyy HH:mm}";
            worksheet.Cell(2, 1).Style.Font.Italic = true;
            worksheet.Range(2, 1, 2, data.Columns.Count).Merge();

            // Insert DataTable starting at row 4
            var tableNode = worksheet.Cell(4, 1).InsertTable(data);
            tableNode.Theme = XLTableTheme.TableStyleMedium2;

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportToPdf(string htmlContent, string reportName)
        {
            // QuestPDF does not inherently convert HTML to PDF directly in a simple way without writing a custom parser.
            // Since the user requested an API approach and we chose QuestPDF, we will implement a data-driven PDF generation instead.
            // This method is a placeholder if we were using DinkToPdf or similar.
            throw new NotImplementedException("Use ExportToPdfFromDataTable for QuestPDF generation.");
        }

        public byte[] ExportToPdfFromDataTable(DataTable data, string reportName)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComposeHeader(c, reportName));
                    page.Content().Element(c => ComposeTable(c, data));
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });

            using var stream = new MemoryStream();
            document.GeneratePdf(stream);
            return stream.ToArray();
        }

        private void ComposeHeader(IContainer container, string reportName)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(reportName).FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                    column.Item().Text($"Generated: {DateTime.Now:MMM dd, yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Medium);
                });
            });
        }

        private void ComposeTable(IContainer container, DataTable data)
        {
            container.PaddingTop(1, Unit.Centimetre).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (int i = 0; i < data.Columns.Count; i++)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    foreach (DataColumn column in data.Columns)
                    {
                        header.Cell().Background(Colors.Grey.Lighten2)
                              .Padding(2).Text(column.ColumnName).SemiBold();
                    }
                });

                foreach (DataRow row in data.Rows)
                {
                    foreach (var item in row.ItemArray)
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                             .Padding(2).Text(item?.ToString() ?? string.Empty);
                    }
                }
            });
        }
    }
}
