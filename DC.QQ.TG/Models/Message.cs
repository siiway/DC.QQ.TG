using System;

namespace DC.QQ.TG.Models
{
    public enum MessageSource
    {
        Discord,
        QQ,
        Telegram,
        System
    }

    public class Message
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; }
        public string SenderName { get; set; }
        public string SenderId { get; set; }
        public MessageSource Source { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? ImageUrl { get; set; }
        public string? AvatarUrl { get; set; }

        // 新增属性，用于支持文件传输
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public string? FileType { get; set; } // 例如: "document", "audio", "video" 等

        /// <summary>
        /// Gets the formatted username in the format <user>@<platform>
        /// </summary>
        public string GetFormattedUsername()
        {
            string platform = Source switch
            {
                MessageSource.Discord => "discord",
                MessageSource.QQ => "qq",
                MessageSource.Telegram => "telegram",
                MessageSource.System => "system",
                _ => "unknown"
            };

            return $"{SenderName}@{platform}";
        }

        public override string ToString()
        {
            // Just return the content for compatibility
            return Content;
        }
    }
}
