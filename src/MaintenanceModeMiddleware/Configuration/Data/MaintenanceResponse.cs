﻿using MaintenanceModeMiddleware.Configuration.Enums;
using System;
using System.Text;

namespace MaintenanceModeMiddleware.Configuration.Data
{
    internal class MaintenanceResponse
    {
        public ContentType ContentType { get; set; }
        public Encoding ContentEncoding { get; set; }
        public byte[] ContentBytes { get; set; }
        public int Code503RetryInterval { get; set; } = 5300;

        public string GetContentTypeString()
        {
            return ContentType switch
            {
                ContentType.Html => "text/html",
                ContentType.Text => "text/plain",
                ContentType.Json => "application/json",
                _ => throw new InvalidOperationException("Content type could not be translated."),
            };
        }
    }
}
