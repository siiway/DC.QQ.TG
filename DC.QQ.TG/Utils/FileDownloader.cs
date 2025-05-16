using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DC.QQ.TG.Utils
{
    /// <summary>
    /// 提供文件下载和临时文件管理功能
    /// </summary>
    public static class FileDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DC.QQ.TG", "TempFiles");
        
        /// <summary>
        /// 下载文件到临时目录
        /// </summary>
        /// <param name="url">文件的 URL</param>
        /// <param name="fileName">文件名（如果为 null，将从 URL 中提取）</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>本地文件路径</returns>
        public static async Task<string> DownloadFileAsync(string url, string fileName, ILogger logger, CancellationToken cancellationToken = default)
        {
            try
            {
                // 如果 URL 已经是本地文件路径，直接返回
                if (url.StartsWith("file://"))
                {
                    return url;
                }
                
                // 确保临时目录存在
                Directory.CreateDirectory(_tempDir);
                
                // 如果没有提供文件名，从 URL 中提取
                if (string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        fileName = Path.GetFileName(new Uri(url).LocalPath);
                    }
                    catch
                    {
                        fileName = $"file_{DateTime.Now.Ticks}";
                    }
                }
                
                // 添加时间戳，避免文件名冲突
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string safeFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
                
                // 构建完整的文件路径
                string filePath = Path.Combine(_tempDir, safeFileName);
                
                // 下载文件
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await response.Content.CopyToAsync(fileStream, cancellationToken);
                }
                
                logger.LogInformation("Downloaded file from {Url} to {FilePath}", url, filePath);
                
                // 设置定时清理任务
                ScheduleCleanup(filePath, logger);
                
                return $"file://{filePath}";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error downloading file from {Url}", url);
                return url; // 如果下载失败，返回原始 URL
            }
        }
        
        /// <summary>
        /// 安排定时清理任务，在指定时间后删除临时文件
        /// </summary>
        private static void ScheduleCleanup(string filePath, ILogger logger)
        {
            // 在 30 分钟后删除临时文件
            _ = Task.Delay(TimeSpan.FromMinutes(30)).ContinueWith(_ =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        logger.LogDebug("Deleted temporary file: {FilePath}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting temporary file: {FilePath}", filePath);
                }
            });
        }
        
        /// <summary>
        /// 清理所有临时文件
        /// </summary>
        public static void CleanupAllFiles(ILogger logger)
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    foreach (var file in Directory.GetFiles(_tempDir))
                    {
                        try
                        {
                            File.Delete(file);
                            logger.LogDebug("Deleted temporary file: {FilePath}", file);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error deleting temporary file: {FilePath}", file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cleaning up temporary directory: {TempDir}", _tempDir);
            }
        }
    }
}
