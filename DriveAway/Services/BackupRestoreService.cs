using DriveAway.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO;
using System.Security.Cryptography;

namespace DriveAway.Services
{
    public class BackupRestoreService : IBackupRestoreService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly string _backupFolder;

        public BackupRestoreService(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _env = env;
            _configuration = configuration;
            _backupFolder = Path.Combine(_env.ContentRootPath, "Backups");

            if (!Directory.Exists(_backupFolder))
                Directory.CreateDirectory(_backupFolder);

            // Grant SQL Server service account write access to the Backups folder
            GrantSqlServerAccessToBackupFolder();
        }

        public async Task<BackupFileInfo> CreateBackupAsync()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var tempFileName = $"DriveAway_Backup_temp_{timestamp}.bak";
            var tempBackupPath = Path.Combine(_backupFolder, tempFileName);
            var finalFileName = $"DriveAway_Backup_{timestamp}.bak";
            var finalBackupPath = Path.Combine(_backupFolder, finalFileName);

            var dbName = GetDatabaseName();

            // SQL Server writes the unencrypted backup to the temp path
            var sql = @"
DECLARE @sql nvarchar(max) = N'BACKUP DATABASE ' + QUOTENAME(@dbName) + N' TO DISK = @filePath WITH FORMAT, INIT, NAME = @backupName';
EXEC sp_executesql @sql,
    N'@filePath nvarchar(4000), @backupName nvarchar(128), @dbName sysname',
    @filePath = @filePath,
    @backupName = @backupName,
    @dbName = @dbName;";

            var conn = _context.Database.GetDbConnection();
            var wasOpen = conn.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await conn.OpenAsync();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = 300; // 5 minutes for large databases

                cmd.Parameters.Add(new SqlParameter("@dbName", dbName));
                var filePathParam = new SqlParameter("@filePath", tempBackupPath);
                var backupNameParam = new SqlParameter("@backupName", $"DriveAway Full Backup {timestamp}");
                cmd.Parameters.Add(filePathParam);
                cmd.Parameters.Add(backupNameParam);

                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (!wasOpen)
                    await conn.CloseAsync();
            }

            try
            {
                // Encrypt the temp backup file to the final destination path
                var encryptionKey = _configuration["Backup:EncryptionKey"] ?? "DriveAwayBackupSecretEncryptionKey2026!";
                EncryptFile(tempBackupPath, finalBackupPath, encryptionKey);
            }
            finally
            {
                // Clean up the temporary plaintext backup file
                if (File.Exists(tempBackupPath))
                {
                    File.Delete(tempBackupPath);
                }
            }

            var fileInfo = new FileInfo(finalBackupPath);
            return new BackupFileInfo
            {
                FileName = finalFileName,
                SizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime
            };
        }

        public Task<List<BackupFileInfo>> GetBackupsAsync()
        {
            var backups = new List<BackupFileInfo>();

            if (Directory.Exists(_backupFolder))
            {
                var files = Directory.GetFiles(_backupFolder, "*.bak")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                foreach (var file in files)
                {
                    backups.Add(new BackupFileInfo
                    {
                        FileName = file.Name,
                        SizeBytes = file.Length,
                        CreatedAt = file.CreationTime
                    });
                }
            }

            return Task.FromResult(backups);
        }

        public async Task RestoreAsync(string fileName)
        {
            var encryptedFilePath = GetRequiredBackupFilePath(fileName);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var decryptedTempFilePath = Path.Combine(_backupFolder, $"DriveAway_Restore_temp_{timestamp}.bak");

            try
            {
                // Decrypt the file to a temporary location so SQL Server can read it
                var encryptionKey = _configuration["Backup:EncryptionKey"] ?? "DriveAwayBackupSecretEncryptionKey2026!";
                DecryptFile(encryptedFilePath, decryptedTempFilePath, encryptionKey);

                var dbName = GetDatabaseName();
                var masterConnStr = BuildMasterConnectionString();

                // We must connect to 'master' to restore, since we're replacing the target database.
                using var masterConn = new SqlConnection(masterConnStr);
                await masterConn.OpenAsync();

                // Step 1: Drop all connections by setting SINGLE_USER
                try
                {
                    using var kickCmd = masterConn.CreateCommand();
                    kickCmd.CommandText = @"
DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@dbName) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE';
EXEC sp_executesql @sql, N'@dbName sysname', @dbName = @dbName;";
                    kickCmd.CommandTimeout = 60;
                    kickCmd.Parameters.Add(new SqlParameter("@dbName", dbName));
                    await kickCmd.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Database might already be in single-user or not exist yet — continue
                }

                // Step 2: Restore the database from the decrypted temp file
                try
                {
                    using var restoreCmd = masterConn.CreateCommand();
                    restoreCmd.CommandText = @"
DECLARE @sql nvarchar(max) = N'RESTORE DATABASE ' + QUOTENAME(@dbName) + N' FROM DISK = @filePath WITH REPLACE';
EXEC sp_executesql @sql,
    N'@filePath nvarchar(4000), @dbName sysname',
    @filePath = @filePath,
    @dbName = @dbName;";
                    restoreCmd.CommandTimeout = 600; // 10 minutes for large databases
                    restoreCmd.Parameters.Add(new SqlParameter("@dbName", dbName));
                    restoreCmd.Parameters.Add(new SqlParameter("@filePath", decryptedTempFilePath));
                    await restoreCmd.ExecuteNonQueryAsync();
                }
                finally
                {
                    // Step 3: Set back to MULTI_USER
                    try
                    {
                        using var multiCmd = masterConn.CreateCommand();
                        multiCmd.CommandText = @"
DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@dbName) + N' SET MULTI_USER';
EXEC sp_executesql @sql, N'@dbName sysname', @dbName = @dbName;";
                        multiCmd.CommandTimeout = 30;
                        multiCmd.Parameters.Add(new SqlParameter("@dbName", dbName));
                        await multiCmd.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Best-effort recovery
                    }
                }
            }
            finally
            {
                // Clean up the temporary decrypted plaintext backup file
                if (File.Exists(decryptedTempFilePath))
                {
                    File.Delete(decryptedTempFilePath);
                }
            }
        }

        public Task DeleteBackupAsync(string fileName)
        {
            var filePath = GetRequiredBackupFilePath(fileName);

            File.Delete(filePath);
            return Task.CompletedTask;
        }

        public string? GetBackupFilePath(string fileName)
        {
            try { ValidateFileName(fileName); }
            catch { return null; }

            // Find the file in the backups folder (case-insensitive match)
            var candidate = Directory.EnumerateFiles(_backupFolder, "*.bak", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            if (candidate == null)
                return null;

            // Canonicalize and ensure the resolved path is under the expected backup folder
            try
            {
                var fullCandidate = Path.GetFullPath(candidate);
                var fullBackupFolder = Path.GetFullPath(_backupFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullCandidate.StartsWith(fullBackupFolder, StringComparison.OrdinalIgnoreCase))
                    return null;
                return fullCandidate;
            }
            catch
            {
                return null;
            }
        }

        // ── Helpers ──────────────────────────────────────────────

        private async Task EnsureBackupCertificateExistsAsync()
        {
            var masterConnStr = BuildMasterConnectionString();
            using var conn = new SqlConnection(masterConnStr);
            await conn.OpenAsync();

            var password = _configuration["Backup:MasterKeyPassword"] ?? "DriveAwaySecureBackupKeyPassword2026!";

            // 1. Check if database master key exists. If not, create it.
            var checkKeySql = "SELECT COUNT(*) FROM sys.symmetric_keys WHERE name = '##MS_DatabaseMasterKey##'";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = checkKeySql;
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using (var createKeyCmd = conn.CreateCommand())
                    {
                        createKeyCmd.CommandText = "CREATE MASTER KEY ENCRYPTION BY PASSWORD = @password";
                        createKeyCmd.Parameters.Add(new SqlParameter("@password", password));
                        await createKeyCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // 2. Check if certificate exists. If not, create it.
            var checkCertSql = "SELECT COUNT(*) FROM sys.certificates WHERE name = 'DriveAwayBackupCertificate'";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = checkCertSql;
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    using (var createCertCmd = conn.CreateCommand())
                    {
                        createCertCmd.CommandText = "CREATE CERTIFICATE DriveAwayBackupCertificate WITH SUBJECT = 'DriveAway Backup Encryption Certificate'";
                        await createCertCmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        public string GetDatabaseName()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string not found.");
            var builder = new SqlConnectionStringBuilder(connStr);
            return builder.InitialCatalog;
        }

        private string BuildMasterConnectionString()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string not found.");
            var builder = new SqlConnectionStringBuilder(connStr)
            {
                InitialCatalog = "master"
            };
            return builder.ConnectionString;
        }

        /// <summary>
        /// Grants the SQL Server service account write access to the app's Backups folder
        /// so that BACKUP DATABASE can write directly to it.
        /// </summary>
        private void GrantSqlServerAccessToBackupFolder()
        {
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                var dirInfo = new DirectoryInfo(_backupFolder);
                var acl = dirInfo.GetAccessControl();

                // Common SQL Server Express service account names
                var serviceAccounts = new[]
                {
                    @"NT Service\MSSQL$SQLEXPRESS",
                    @"NT AUTHORITY\NETWORK SERVICE",
                    @"BUILTIN\Users"
                };

                foreach (var account in serviceAccounts)
                {
                    try
                    {
                        var identity = new NTAccount(account);
                        acl.AddAccessRule(new FileSystemAccessRule(
                            identity,
                            FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None,
                            AccessControlType.Allow));
                    }
                    catch
                    {
                        // Account may not exist on this system — skip
                    }
                }

                dirInfo.SetAccessControl(acl);
            }
            catch
            {
                // If we can't set permissions (e.g. not running as admin), 
                // continue and let the backup attempt surface the real error.
            }
        }

        /// <summary>
        /// Validates the filename to prevent path-traversal attacks.
        /// Only allows .bak files that match the expected backup filename pattern.
        /// </summary>
        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Filename cannot be empty.");

            // Reject obvious traversal characters early
            if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
                throw new ArgumentException("Invalid filename.");

            // Only allow the exact backup filename pattern produced by CreateBackupAsync
            // e.g. DriveAway_Backup_20260521_031336.bak
            var allowedPattern = "^DriveAway_Backup_\\d{8}_\\d{6}\\.bak$";
            var allowedRegex = new System.Text.RegularExpressions.Regex(
                allowedPattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            if (!allowedRegex.IsMatch(fileName))
                throw new ArgumentException("Filename does not match expected backup pattern.");
        }

        private string GetRequiredBackupFilePath(string fileName)
        {
            ValidateFileName(fileName);

            var filePath = Directory.EnumerateFiles(_backupFolder, "*.bak", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

            if (filePath == null)
                throw new FileNotFoundException("Backup file not found.", fileName);

            return filePath;
        }

        private void EncryptFile(string inputFile, string outputFile, string keyString)
        {
            var keyBytes = DeriveKey(keyString, 32); // 256 bits
            var ivBytes = DeriveKey(keyString, 16);  // 128 bits

            using (var aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;

                using (var fsCrypt = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                {
                    using (var cryptoStream = new CryptoStream(fsCrypt, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        using (var fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                        {
                            fsIn.CopyTo(cryptoStream);
                        }
                    }
                }
            }
        }

        private void DecryptFile(string inputFile, string outputFile, string keyString)
        {
            var keyBytes = DeriveKey(keyString, 32);
            var ivBytes = DeriveKey(keyString, 16);

            using (var aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;

                using (var fsCrypt = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
                {
                    using (var cryptoStream = new CryptoStream(fsCrypt, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        using (var fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                        {
                            cryptoStream.CopyTo(fsOut);
                        }
                    }
                }
            }
        }

        private byte[] DeriveKey(string keyString, int length)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keyString));
                var result = new byte[length];
                Array.Copy(hash, result, Math.Min(length, hash.Length));
                return result;
            }
        }
    }
}
