﻿using MaintenanceModeMiddleware.Configuration;
using MaintenanceModeMiddleware.Configuration.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MaintenanceModeMiddleware
{
    public class MaintenanceMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMaintenanceControlService _maintenanceCtrlSev;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly OptionCollection _startupOptions;
        private readonly MaintenanceResponse _response;

        public MaintenanceMiddleware(RequestDelegate next,
            IMaintenanceControlService maintenanceCtrlSev,
            IWebHostEnvironment webHostEnvironment,
            Action<MiddlewareOptionsBuilder> optionsBuilder)
        {
            _next = next;
            _maintenanceCtrlSev = maintenanceCtrlSev;
            _webHostEnvironment = webHostEnvironment;
            
            OptionCollection options = BuildOptions(optionsBuilder);
            VerifyOptions(options);
            _startupOptions = options;

            _response = GetMaintenanceResponse();

            // We should try to restore the state after all dependecies have been registered,
            // because some implementation of IStateStore may rely on a dependency, such is the case
            // with FileStateStore - it relies on a resolvable instance of IWebHostEnvironment.
            // That's why we are doing this here and not, for example, in the service's constructor.
            if (_maintenanceCtrlSev is ICanRestoreState iCanRestoreState)
            {
                iCanRestoreState.RestoreState();
            }
        }
        private OptionCollection BuildOptions(Action<MiddlewareOptionsBuilder> options)
        {
            MiddlewareOptionsBuilder optionsBuilder = new MiddlewareOptionsBuilder();
            options?.Invoke(optionsBuilder);
            if (!(optionsBuilder.Options.Any<UseNoDefaultValuesOption>()
                     && optionsBuilder.Options.Get<UseNoDefaultValuesOption>().Value))
            {
                optionsBuilder.FillEmptyOptionsWithDefault();
            }

            return optionsBuilder.Options;
        }

        private MaintenanceResponse GetMaintenanceResponse()
        {
            MaintenanceResponse response;

            if (_startupOptions.Any<UseDefaultResponseOption>() 
                && _startupOptions.Get<UseDefaultResponseOption>().Value)
            {
                Stream resStream = GetType()
                    .Assembly
                    .GetManifestResourceStream($"{nameof(MaintenanceModeMiddleware)}.Resources.DefaultResponse.html");
                if (resStream == null)
                {
                    throw new InvalidOperationException("The default response resource could not be found.");
                }

                using var resSr = new StreamReader(resStream, Encoding.UTF8);
                response = new MaintenanceResponse
                {
                    ContentBytes = resSr.CurrentEncoding.GetBytes(resSr.ReadToEnd()),
                    ContentEncoding = resSr.CurrentEncoding,
                    ContentType = ContentType.Html
                };
            }
            else if (_startupOptions.Any<ResponseOption>())
            {
                response = _startupOptions.Get<ResponseOption>().Value;
            }
            else if(_startupOptions.Any<ResponseFileOption>())
            {
                string absPath = GetAbsolutePathOfResponseFile();
                using StreamReader sr = new StreamReader(absPath, detectEncodingFromByteOrderMarks: true);

                response = new MaintenanceResponse
                {
                    ContentBytes = sr.CurrentEncoding.GetBytes(sr.ReadToEnd()),
                    ContentEncoding = sr.CurrentEncoding,
                    ContentType = absPath.EndsWith(".txt")
                        ? ContentType.Text : ContentType.Html
                };
            }
            else
            {
                throw new InvalidOperationException("Configuration error: No response was specified.");
            }

            return response;
        }

        private void VerifyOptions(OptionCollection options)
        {
            if (!options.Any<UseDefaultResponseOption>()
                && !options.Any<ResponseOption>()
                && !options.Any<ResponseFileOption>())
            {
                throw new InvalidOperationException("No response was specified.");
            }

            if (options.Any<ResponseFileOption>())
            {
                string absPath = GetAbsolutePathOfResponseFile();

                if (!File.Exists(absPath))
                {
                    throw new ArgumentException($"Could not find file {options.Get<ResponseFileOption>().Value.FilePath}. Expected absolute path: {absPath}.");
                }
            }

            if (!options.Any<Code503RetryIntervalOption>())
            {
                throw new ArgumentException("No value was specified for 503 retry interval.");
            }
        }

        private string GetAbsolutePathOfResponseFile()
        {
            ResponseFileOption resFileOption = _startupOptions.Get<ResponseFileOption>();
            if (resFileOption.Value.BaseDir == null)
            {
                return resFileOption.Value.FilePath;
            }

            string baseDir = resFileOption.Value.BaseDir == PathBaseDirectory.WebRootPath 
                ? _webHostEnvironment.WebRootPath 
                : _webHostEnvironment.ContentRootPath;
            
            return Path.Combine(baseDir, resFileOption.Value.FilePath);
        }

        private OptionCollection GetOptionCollection()
        {
            if (_maintenanceCtrlSev is ICanOverrideMiddlewareOptions optionsOverrider)
            {
                return optionsOverrider.GetOptionsToOverride();
            }

            return _startupOptions;
        }

        public async Task Invoke(HttpContext context)
        {
            if (!_maintenanceCtrlSev.IsMaintenanceModeOn)
            {
                goto nextDelegate;
            }

            OptionCollection options = GetOptionCollection();
            if (options == null)
            {
                goto nextDelegate;
            }

            if (options.GetAll<BypassUrlPathOption>().Any(o =>
                context.Request.Path.StartsWithSegments(
                    o.Value.String, o.Value.Comparison)))
            {
                goto nextDelegate;
            }

            if (options.GetAll<BypassFileExtensionOption>().Any(o =>
                context.Request.Path.Value.EndsWith(
                    $".{o.Value}", StringComparison.OrdinalIgnoreCase)))
            {
                goto nextDelegate;
            }

            if (options.Any<BypassAllAuthenticatedUsersOption>()
                && options.Get<BypassAllAuthenticatedUsersOption>().Value
                && context.User.Identity.IsAuthenticated)
            {
                goto nextDelegate;
            }

            if (options.GetAll<BypassUserNameOption>().Any(o =>
                o.Value == context.User.Identity.Name))
            {
                goto nextDelegate;
            }

            if (options.GetAll<BypassUserRoleOption>().Any(o =>
                context.User.IsInRole(o.Value)))
            {
                goto nextDelegate;
            }

            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            context.Response.Headers.Add("Retry-After", options.Get<Code503RetryIntervalOption>().Value.ToString());
            context.Response.ContentType = _response.GetContentTypeString();

            await context
                .Response
                .WriteAsync(_response.ContentEncoding.GetString(_response.ContentBytes),
                    _response.ContentEncoding);

            return;


        nextDelegate:
            await _next.Invoke(context);
        }
    }
}