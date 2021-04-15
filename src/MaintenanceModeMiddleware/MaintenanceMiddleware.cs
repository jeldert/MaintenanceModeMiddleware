﻿using MaintenanceModeMiddleware.Configuration;
using MaintenanceModeMiddleware.Configuration.Builders;
using MaintenanceModeMiddleware.Configuration.Data;
using MaintenanceModeMiddleware.Configuration.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MaintenanceModeMiddleware
{
    internal class MaintenanceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMaintenanceControlService _maintenanceCtrlSev;
        private readonly ICanOverrideMiddlewareOptions _optionsOverriderSvc;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly OptionCollection _startupOptions;

        public MaintenanceMiddleware(RequestDelegate next,
            IMaintenanceControlService maintenanceCtrlSev,
            IWebHostEnvironment webHostEnvironment,
            Action<MiddlewareOptionsBuilder> options)
        {
            _next = next;
            _maintenanceCtrlSev = maintenanceCtrlSev;
            _webHostEnvironment = webHostEnvironment;

            if (maintenanceCtrlSev is ICanOverrideMiddlewareOptions overriderSvc)
            {
                _optionsOverriderSvc = overriderSvc;
            }

            var optionsBuilder = new MiddlewareOptionsBuilder();
            options?.Invoke(optionsBuilder);
            _startupOptions = optionsBuilder.GetOptions();

            _startupOptions
                .GetSingleOrDefault<IResponseHolder>()
                .Verify(webHostEnvironment);

            RestoreMaintenanceState();
        }

        public async Task Invoke(HttpContext context)
        {
            if (!_maintenanceCtrlSev.IsMaintenanceModeOn)
            {
                await _next.Invoke(context);
                return;
            }

            OptionCollection options = _optionsOverriderSvc
                ?.GetOptionsToOverride()
                ?? _startupOptions;

            if (options.GetAll<IAllowedRequestMatcher>()
                .Any(matcher => matcher.IsMatch(context)))
            {
                await _next.Invoke(context);
                return;
            }

            int retryAfterInterval = options
                .GetSingleOrDefault<Code503RetryIntervalOption>()
                .Value;

            MaintenanceResponse response = options
                .GetSingleOrDefault<IResponseHolder>()
                .GetResponse(_webHostEnvironment);

            await WriteMaintenanceResponse(context, retryAfterInterval, response);
        }

        private async Task WriteMaintenanceResponse(HttpContext context, 
            int retryAfterInterval, 
            MaintenanceResponse response)
        {

            context
                .Response
                .StatusCode = (int)HttpStatusCode.ServiceUnavailable;

            context
                .Response
                .Headers
                .Add("Retry-After", retryAfterInterval.ToString());

            context
                .Response
                .ContentType = response.GetContentTypeString();

            string responseStr = response
                .ContentEncoding
                .GetString(response.ContentBytes);

            await context
                .Response
                .WriteAsync(responseStr,
                    response.ContentEncoding);
        }

        private void RestoreMaintenanceState()
        {
            if (_maintenanceCtrlSev is ICanRestoreState iCanRestoreState)
            {
                iCanRestoreState.RestoreState();
            }
        }
    }
}