namespace DriveAway.Services
{
    public class BackupFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public interface IBackupRestoreService
    {
        /// <summary>
        /// Creates a timestamped .bak backup of the database.
        /// Returns the backup file info on success.
        /// </summary>
        Task<BackupFileInfo> CreateBackupAsync();

        /// <summary>
        /// Lists all existing .bak files in the Backups folder.
        /// </summary>
        Task<List<BackupFileInfo>> GetBackupsAsync();

        /// <summary>
        /// Restores the database from the specified .bak file.
        /// This is a destructive operation – all current data will be replaced.
        /// </summary>
        Task RestoreAsync(string fileName);

        /// <summary>
        /// Deletes a backup .bak file from the Backups folder.
        /// </summary>
        Task DeleteBackupAsync(string fileName);

        /// <summary>
        /// Returns the full file path for a given backup filename (for download).
        /// Returns null if the file does not exist.
        /// </summary>
        string? GetBackupFilePath(string fileName);

        /// <summary>
        /// Returns the name of the database that is currently being targeted.
        /// </summary>
        string GetDatabaseName();
    }
}
