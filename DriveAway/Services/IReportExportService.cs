using System.Data;

namespace DriveAway.Services
{
    public interface IReportExportService
    {
        byte[] ExportToExcel(DataTable data, string reportName);
        byte[] ExportToPdf(string htmlContent, string reportName);
        byte[] ExportToPdfFromDataTable(DataTable data, string reportName);
    }
}
