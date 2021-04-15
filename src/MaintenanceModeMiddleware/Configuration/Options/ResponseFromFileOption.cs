﻿using MaintenanceModeMiddleware.Configuration.Data;
using MaintenanceModeMiddleware.Configuration.Enums;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;

namespace MaintenanceModeMiddleware.Configuration.Options
{
    internal class ResponseFromFileOption : Option<FileMaintenanceResponse>, IResponseHolder
    {
        private const char PARTS_SEPARATOR = ';';

        internal ResponseFromFileOption() { }

        internal ResponseFromFileOption(string filePath, PathBaseDirectory baseDir, int code503RetryInterval)
        {
            SetValue(filePath, baseDir, code503RetryInterval);
        }

        public override void LoadFromString(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                throw new ArgumentNullException(nameof(str));
            }

            string[] parts = str.Split(PARTS_SEPARATOR);
            if (parts.Length != 3)
            {
                throw new FormatException($"{nameof(str)} is in incorrect format.");
            }

            if (!Enum.TryParse(parts[0], out PathBaseDirectory baseDir))
            {
                throw new ArgumentException($"Unknown base directory type {parts[0]}");
            }

            if (!int.TryParse(parts[2], out int code503RetryInterval))
            {
                throw new ArgumentException("Unable to parse the code 503 retry interval.");
            }

            SetValue(parts[1], baseDir, code503RetryInterval);
        }

        private void SetValue(string filePath, PathBaseDirectory baseDir, int code503RetryInterval)
        {
            if (!TryGetContentType(filePath, out _))
            {
                string fileName = Path.GetFileName(filePath);
                throw new ArgumentException($"The file, specified in {filePath} must have one of the following extensions: .txt, .html or .json.");
            }

            Value = new FileMaintenanceResponse
            {
                File = new FileDescriptor(filePath, baseDir),
                Code503RetryInterval = code503RetryInterval
            };
        }

        public override string GetStringValue()
        {
            return $"{Value.File.BaseDir}{PARTS_SEPARATOR}{Value.File.Path}{PARTS_SEPARATOR}{Value.Code503RetryInterval}";
        }

        public MaintenanceResponse GetResponse(IWebHostEnvironment webHostEnv)
        {
            string fullPath = GetFileFullPath(webHostEnv);

            using (StreamReader sr = new StreamReader(fullPath,
                detectEncodingFromByteOrderMarks: true))
            {
                TryGetContentType(fullPath, out ContentType? contentType);

                return new MaintenanceResponse
                {
                    ContentBytes = sr.CurrentEncoding.GetBytes(sr.ReadToEnd()),
                    ContentEncoding = sr.CurrentEncoding,
                    ContentType = contentType.Value,
                    Code503RetryInterval = Value.Code503RetryInterval
                };
            }
        }

        private bool TryGetContentType(string filePath, out ContentType? contentType)
        {
            string extension = Path.GetExtension(filePath)
                .ToLower();

            contentType = extension switch
            {
                ".txt" => ContentType.Text,
                ".html" => ContentType.Html,
                ".json" => ContentType.Json,
                _ => null,
            };

            return contentType.HasValue;
        }

        private string GetFileFullPath(IWebHostEnvironment webHostEnv)
        {
            string baseDir = Value.File.BaseDir == PathBaseDirectory.WebRootPath
                               ? webHostEnv.WebRootPath
                               : webHostEnv.ContentRootPath;

            string absPath = Path.Combine(baseDir, Value.File.Path);
            return absPath;
        }
    }
}
